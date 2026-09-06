using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using WindowsInput;
using WindowsInput.Native;

namespace SmartPaste
{
    /// <summary>
    /// Smart Copy engine - captures web content, builds a ContentPackage,
    /// saves it to FormatCache, and sets a multi-format clipboard.
    ///
    /// Every captured image (remote, data: URI or inline SVG) is embedded in the
    /// HTML fragment as a data: URI, so the CF_HTML placed on the clipboard is
    /// fully self-contained: browsers, Electron apps and mail clients reject
    /// file:// references, and remote URLs break offline or behind hotlink checks.
    ///
    /// When SmartPaste detects a tagged clipboard (CopyId), it reads the
    /// ContentPackage and injects the optimal format for the target app.
    /// </summary>
    public class SmartCopyManager : IDisposable
    {
        private GlobalHotkey? _hotkey;
        private readonly InputSimulator _simulator = new InputSimulator();

        private static readonly HttpClient _httpClient = CreateHttpClient();

        private const int MaxImageBytes = 10 * 1024 * 1024;
        private const int MaxImages = 50;
        private const int MaxRtfImageBytes = 5 * 1024 * 1024;
        private const int MaxConcurrentDownloads = 6;
        private const int DownloadTimeoutSeconds = 15;

        /// <summary>Upper bound for the source app to publish the copied selection.</summary>
        private const int CopyTimeoutMs = 1500;
        private const int KeyReleaseTimeoutMs = 300;

        /// <summary>
        /// Placeholder scheme used in the intermediate fragment ("smartpaste-image://N").
        /// Each tag is rewritten individually to a placeholder first; the RTF builder
        /// maps placeholders to images cheaply, and the final HTML substitutes them
        /// with data: URIs.
        /// </summary>
        private const string PlaceholderScheme = "smartpaste-image://";

        /// <summary>U+FEFF - stripped before sniffing / normalizing SVG text.</summary>
        private const char ByteOrderMark = (char)0xFEFF;

        // Browser-like headers: many CDNs answer 403 to a bare .NET client.
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
        private const string AcceptImages =
            "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8";

        // Image "sites" in the fragment: inline <svg> elements (nesting-aware) and <img> tags
        private static readonly Regex SiteStartRegex = new(
            @"<(?<svg>svg\b)|<(?<img>img\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SvgTagRegex = new(
            @"<(?<close>/)?svg\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ImgTagRegex = new(
            @"<img\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "src" only - never "data-src" / "srcset"
        private static readonly Regex SrcAttrRegex = new(
            @"(?<![\w-])src\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ResponsiveAttrRegex = new(
            @"\s+(?:srcset|sizes)\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PlaceholderRegex = new(
            Regex.Escape(PlaceholderScheme) + @"(\d+)",
            RegexOptions.Compiled);


        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = MaxImageBytes
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd(AcceptImages);
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            return client;
        }


        private sealed class CachedImage
        {
            public int Index { get; init; }
            public string FileName { get; init; } = "";
            public string MimeType { get; init; } = "image/png";
            public byte[] Data { get; init; } = Array.Empty<byte>();
            public int Width { get; init; }
            public int Height { get; init; }
            public bool IsSvg { get; init; }

            public string Placeholder => PlaceholderScheme + Index.ToString(CultureInfo.InvariantCulture);
            public string DataUri => "data:" + MimeType + ";base64," + Convert.ToBase64String(Data);
        }

        /// <summary>Raw image payload before it is numbered and packaged.</summary>
        private sealed class RawImage
        {
            public RawImage(byte[] data, string mimeType, bool isSvg)
            {
                Data = data;
                MimeType = mimeType;
                IsSvg = isSvg;
            }

            public byte[] Data { get; }
            public string MimeType { get; }
            public bool IsSvg { get; }
        }

        /// <summary>An image location in the fragment: an inline &lt;svg&gt; element or an &lt;img&gt; tag.</summary>
        private sealed record ImageSite(int Position, int Length, bool IsInlineSvg, string Src);


        public void RegisterHotkey(IntPtr hwnd, string shortcut)
        {
            UnregisterHotkey();
            if (ShortcutParser.TryParse(shortcut, out uint modifiers, out VirtualKeyCode key))
            {
                _hotkey = new GlobalHotkey(modifiers, (uint)key, hwnd, 9006);
                _hotkey.HotkeyPressed += (s, e) => PerformSmartCopy();
            }
        }

        public void UnregisterHotkey()
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }


        private async void PerformSmartCopy()
        {
            try
            {
                // Let Shift/Alt/Win from the hotkey go up so the source app receives a clean Ctrl+C
                await ClipboardSync.WaitForChordReleaseAsync(KeyReleaseTimeoutMs);

                uint sequenceBefore = ClipboardSync.GetSequenceNumber();
                _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);

                // Proceed as soon as the source app has published the selection
                if (!await ClipboardSync.WaitForChangeAsync(sequenceBefore, CopyTimeoutMs))
                    return; // nothing was copied - leave the clipboard untouched

                IDataObject? clip = await GetClipboardAsync();
                if (clip == null) return;

                // Snapshot originals (prefer the alpha-preserving "PNG" clipboard format)
                var originalBitmap = GetClipboardImageWithAlpha(clip);
                string? originalText = clip.GetDataPresent(DataFormats.UnicodeText)
                    ? clip.GetData(DataFormats.UnicodeText) as string
                    : clip.GetDataPresent(DataFormats.Text)
                        ? clip.GetData(DataFormats.Text) as string : null;
                string? originalRtf = clip.GetDataPresent(DataFormats.Rtf)
                    ? clip.GetData(DataFormats.Rtf) as string : null;

                bool hasHtml = clip.GetDataPresent(DataFormats.Html);
                string? rawHtml = hasHtml ? clip.GetData(DataFormats.Html) as string : null;

                // No usable HTML: enhance an image-only copy (screenshot, image viewer) when a
                // bitmap is present, otherwise there is nothing rich to enhance.
                if (string.IsNullOrEmpty(rawHtml))
                {
                    if (originalBitmap != null)
                        await CaptureImageOnlyAsync(originalBitmap, originalText, originalRtf);
                    return;
                }

                // ── Process HTML → self-contained fragment + images (off the UI thread) ──
                var result = await Task.Run(() => ProcessHtmlAsync(rawHtml!));

                // Build ContentPackage
                var package = new ContentPackage
                {
                    Type = result.Images.Count > 0
                        ? (string.IsNullOrWhiteSpace(originalText) ? ContentType.SingleImage : ContentType.Mixed)
                        : ContentType.RichHtml,
                    SourceUrl = result.SourceUrl,
                    HtmlFragment = result.Fragment,
                    RtfContent = result.Rtf,
                    PlainText = originalText,
                    Images = result.Images.Select(i => new PackagedImage
                    {
                        FileName = i.FileName,
                        MimeType = i.MimeType,
                        Width = i.Width,
                        Height = i.Height,
                        IsSvg = i.IsSvg,
                        Data = i.Data
                    }).ToList()
                };

                // Save to FormatCache (the structured disk cache)
                await Task.Run(() => FormatCache.Save(package));

                // Save selection bitmap
                if (originalBitmap != null)
                    SaveBitmap(originalBitmap);

                // ── Build multi-format clipboard (passive Ctrl+V) ──
                var data = new DataObject();

                // Tag with CopyId so SmartPaste can identify this content
                data.SetData(FormatCache.CopyIdFormat, package.Id);

                // Self-contained CF_HTML (data: URI images) - portable across
                // browsers, Electron apps and mail clients
                string cfHtml = FormatCache.BuildCFHtml(result.Fragment, result.SourceUrl);
                data.SetData(DataFormats.Html, cfHtml);

                // CF_RTF with embedded raster images (fallback for RTF-only consumers)
                if (!string.IsNullOrEmpty(result.Rtf))
                    data.SetData(DataFormats.Rtf, result.Rtf);
                else if (originalRtf != null)
                    data.SetData(DataFormats.Rtf, originalRtf);

                // CF_BITMAP (browser render - universal fallback)
                if (originalBitmap != null)
                    data.SetImage(originalBitmap);

                // CF_TEXT
                if (originalText != null)
                    data.SetData(DataFormats.UnicodeText, originalText);

                await SetClipboardAsync(data);
            }
            catch (Exception ex)
            {
                // On failure the original clipboard is untouched
                System.Diagnostics.Debug.WriteLine($"SmartCopy failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds and publishes a SingleImage package from an image-only clipboard (no HTML):
        /// a self-contained CF_HTML fragment (data: URI), RTF with the raster \pict, the bitmap,
        /// so a copied screenshot / image pastes richly into any target.
        /// </summary>
        private async Task CaptureImageOnlyAsync(BitmapSource bitmap, string? originalText, string? originalRtf)
        {
            byte[] png = EncodePng(bitmap);
            if (png.Length == 0) return;

            var result = await Task.Run(() => BuildSingleImageResult(png));

            var package = new ContentPackage
            {
                Type = ContentType.SingleImage,
                HtmlFragment = result.Fragment,
                RtfContent = result.Rtf,
                PlainText = originalText,
                Images = result.Images.Select(i => new PackagedImage
                {
                    FileName = i.FileName,
                    MimeType = i.MimeType,
                    Width = i.Width,
                    Height = i.Height,
                    IsSvg = i.IsSvg,
                    Data = i.Data
                }).ToList()
            };

            await Task.Run(() => FormatCache.Save(package));
            SaveBitmap(bitmap);

            var data = new DataObject();
            data.SetData(FormatCache.CopyIdFormat, package.Id);
            data.SetData(DataFormats.Html, FormatCache.BuildCFHtml(result.Fragment, null));
            if (!string.IsNullOrEmpty(result.Rtf))
                data.SetData(DataFormats.Rtf, result.Rtf);
            else if (originalRtf != null)
                data.SetData(DataFormats.Rtf, originalRtf);
            data.SetImage(bitmap);
            if (originalText != null)
                data.SetData(DataFormats.UnicodeText, originalText);

            await SetClipboardAsync(data);
        }

        /// <summary>Builds a self-contained fragment + RTF from a single raster image.</summary>
        private static ProcessResult BuildSingleImageResult(byte[] pngBytes)
        {
            var image = CreateCachedImage(new RawImage(pngBytes, "image/png", false), 0);
            var images = new List<CachedImage> { image };
            string placeholderFragment = BuildImgTag(image.Placeholder, image.Width, image.Height);
            string rtf = BuildRtf(placeholderFragment, images);
            string html = placeholderFragment.Replace(image.Placeholder, image.DataUri);
            return new ProcessResult(html, null, rtf, images);
        }

        private static byte[] EncodePng(BitmapSource bitmap)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            catch { return Array.Empty<byte>(); }
        }

        /// <summary>
        /// Reads the clipboard image, preferring the "PNG" format (Chrome/Figma/modern apps)
        /// which keeps the alpha channel; falls back to CF_DIB via GetImage() (no transparency).
        /// </summary>
        private static BitmapSource? GetClipboardImageWithAlpha(IDataObject clip)
        {
            try
            {
                if (clip.GetDataPresent("PNG") && clip.GetData("PNG") is MemoryStream ms && ms.Length > 0)
                {
                    ms.Position = 0;
                    var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (dec.Frames.Count > 0) return dec.Frames[0];
                }
            }
            catch { }
            try { return clip.GetDataPresent(DataFormats.Bitmap) ? Clipboard.GetImage() : null; }
            catch { return null; }
        }

        private static void SaveBitmap(BitmapSource bitmap)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(FormatCache.SelectionBitmapPath);
                encoder.Save(stream);
            }
            catch { }
        }

        /// <summary>Reads the clipboard, retrying while the source app still holds it.</summary>
        private static async Task<IDataObject?> GetClipboardAsync()
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try { return Clipboard.GetDataObject(); }
                catch (ExternalException) { await Task.Delay(40); }
            }
            return null;
        }

        private static async Task SetClipboardAsync(DataObject data)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { Clipboard.SetDataObject(data, true); return; }
                catch (ExternalException) { await Task.Delay(100); }
            }
        }


        private sealed record ProcessResult(
            string Fragment,
            string? SourceUrl,
            string Rtf,
            List<CachedImage> Images);

        /// <summary>
        /// Turns raw CF_HTML into a self-contained fragment:
        ///   1. inline &lt;svg&gt; blocks and &lt;img&gt; tags are collected in document order,
        ///   2. remote images are downloaded in parallel (bounded), data: URIs are decoded,
        ///      SVG is kept as vector data,
        ///   3. each tag is rewritten individually to a placeholder src (srcset/sizes dropped),
        ///   4. RTF is built from the placeholder fragment (raster images only),
        ///   5. placeholders are substituted with data: URIs for the final HTML.
        /// </summary>
        private static async Task<ProcessResult> ProcessHtmlAsync(string rawHtml)
        {
            // Source URL
            string? sourceUrl = null;
            var urlMatch = Regex.Match(rawHtml, @"SourceURL:(.+?)[\r\n]");
            if (urlMatch.Success) sourceUrl = urlMatch.Groups[1].Value.Trim();

            // Fragment
            string fragment = rawHtml;
            var fragMatch = Regex.Match(rawHtml,
                @"<!--StartFragment-->(.*?)<!--EndFragment-->",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (fragMatch.Success) fragment = fragMatch.Groups[1].Value;

            // ── 1. Collect image sites in document order ──
            List<ImageSite> sites = CollectImageSites(fragment);

            // ── 2. Resolve sites → payloads (inline/data: synchronously, remote in parallel) ──
            var payloads = new RawImage?[sites.Count];
            var downloads = new Dictionary<string, Task<RawImage?>>(StringComparer.Ordinal);
            var pendingSites = new List<(int siteIndex, Task<RawImage?> task)>();
            int candidates = 0;

            using (var gate = new SemaphoreSlim(MaxConcurrentDownloads, MaxConcurrentDownloads))
            {
                for (int i = 0; i < sites.Count && candidates < MaxImages; i++)
                {
                    var site = sites[i];
                    if (site.IsInlineSvg)
                    {
                        payloads[i] = RawImageFromSvg(fragment.Substring(site.Position, site.Length));
                        candidates++;
                        continue;
                    }

                    string src = site.Src;
                    if (src.Length == 0) continue;
                    if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        payloads[i] = RawImageFromDataUri(src);
                        candidates++;
                        continue;
                    }
                    // Local paths are neither portable nor reachable from here
                    if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;

                    string url = ResolveUrl(src, sourceUrl);
                    if (url.Length == 0) continue;

                    if (!downloads.TryGetValue(url, out var task))
                    {
                        task = DownloadImageAsync(url, sourceUrl, gate);
                        downloads[url] = task;
                        candidates++;
                    }
                    pendingSites.Add((i, task));
                }

                await Task.WhenAll(downloads.Values);
            }

            foreach (var (siteIndex, task) in pendingSites)
                payloads[siteIndex] = await task;

            // ── 3. Number images in document order (a shared payload keeps one index) ──
            var images = new List<CachedImage>();
            var imageByPayload = new Dictionary<RawImage, CachedImage>();
            var siteImages = new CachedImage?[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                var payload = payloads[i];
                if (payload == null) continue;
                if (!imageByPayload.TryGetValue(payload, out var image))
                {
                    image = CreateCachedImage(payload, images.Count);
                    images.Add(image);
                    imageByPayload[payload] = image;
                }
                siteImages[i] = image;
            }

            // Rewrite each site individually; unresolved sites are left untouched
            var rewritten = new StringBuilder(fragment.Length);
            int cursor = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                var image = siteImages[i];
                rewritten.Append(fragment, cursor, site.Position - cursor);
                if (image == null)
                    rewritten.Append(fragment, site.Position, site.Length);
                else if (site.IsInlineSvg)
                    rewritten.Append(BuildImgTag(image.Placeholder, image.Width, image.Height));
                else
                    rewritten.Append(RewriteImgTag(fragment.Substring(site.Position, site.Length), image.Placeholder));
                cursor = site.Position + site.Length;
            }
            rewritten.Append(fragment, cursor, fragment.Length - cursor);
            string placeholderFragment = rewritten.ToString();

            // ── 4./5. RTF from placeholders, final HTML with data: URIs ──
            string rtf = BuildRtf(placeholderFragment, images);
            string html = PlaceholderRegex.Replace(placeholderFragment, m =>
                int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index < images.Count
                    ? images[index].DataUri
                    : m.Value);

            return new ProcessResult(html, sourceUrl, rtf, images);
        }

        private static CachedImage CreateCachedImage(RawImage payload, int index)
        {
            var (w, h) = payload.IsSvg
                ? ParseSvgDimensions(Encoding.UTF8.GetString(payload.Data))
                : ReadImageDimensions(payload.Data);

            string fileName = payload.IsSvg
                ? $"svg_{index:000}.svg"
                : $"img_{index:000}{MimeToExtension(payload.MimeType)}";

            return new CachedImage
            {
                Index = index,
                FileName = fileName,
                MimeType = payload.MimeType,
                Data = payload.Data,
                Width = w,
                Height = h,
                IsSvg = payload.IsSvg
            };
        }

        /// <summary>Finds inline &lt;svg&gt; elements and &lt;img&gt; tags in document order.</summary>
        private static List<ImageSite> CollectImageSites(string fragment)
        {
            var sites = new List<ImageSite>();
            int position = 0;
            while (position < fragment.Length)
            {
                var start = SiteStartRegex.Match(fragment, position);
                if (!start.Success) break;

                if (start.Groups["svg"].Success)
                {
                    int end = FindSvgEnd(fragment, start.Index);
                    if (end < 0)
                    {
                        position = start.Index + 1;   // unbalanced markup - skip this opening tag
                        continue;
                    }
                    sites.Add(new ImageSite(start.Index, end - start.Index, true, ""));
                    position = end;
                }
                else
                {
                    var tag = ImgTagRegex.Match(fragment, start.Index);
                    if (!tag.Success || tag.Index != start.Index)
                    {
                        position = start.Index + 1;
                        continue;
                    }
                    string src = "";
                    var srcMatch = SrcAttrRegex.Match(tag.Value);
                    if (srcMatch.Success)
                        src = WebUtility.HtmlDecode(srcMatch.Groups["v"].Value).Trim();
                    sites.Add(new ImageSite(tag.Index, tag.Length, false, src));
                    position = tag.Index + tag.Length;
                }
            }
            return sites;
        }

        /// <summary>
        /// End offset (exclusive) of the &lt;svg&gt; element opening at <paramref name="start"/>,
        /// honoring nested svg elements. Returns -1 when the element is never closed.
        /// </summary>
        private static int FindSvgEnd(string fragment, int start)
        {
            int depth = 0;
            for (var tag = SvgTagRegex.Match(fragment, start); tag.Success; tag = tag.NextMatch())
            {
                if (tag.Groups["close"].Success)
                {
                    if (--depth <= 0) return tag.Index + tag.Length;
                }
                else if (tag.Value.EndsWith("/>", StringComparison.Ordinal))
                {
                    if (depth == 0) return tag.Index + tag.Length;   // self-closing root
                }
                else
                {
                    depth++;
                }
            }
            return -1;
        }

        private static string BuildImgTag(string src, int width, int height) =>
            $"<img src=\"{src}\" width=\"{width}\" height=\"{height}\" alt=\"SVG\" />";

        /// <summary>
        /// Rewrites ONE &lt;img&gt; tag: only its src changes. srcset/sizes are dropped
        /// so a consumer cannot prefer a remote candidate over the embedded data.
        /// </summary>
        private static string RewriteImgTag(string tag, string newSrc)
        {
            string stripped = ResponsiveAttrRegex.Replace(tag, "");
            return SrcAttrRegex.Replace(stripped, _ => "src=\"" + newSrc + "\"", 1);
        }


        private static RawImage? RawImageFromSvg(string svgMarkup)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(NormalizeSvg(svgMarkup));
                return bytes.Length <= MaxImageBytes ? new RawImage(bytes, "image/svg+xml", true) : null;
            }
            catch { return null; }
        }

        private static RawImage? RawImageFromDataUri(string src)
        {
            // data:[<mime>][;base64],<payload>
            int comma = src.IndexOf(',');
            if (comma < 5) return null;

            try
            {
                string meta = src.Substring(5, comma - 5);
                string payload = src.Substring(comma + 1);
                bool isBase64 = meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
                string declaredMime = (isBase64 ? meta[..^7] : meta).Split(';')[0].Trim().ToLowerInvariant();

                byte[] bytes = isBase64
                    ? DecodeBase64Tolerant(payload)
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
                if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return null;

                string mime = DetectMimeType(bytes, "", declaredMime);
                if (mime == "image/svg+xml")
                    return new RawImage(Encoding.UTF8.GetBytes(NormalizeSvg(Encoding.UTF8.GetString(bytes))), mime, true);
                return new RawImage(bytes, mime, false);
            }
            catch { return null; }
        }

        /// <summary>
        /// Decodes base64 that real-world data: URIs may not keep clean: wrapped over
        /// several lines (whitespace), URL-safe alphabet (-_ instead of +/), or missing
        /// padding. Standard Convert.FromBase64String throws on any of these.
        /// </summary>
        private static byte[] DecodeBase64Tolerant(string payload)
        {
            var sb = new StringBuilder(payload.Length);
            foreach (char c in payload)
            {
                if (char.IsWhiteSpace(c)) continue;
                sb.Append(c switch { '-' => '+', '_' => '/', _ => c });
            }
            switch (sb.Length % 4)
            {
                case 2: sb.Append("=="); break;
                case 3: sb.Append('='); break;
                case 1: return Array.Empty<byte>();   // not a valid base64 length
            }
            return Convert.FromBase64String(sb.ToString());
        }

        /// <summary>
        /// Makes an SVG serialized out of an HTML document renderable as a standalone
        /// image: inside a data: URI it is parsed as XML, where the SVG namespace (and
        /// the xlink namespace used by &lt;use&gt;/&lt;image&gt;) must be declared explicitly.
        /// </summary>
        private static string NormalizeSvg(string svg)
        {
            svg = svg.TrimStart(ByteOrderMark, ' ', '\r', '\n', '\t');
            if (svg.Contains("&nbsp;", StringComparison.OrdinalIgnoreCase))
                svg = Regex.Replace(svg, "&nbsp;", "&#160;", RegexOptions.IgnoreCase);

            var root = Regex.Match(svg, @"<svg\b[^>]*>", RegexOptions.IgnoreCase);
            if (!root.Success) return svg;

            string tag = root.Value;
            var extra = new StringBuilder();
            if (!Regex.IsMatch(tag, @"\sxmlns\s*=", RegexOptions.IgnoreCase))
                extra.Append(" xmlns=\"http://www.w3.org/2000/svg\"");
            if (svg.Contains("xlink:", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(tag, @"\sxmlns:xlink\s*=", RegexOptions.IgnoreCase))
                extra.Append(" xmlns:xlink=\"http://www.w3.org/1999/xlink\"");
            if (extra.Length == 0) return svg;

            return svg.Substring(0, root.Index)
                   + "<svg" + extra + tag.Substring(4)
                   + svg.Substring(root.Index + tag.Length);
        }


        private static string ResolveUrl(string src, string? sourceUrl)
        {
            Uri? resolved = null;
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                Uri.TryCreate(src, UriKind.Absolute, out resolved);
            else if (src.StartsWith("//"))
                Uri.TryCreate("https:" + src, UriKind.Absolute, out resolved);
            else if (!string.IsNullOrEmpty(sourceUrl)
                     && Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? baseUri))
                Uri.TryCreate(baseUri, src, out resolved);

            if (resolved == null) return "";
            if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) return "";
            return resolved.AbsoluteUri;
        }


        private static async Task<RawImage?> DownloadImageAsync(string url, string? referer, SemaphoreSlim gate)
        {
            await gate.WaitAsync();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Hotlink protection expects the page the image was displayed on
                if (!string.IsNullOrEmpty(referer)
                    && Uri.TryCreate(referer, UriKind.Absolute, out Uri? refererUri)
                    && (refererUri.Scheme == Uri.UriSchemeHttp || refererUri.Scheme == Uri.UriSchemeHttps))
                    request.Headers.Referrer = refererUri;

                using var response = await _httpClient.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode) return null;
                if (response.Content.Headers.ContentLength > MaxImageBytes) return null;

                byte[]? bytes = await ReadCappedAsync(response.Content, MaxImageBytes, timeout.Token);
                if (bytes == null || bytes.Length == 0) return null;

                string mime = DetectMimeType(bytes, url, response.Content.Headers.ContentType?.MediaType);
                if (mime == "image/svg+xml")
                    return new RawImage(Encoding.UTF8.GetBytes(NormalizeSvg(Encoding.UTF8.GetString(bytes))), mime, true);
                return new RawImage(bytes, mime, false);
            }
            catch { return null; }
            finally { gate.Release(); }
        }

        /// <summary>Reads the body, aborting once it exceeds <paramref name="maxBytes"/> (Content-Length may be absent).</summary>
        private static async Task<byte[]?> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken token)
        {
            using var stream = await content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, token)) > 0)
            {
                if (buffer.Length + read > maxBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }


        private static (int w, int h) ReadImageDimensions(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                var dec = BitmapDecoder.Create(ms,
                    BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                return (dec.Frames[0].PixelWidth, dec.Frames[0].PixelHeight);
            }
            catch { return (300, 200); }
        }

        /// <summary>
        /// Intrinsic size of an SVG in CSS pixels, read from the ROOT element only
        /// (child elements have their own width/height). Falls back to the viewBox.
        /// </summary>
        private static (int w, int h) ParseSvgDimensions(string svg)
        {
            var root = Regex.Match(svg, @"<svg\b[^>]*>", RegexOptions.IgnoreCase);
            string tag = root.Success ? root.Value : svg;

            double? w = ParseSvgLength(AttributeValue(tag, "width"));
            double? h = ParseSvgLength(AttributeValue(tag, "height"));
            if (w > 0 && h > 0)
                return ((int)Math.Ceiling(w.Value), (int)Math.Ceiling(h.Value));

            var vb = Regex.Match(tag,
                @"viewBox\s*=\s*[""']\s*[-\d.]+[\s,]+[-\d.]+[\s,]+([\d.]+)[\s,]+([\d.]+)",
                RegexOptions.IgnoreCase);
            if (vb.Success
                && double.TryParse(vb.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double vw)
                && double.TryParse(vb.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double vh)
                && vw > 0 && vh > 0)
            {
                // A single explicit dimension keeps the viewBox aspect ratio
                if (w > 0) return ((int)Math.Ceiling(w.Value), (int)Math.Ceiling(w.Value * vh / vw));
                if (h > 0) return ((int)Math.Ceiling(h.Value * vw / vh), (int)Math.Ceiling(h.Value));
                return ((int)Math.Ceiling(vw), (int)Math.Ceiling(vh));
            }
            return (300, 200);
        }

        private static string? AttributeValue(string tag, string name)
        {
            var m = Regex.Match(tag,
                @"(?<![\w:-])" + name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
                RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return m.Groups[1].Success ? m.Groups[1].Value
                 : m.Groups[2].Success ? m.Groups[2].Value
                 : m.Groups[3].Value;
        }

        /// <summary>CSS length → CSS pixels. ex/em use the 16px default font (MathJax sizes in ex); percentages are unusable.</summary>
        private static double? ParseSvgLength(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var m = Regex.Match(value.Trim(), @"^([\d.]+)\s*([a-z%]*)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return null;

            double factor = m.Groups[2].Value.ToLowerInvariant() switch
            {
                "" or "px" => 1,
                "pt" => 96.0 / 72.0,
                "pc" => 16,
                "in" => 96,
                "cm" => 96.0 / 2.54,
                "mm" => 96.0 / 25.4,
                "em" => 16,
                "ex" => 8,
                _ => 0
            };
            return factor > 0 ? number * factor : null;
        }

        private static string DetectMimeType(byte[] data, string url, string? contentType)
        {
            if (data.Length >= 12)
            {
                if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                    return "image/png";
                if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                    return "image/jpeg";
                if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
                    return "image/gif";
                if (data[0] == 0x42 && data[1] == 0x4D)
                    return "image/bmp";
                if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
                    && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
                    return "image/webp";
            }
            if (LooksLikeSvg(data)) return "image/svg+xml";

            if (!string.IsNullOrEmpty(contentType))
            {
                string declared = contentType.Trim().ToLowerInvariant();
                if (declared == "image/jpg") return "image/jpeg";
                if (declared.StartsWith("image/") && declared != "image/*") return declared;
            }

            string lower = url.Split('?')[0].ToLowerInvariant();
            if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
            if (lower.EndsWith(".gif")) return "image/gif";
            if (lower.EndsWith(".webp")) return "image/webp";
            if (lower.EndsWith(".bmp")) return "image/bmp";
            if (lower.EndsWith(".svg")) return "image/svg+xml";
            return "image/png";
        }

        /// <summary>Content sniffing for SVG: math renderers (Wikipedia, MathJax) serve SVG from extension-less URLs.</summary>
        private static bool LooksLikeSvg(byte[] data)
        {
            string head = Encoding.UTF8.GetString(data, 0, Math.Min(data.Length, 2048))
                .TrimStart(ByteOrderMark, ' ', '\r', '\n', '\t');
            if (head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)) return true;
            return (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                    || head.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                    || head.StartsWith("<!--", StringComparison.Ordinal))
                   && head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
        }

        private static string MimeToExtension(string mime) => mime switch
        {
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/svg+xml" => ".svg",
            _ => ".png"
        };


        /// <summary>
        /// Builds RTF from the placeholder fragment, preserving inline character formatting
        /// (bold/italic/underline/strike/colour), headings and paragraph structure. Raster
        /// images embed as \pict; SVG (equations/icons) is rasterized to PNG via
        /// <see cref="SvgRasterizer"/> so it survives into RTF-only consumers (WordPad).
        /// The original vector and full styling always remain in the self-contained CF_HTML.
        /// Falls back to a plain-text RTF if styled generation throws.
        /// </summary>
        private static string BuildRtf(string htmlFragment, List<CachedImage> images)
        {
            var map = images.ToDictionary(i => i.Placeholder, StringComparer.Ordinal);
            try { return BuildStyledRtf(htmlFragment, map); }
            catch { return BuildSimpleRtf(htmlFragment, map); }
        }

        private static readonly Regex RtfTagRegex = new(
            @"<(?<close>/?)(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*)>",
            RegexOptions.Compiled);

        /// <summary>HTML fragment → RTF with inline styling, a \colortbl, headings and \pict images.</summary>
        private static string BuildStyledRtf(string fragment, Dictionary<string, CachedImage> map)
        {
            var colors = new List<string>();                 // "RRGGBB", 1-based in \colortbl
            int ColorIndex(string hex)
            {
                int i = colors.IndexOf(hex);
                if (i >= 0) return i + 1;
                colors.Add(hex);
                return colors.Count;
            }

            var body = new StringBuilder(8192);
            int bold = 0, italic = 0, underline = 0, strike = 0;
            var colorStack = new Stack<int>();
            bool atLineStart = true, lastWasPar = true;

            void AppendText(string htmlText)
            {
                string text = WebUtility.HtmlDecode(Regex.Replace(htmlText, @"\s+", " "));
                if (atLineStart) text = text.TrimStart();
                if (text.Length == 0) return;
                body.Append(EscapeRtf(text));
                atLineStart = false; lastWasPar = false;
            }
            void Par() { if (lastWasPar) return; body.Append(@"\par "); atLineStart = true; lastWasPar = true; }
            void Toggle(string on, ref int counter, bool opening, string off)
            {
                if (opening) { counter++; if (counter == 1) { body.Append(on); lastWasPar = false; } }
                else if (counter > 0) { counter--; if (counter == 0) body.Append(off); }
            }

            int last = 0;
            for (var m = RtfTagRegex.Match(fragment); m.Success; m = m.NextMatch())
            {
                if (m.Index > last) AppendText(fragment.Substring(last, m.Index - last));
                last = m.Index + m.Length;

                bool closing = m.Groups["close"].Value == "/";
                string name = m.Groups["name"].Value.ToLowerInvariant();
                string attrs = m.Groups["attrs"].Value;

                switch (name)
                {
                    case "b": case "strong": Toggle(@"\b ", ref bold, !closing, @"\b0 "); break;
                    case "i": case "em": Toggle(@"\i ", ref italic, !closing, @"\i0 "); break;
                    case "u": case "ins": Toggle(@"\ul ", ref underline, !closing, @"\ulnone "); break;
                    case "s": case "strike": case "del": Toggle(@"\strike ", ref strike, !closing, @"\strike0 "); break;
                    case "a": Toggle(@"\ul ", ref underline, !closing, @"\ulnone "); break;
                    case "br": body.Append(@"\line "); atLineStart = true; break;

                    case "img":
                        var srcM = SrcAttrRegex.Match(m.Value);
                        if (srcM.Success && map.TryGetValue(srcM.Groups["v"].Value, out var img))
                        { AppendRtfImage(body, img); lastWasPar = false; }
                        break;

                    case "p": case "div": case "li": case "tr":
                    case "blockquote": case "ul": case "ol": case "table":
                        Par();
                        break;

                    case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                        if (!closing) { Par(); body.Append(@"\b ").Append(HeadingSize(name)).Append(' '); lastWasPar = false; }
                        else { body.Append(@"\b0\fs22 "); Par(); }
                        break;

                    case "span": case "font":
                        if (!closing)
                        {
                            string? hex = ExtractColor(attrs);
                            if (hex != null) { int ci = ColorIndex(hex); colorStack.Push(ci); body.Append(@"\cf").Append(ci).Append(' '); }
                            else colorStack.Push(0);
                        }
                        else if (colorStack.Count > 0)
                        {
                            colorStack.Pop();
                            body.Append(@"\cf").Append(colorStack.Count > 0 ? colorStack.Peek() : 0).Append(' ');
                        }
                        break;
                }
            }
            if (last < fragment.Length) AppendText(fragment.Substring(last));

            var sb = new StringBuilder(body.Length + 256);
            sb.AppendLine(@"{\rtf1\ansi\ansicpg1252\deff0");
            sb.AppendLine(@"{\fonttbl{\f0\fswiss\fcharset0 Calibri;}}");
            sb.Append(@"{\colortbl ;");
            foreach (string hex in colors)
                sb.Append($@"\red{Convert.ToInt32(hex.Substring(0, 2), 16)}\green{Convert.ToInt32(hex.Substring(2, 2), 16)}\blue{Convert.ToInt32(hex.Substring(4, 2), 16)};");
            sb.AppendLine("}");
            sb.Append(@"\viewkind4\uc1\f0\fs22 ");
            sb.Append(body);
            sb.Append('}');
            return sb.ToString();
        }

        private static string HeadingSize(string h) => h switch
        {
            "h1" => @"\fs36", "h2" => @"\fs32", "h3" => @"\fs28", _ => @"\fs24"
        };

        /// <summary>Reads a colour from a style="color:…" or color="…" attribute → "RRGGBB", or null.</summary>
        private static string? ExtractColor(string attrs)
        {
            // (?<![\w-]) so "background-color" does not match as "color"
            var m = Regex.Match(attrs, @"(?<![\w-])color\s*[:=]\s*[""']?\s*(#[0-9a-fA-F]{3,6}|rgb\([^)]*\)|[a-zA-Z]+)", RegexOptions.IgnoreCase);
            return m.Success ? NormalizeColor(m.Groups[1].Value.Trim()) : null;
        }

        private static string? NormalizeColor(string c)
        {
            c = c.Trim();
            if (c.StartsWith("#"))
            {
                c = c.Substring(1);
                if (c.Length == 3) c = string.Concat(c[0], c[0], c[1], c[1], c[2], c[2]);
                return c.Length == 6 && Regex.IsMatch(c, "^[0-9a-fA-F]{6}$") ? c.ToUpperInvariant() : null;
            }
            var rgb = Regex.Match(c, @"rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)", RegexOptions.IgnoreCase);
            if (rgb.Success)
                return $"{Clamp(rgb.Groups[1].Value):X2}{Clamp(rgb.Groups[2].Value):X2}{Clamp(rgb.Groups[3].Value):X2}";
            return c.ToLowerInvariant() switch
            {
                "black" => "000000", "white" => "FFFFFF", "red" => "FF0000", "green" => "008000",
                "blue" => "0000FF", "yellow" => "FFFF00", "gray" or "grey" => "808080",
                "orange" => "FFA500", "purple" => "800080", "navy" => "000080", "teal" => "008080",
                _ => null
            };
        }

        private static int Clamp(string n) => Math.Clamp(int.TryParse(n, out int v) ? v : 0, 0, 255);

        /// <summary>Fallback: plain-text RTF (paragraph breaks + \pict images), no character styling.</summary>
        private static string BuildSimpleRtf(string fragment, Dictionary<string, CachedImage> map)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine(@"{\rtf1\ansi\ansicpg1252\deff0");
            sb.AppendLine(@"{\fonttbl{\f0\fswiss\fcharset0 Calibri;}}");
            sb.Append(@"\viewkind4\uc1\f0\fs22 ");

            var parts = Regex.Split(fragment, @"(<img[^>]+>)", RegexOptions.IgnoreCase);
            foreach (string part in parts)
            {
                if (part.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
                {
                    var srcM = SrcAttrRegex.Match(part);
                    if (srcM.Success && map.TryGetValue(srcM.Groups["v"].Value, out var img))
                        AppendRtfImage(sb, img);
                }
                else
                {
                    string text = StripHtmlToText(part);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    foreach (string line in text.Split('\n'))
                    {
                        string t = line.Trim();
                        if (t.Length > 0) sb.Append(EscapeRtf(t));
                        sb.Append(@"\par ");
                    }
                }
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendRtfImage(StringBuilder sb, CachedImage img)
        {
            byte[] data;
            string blip;

            if (img.IsSvg)
            {
                // RTF has no vector support → rasterize the SVG (equation/icon) to PNG
                byte[]? png = SvgRasterizer.ToPng(img.Data, img.Width, img.Height);
                if (png == null) return;
                data = png;
                blip = @"\pngblip";
            }
            else
            {
                data = img.Data;
                blip = img.MimeType == "image/jpeg" ? @"\jpegblip" : @"\pngblip";
                if (img.MimeType != "image/png" && img.MimeType != "image/jpeg")
                {
                    byte[]? converted = ConvertToPng(data);
                    if (converted == null) return;
                    data = converted;
                    blip = @"\pngblip";
                }
            }

            if (data.Length > MaxRtfImageBytes) return;

            int twW = img.Width * 15;
            int twH = img.Height * 15;
            int maxGoal = 6 * 1440;
            int goalW = Math.Min(twW, maxGoal);
            int goalH = twW > 0 ? (int)((long)twH * goalW / twW) : twH;

            string hex = Convert.ToHexString(data);

            sb.Append(@"{\pict");
            sb.Append(blip);
            sb.Append($@"\picw{twW}\pich{twH}\picwgoal{goalW}\pichgoal{goalH} ");
            for (int i = 0; i < hex.Length; i += 128)
                sb.AppendLine(hex.Substring(i, Math.Min(128, hex.Length - i)));
            sb.Append("} ");
        }

        private static byte[]? ConvertToPng(byte[] source)
        {
            try
            {
                using var src = new MemoryStream(source);
                var dec = BitmapDecoder.Create(src,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(dec.Frames[0]);
                using var dst = new MemoryStream();
                enc.Save(dst);
                return dst.ToArray();
            }
            catch { return null; }
        }


        private static string StripHtmlToText(string html)
        {
            string s = html;
            s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</(?:p|div|tr|li|h[1-6])>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", "");
            return WebUtility.HtmlDecode(s);
        }

        private static string EscapeRtf(string text)
        {
            var sb = new StringBuilder(text.Length + 16);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '{':  sb.Append(@"\{"); break;
                    case '}':  sb.Append(@"\}"); break;
                    default:
                        sb.Append(c > 127 ? $@"\u{(int)c}?" : c.ToString());
                        break;
                }
            }
            return sb.ToString();
        }


        public void Dispose()
        {
            UnregisterHotkey();
        }
    }
}
