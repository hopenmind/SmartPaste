using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindowsInput;
using WindowsInput.Native;

namespace SmartPaste
{
    /// <summary>
    /// Smart Copy engine — captures web content, builds a ContentPackage,
    /// saves it to FormatCache, and sets a multi-format clipboard.
    ///
    /// When SmartPaste detects a tagged clipboard (CopyId), it reads the
    /// ContentPackage and injects the optimal format for the target app.
    /// </summary>
    public class SmartCopyManager : IDisposable
    {
        private GlobalHotkey? _hotkey;
        private InputSimulator _simulator = new InputSimulator();

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 10 * 1024 * 1024
        };

        private const int MaxImageBytes = 10 * 1024 * 1024;
        private const int MaxImages = 50;
        private const int MaxRtfImageBytes = 5 * 1024 * 1024;

        // ── Internal image descriptor (not serialized) ───────────────

        private sealed class CachedImage
        {
            public string OriginalSrc { get; init; } = "";
            public string FileName { get; init; } = "";
            public string LocalPath { get; init; } = "";
            public string MimeType { get; init; } = "image/png";
            public byte[] Data { get; init; } = Array.Empty<byte>();
            public int Width { get; init; }
            public int Height { get; init; }
            public bool IsSvg { get; init; }
        }

        // ── Hotkey ───────────────────────────────────────────────────

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

        // ── Main flow ────────────────────────────────────────────────

        /// <summary>
        /// Called by PasteInterceptor when Ctrl+C is intercepted.
        /// Processes the existing clipboard content (Ctrl+C already happened).
        /// </summary>
        public async void EnhanceClipboard()
        {
            await Task.Delay(400); // Let the normal Ctrl+C finish
            await ProcessClipboardContent();
        }

        private async void PerformSmartCopy()
        {
            _simulator ??= new InputSimulator();
            _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
            await Task.Delay(350);
            await ProcessClipboardContent();
        }

        private async Task ProcessClipboardContent()
        {
            try
            {
                IDataObject? clip = Clipboard.GetDataObject();
                if (clip == null) return;

                // Snapshot originals
                var originalBitmap = clip.GetDataPresent(DataFormats.Bitmap)
                    ? Clipboard.GetImage() : null;
                string? originalText = clip.GetDataPresent(DataFormats.UnicodeText)
                    ? clip.GetData(DataFormats.UnicodeText) as string
                    : clip.GetDataPresent(DataFormats.Text)
                        ? clip.GetData(DataFormats.Text) as string : null;
                string? originalRtf = clip.GetDataPresent(DataFormats.Rtf)
                    ? clip.GetData(DataFormats.Rtf) as string : null;

                // No HTML → nothing to enhance
                if (!clip.GetDataPresent(DataFormats.Html)) return;

                string? rawHtml = clip.GetData(DataFormats.Html) as string;
                if (string.IsNullOrEmpty(rawHtml)) return;

                // ── Process HTML → ContentPackage ──
                var result = await ProcessHtmlAsync(rawHtml);

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
                        IsSvg = i.IsSvg
                    }).ToList()
                };

                // Save to FormatCache (the structured disk cache)
                FormatCache.Save(package);

                // Save selection bitmap
                if (originalBitmap != null)
                    SaveBitmap(originalBitmap);

                // ── Build multi-format clipboard (passive Ctrl+V) ──
                var data = new DataObject();

                // 1. Plain text first (universally supported)
                if (!string.IsNullOrEmpty(originalText))
                    data.SetText(originalText, TextDataFormat.UnicodeText);

                // 2. RTF (Office/LibreOffice preferred format)
                string rtfToSet = !string.IsNullOrEmpty(result.Rtf) ? result.Rtf : originalRtf ?? "";
                if (!string.IsNullOrEmpty(rtfToSet))
                    data.SetText(rtfToSet, TextDataFormat.Rtf);

                // 3. CF_HTML (browsers/Electron)
                string cfHtml = FormatCache.BuildCFHtml(result.Fragment, result.SourceUrl);
                data.SetText(cfHtml, TextDataFormat.Html);

                // 4. Bitmap (image editors, universal fallback)
                if (originalBitmap != null)
                    data.SetImage(originalBitmap);

                // 5. CopyId tag (must be last — custom format)
                data.SetData(FormatCache.CopyIdFormat, package.Id);

                SetClipboardSafe(data);
            }
            catch
            {
                // On failure the original clipboard is untouched
            }
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

        private static void SetClipboardSafe(DataObject data)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetDataObject(data, true); return; }
                catch (System.Runtime.InteropServices.COMException)
                { System.Threading.Thread.Sleep(100); }
            }
        }

        // ── HTML processing pipeline ─────────────────────────────────

        private sealed record ProcessResult(
            string Fragment,
            string? SourceUrl,
            string Rtf,
            List<CachedImage> Images);

        private async Task<ProcessResult> ProcessHtmlAsync(string rawHtml)
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

            var images = new List<CachedImage>();
            int idx = 0;

            // ── Inline SVGs → rasterize to PNG for universal compat ──
            fragment = Regex.Replace(fragment, @"<svg[^>]*>.*?</svg>", m =>
            {
                try
                {
                    var (w, h) = ParseSvgDimensions(m.Value);
                    // Ensure minimum size for readability
                    if (w < 20) w = 200;
                    if (h < 20) h = 60;

                    // Save SVG source
                    string svgName = $"svg_{idx}.svg";
                    string svgPath = FormatCache.GetImagePath(svgName);
                    File.WriteAllText(svgPath, m.Value, Encoding.UTF8);

                    // Rasterize SVG → PNG using WPF's XAML renderer
                    byte[]? pngData = RasterizeSvgToPng(m.Value, w * 2, h * 2);
                    if (pngData != null)
                    {
                        string pngName = $"svg_{idx}.png";
                        string pngPath = FormatCache.GetImagePath(pngName);
                        File.WriteAllBytes(pngPath, pngData);

                        images.Add(new CachedImage
                        {
                            OriginalSrc = $"__svg__{svgName}",
                            FileName = pngName,
                            LocalPath = pngPath,
                            MimeType = "image/png",
                            Data = pngData,
                            Width = w * 2, Height = h * 2,
                            IsSvg = false  // It's now a raster PNG
                        });

                        idx++;
                        string uri = new Uri(pngPath).AbsoluteUri;
                        return $"<img src=\"{uri}\" width=\"{w}\" height=\"{h}\" alt=\"equation\" />";
                    }

                    // Fallback: keep as SVG file reference
                    images.Add(new CachedImage
                    {
                        OriginalSrc = $"__svg__{svgName}",
                        FileName = svgName,
                        LocalPath = svgPath,
                        MimeType = "image/svg+xml",
                        Data = Encoding.UTF8.GetBytes(m.Value),
                        Width = w, Height = h,
                        IsSvg = true
                    });

                    idx++;
                    string svgUri = new Uri(svgPath).AbsoluteUri;
                    return $"<img src=\"{svgUri}\" width=\"{w}\" height=\"{h}\" alt=\"SVG\" />";
                }
                catch { return m.Value; }
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // ── <img> tags → download & cache ──
            var imgMatches = Regex.Matches(fragment,
                @"<img[^>]+src\s*=\s*[""']([^""']+)[""'][^>]*>",
                RegexOptions.IgnoreCase);

            foreach (Match match in imgMatches)
            {
                if (images.Count >= MaxImages) break;

                string src = match.Groups[1].Value;
                if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    string url = ResolveUrl(src, sourceUrl);
                    if (string.IsNullOrEmpty(url)) continue;

                    byte[]? bytes = await DownloadImageAsync(url);
                    if (bytes == null || bytes.Length == 0) continue;

                    string mime = DetectMimeType(bytes, url);
                    string ext = MimeToExtension(mime);
                    string name = $"img_{idx++}{ext}";
                    string path = FormatCache.GetImagePath(name);
                    File.WriteAllBytes(path, bytes);

                    var (w, h) = ReadImageDimensions(bytes);

                    images.Add(new CachedImage
                    {
                        OriginalSrc = src,
                        FileName = name,
                        LocalPath = path,
                        MimeType = mime,
                        Data = bytes,
                        Width = w, Height = h,
                        IsSvg = false
                    });

                    fragment = fragment.Replace(src, new Uri(path).AbsoluteUri);
                }
                catch { }
            }

            string rtf = BuildRtf(fragment, images);
            return new ProcessResult(fragment, sourceUrl, rtf, images);
        }

        // ── URL resolution ───────────────────────────────────────────

        private static string ResolveUrl(string src, string? sourceUrl)
        {
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return src;
            if (src.StartsWith("//"))
                return "https:" + src;
            if (!string.IsNullOrEmpty(sourceUrl) &&
                Uri.TryCreate(new Uri(sourceUrl!), src, out Uri? resolved))
                return resolved.ToString();
            return "";
        }

        // ── Image download ───────────────────────────────────────────

        private static async Task<byte[]?> DownloadImageAsync(string url)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url,
                    HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxImageBytes) return null;
                byte[] data = await response.Content.ReadAsByteArrayAsync();
                return data.Length <= MaxImageBytes ? data : null;
            }
            catch { return null; }
        }

        // ── Image utilities ──────────────────────────────────────────

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

        private static (int w, int h) ParseSvgDimensions(string svg)
        {
            var wm = Regex.Match(svg, @"\bwidth\s*=\s*[""']?([\d.]+)", RegexOptions.IgnoreCase);
            var hm = Regex.Match(svg, @"\bheight\s*=\s*[""']?([\d.]+)", RegexOptions.IgnoreCase);
            if (wm.Success && hm.Success)
            {
                int w = (int)Math.Ceiling(double.Parse(wm.Groups[1].Value, CultureInfo.InvariantCulture));
                int h = (int)Math.Ceiling(double.Parse(hm.Groups[1].Value, CultureInfo.InvariantCulture));
                if (w > 0 && h > 0) return (w, h);
            }
            var vb = Regex.Match(svg,
                @"viewBox\s*=\s*[""']\s*[\d.]+\s+[\d.]+\s+([\d.]+)\s+([\d.]+)",
                RegexOptions.IgnoreCase);
            if (vb.Success)
            {
                int w = (int)Math.Ceiling(double.Parse(vb.Groups[1].Value, CultureInfo.InvariantCulture));
                int h = (int)Math.Ceiling(double.Parse(vb.Groups[2].Value, CultureInfo.InvariantCulture));
                if (w > 0 && h > 0) return (w, h);
            }
            return (300, 200);
        }

        private static string DetectMimeType(byte[] data, string url)
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
            string lower = url.Split('?')[0].ToLowerInvariant();
            if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
            if (lower.EndsWith(".gif")) return "image/gif";
            if (lower.EndsWith(".webp")) return "image/webp";
            if (lower.EndsWith(".bmp")) return "image/bmp";
            if (lower.EndsWith(".svg")) return "image/svg+xml";
            return "image/png";
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

        // ── RTF generation ───────────────────────────────────────────

        private static string BuildRtf(string htmlFragment, List<CachedImage> images)
        {
            var raster = images.Where(i => !i.IsSvg).ToList();

            var map = new Dictionary<string, CachedImage>(StringComparer.OrdinalIgnoreCase);
            foreach (var img in raster)
                map[new Uri(img.LocalPath).AbsoluteUri] = img;

            var sb = new StringBuilder(4096);
            sb.AppendLine(@"{\rtf1\ansi\ansicpg1252\deff0");
            sb.AppendLine(@"{\fonttbl{\f0\fswiss\fcharset0 Calibri;}}");
            sb.Append(@"\viewkind4\uc1\f0\fs22 ");

            var parts = Regex.Split(htmlFragment, @"(<img[^>]+>)", RegexOptions.IgnoreCase);
            foreach (string part in parts)
            {
                if (part.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
                {
                    var srcM = Regex.Match(part,
                        @"src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    if (srcM.Success && map.TryGetValue(srcM.Groups[1].Value, out var img))
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
            byte[] data = img.Data;
            string blip = img.MimeType == "image/jpeg" ? @"\jpegblip" : @"\pngblip";

            if (img.MimeType != "image/png" && img.MimeType != "image/jpeg")
            {
                byte[]? converted = ConvertToPng(data);
                if (converted == null) return;
                data = converted;
                blip = @"\pngblip";
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

        // ── SVG rasterization ────────────────────────────────────────

        /// <summary>
        /// Rasterize an SVG string to PNG bytes using WPF's rendering engine.
        /// Wraps the SVG in a WebBrowser-like XAML DrawingVisual approach.
        /// Falls back to a simpler method if the SVG is too complex.
        /// </summary>
        private static byte[]? RasterizeSvgToPng(string svgXml, int width, int height)
        {
            try
            {
                // Use WPF's built-in XAML/Drawing pipeline to render
                // We create a DrawingVisual + FormattedText as a fallback
                // since WPF can't natively parse arbitrary SVGs.
                //
                // Strategy: save SVG to temp file, load as image via WPF's
                // SvgImage or use a data-uri approach with DrawingImage.

                // Try loading SVG as a standard image (works for simple SVGs)
                byte[] svgBytes = Encoding.UTF8.GetBytes(svgXml);
                using var svgStream = new MemoryStream(svgBytes);

                try
                {
                    // WPF can decode some SVG-like formats
                    var decoder = BitmapDecoder.Create(svgStream,
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 0)
                    {
                        var frame = decoder.Frames[0];
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(frame));
                        using var pngStream = new MemoryStream();
                        encoder.Save(pngStream);
                        return pngStream.ToArray();
                    }
                }
                catch { /* WPF can't decode this SVG, use DrawingVisual fallback */ }

                // Fallback: render SVG text content as formatted text image
                // This captures the mathematical notation as readable text
                string textContent = ExtractSvgTextContent(svgXml);
                if (string.IsNullOrWhiteSpace(textContent)) return null;

                return RenderTextToPng(textContent, width, height);
            }
            catch { return null; }
        }

        /// <summary>Extract visible text from SVG (MathJax equations etc.)</summary>
        private static string ExtractSvgTextContent(string svg)
        {
            // Extract text from <text> elements and data-c attributes (MathJax)
            var sb = new StringBuilder();

            // MathJax uses data-c="XX" with hex Unicode codepoints
            var dataC = Regex.Matches(svg, @"data-c=""([0-9A-Fa-f]+)""", RegexOptions.IgnoreCase);
            if (dataC.Count > 0)
            {
                foreach (Match m in dataC)
                {
                    if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out int cp))
                        sb.Append(char.ConvertFromUtf32(cp));
                }
                return sb.ToString();
            }

            // Standard SVG <text> elements
            var textMatches = Regex.Matches(svg, @"<text[^>]*>(.*?)</text>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in textMatches)
            {
                string inner = Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "");
                sb.Append(System.Net.WebUtility.HtmlDecode(inner));
            }

            return sb.ToString();
        }

        /// <summary>Render text to PNG using WPF DrawingVisual.</summary>
        private static byte[]? RenderTextToPng(string text, int width, int height)
        {
            try
            {
                width = Math.Max(width, 100);
                height = Math.Max(height, 40);

                var visual = new System.Windows.Media.DrawingVisual();
                using (var ctx = visual.RenderOpen())
                {
                    // White background
                    ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

                    // Render the equation text
                    var typeface = new Typeface(new FontFamily("Cambria Math, Cambria, Times New Roman"),
                        FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);

                    double fontSize = Math.Min(height * 0.6, 36);
                    var formattedText = new FormattedText(text,
                        CultureInfo.InvariantCulture,
                        System.Windows.FlowDirection.LeftToRight,
                        typeface, fontSize,
                        Brushes.Black,
                        96.0); // Standard DPI — DrawingVisual has no PresentationSource

                    // Center the text
                    double x = (width - formattedText.Width) / 2;
                    double y = (height - formattedText.Height) / 2;
                    ctx.DrawText(formattedText, new Point(Math.Max(4, x), Math.Max(4, y)));
                }

                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        // ── HTML text helpers ────────────────────────────────────────

        private static string StripHtmlToText(string html)
        {
            string s = html;
            s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</(?:p|div|tr|li|h[1-6])>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", "");
            return System.Net.WebUtility.HtmlDecode(s);
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

        // ── Disposal ─────────────────────────────────────────────────

        public void Dispose()
        {
            UnregisterHotkey();
        }
    }
}
