using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartPaste
{
    // ── Content classification ───────────────────────────────────────

    public enum ContentType
    {
        PlainText,
        RichHtml,
        SingleImage,
        Mixed
    }

    // ── Image descriptor (serialized in manifest.json) ───────────────

    public class PackagedImage
    {
        public string FileName { get; set; } = "";
        public string MimeType { get; set; } = "image/png";
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsSvg { get; set; }

        /// <summary>Loaded on demand by FormatCache.Load() - not serialized.</summary>
        [JsonIgnore]
        public byte[]? Data { get; set; }
    }

    // ── Content package (the "format cache" the user envisioned) ─────

    public class ContentPackage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime CapturedAt { get; set; } = DateTime.Now;
        public ContentType Type { get; set; } = ContentType.PlainText;
        public string? SourceUrl { get; set; }

        /// <summary>
        /// Self-contained HTML fragment: every captured image is embedded as a
        /// data: URI, so no file:// or remote reference is needed by the consumer.
        /// </summary>
        [JsonIgnore] public string? HtmlFragment { get; set; }

        /// <summary>
        /// Pre-built RTF with \pict embedded raster images. RTF cannot carry SVG,
        /// so it is only a fallback for consumers that ignore CF_HTML.
        /// </summary>
        [JsonIgnore] public string? RtfContent { get; set; }

        /// <summary>Plain text representation.</summary>
        public string? PlainText { get; set; }

        /// <summary>Image descriptors - binary data loaded separately.</summary>
        public List<PackagedImage> Images { get; set; } = new();

        [JsonIgnore] public bool HasImages => Images.Count > 0;
        [JsonIgnore] public bool HasRichContent => !string.IsNullOrEmpty(HtmlFragment) || HasImages;
    }

    // ── FormatCache - structured disk storage ────────────────────────
    //
    //   %TEMP%\SmartPaste\current\
    //   ├── manifest.json          metadata + image descriptors
    //   ├── fragment.html          self-contained HTML (data: URI images)
    //   ├── content.rtf            RTF with \pict images
    //   ├── text.txt               plain text
    //   ├── selection.png          browser bitmap snapshot
    //   └── images/
    //       ├── img_000.png
    //       ├── img_001.jpg
    //       └── svg_000.svg

    public static class FormatCache
    {
        public static readonly string CacheRoot = Path.Combine(Path.GetTempPath(), "SmartPaste");
        public static readonly string CurrentDir = Path.Combine(CacheRoot, "current");
        public static readonly string ImagesDir = Path.Combine(CurrentDir, "images");
        public static readonly string SelectionBitmapPath = Path.Combine(CurrentDir, "selection.png");

        /// <summary>Custom clipboard format used to tag SmartCopy content.</summary>
        public const string CopyIdFormat = "SmartPaste_CopyId";

        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        // ── Save ─────────────────────────────────────────────────────

        /// <summary>
        /// Persists the package - metadata, text formats and image bytes - replacing
        /// the previous capture. Images are written here (from PackagedImage.Data)
        /// because the directory is recreated on every save.
        /// </summary>
        public static void Save(ContentPackage package)
        {
            try
            {
                if (Directory.Exists(CurrentDir))
                    Directory.Delete(CurrentDir, true);
                Directory.CreateDirectory(ImagesDir);

                foreach (var img in package.Images)
                {
                    if (img.Data == null || img.Data.Length == 0 || string.IsNullOrEmpty(img.FileName))
                        continue;
                    File.WriteAllBytes(GetImagePath(img.FileName), img.Data);
                }

                if (!string.IsNullOrEmpty(package.HtmlFragment))
                    File.WriteAllText(Path.Combine(CurrentDir, "fragment.html"),
                        package.HtmlFragment, Encoding.UTF8);

                if (!string.IsNullOrEmpty(package.RtfContent))
                    File.WriteAllText(Path.Combine(CurrentDir, "content.rtf"),
                        package.RtfContent, Encoding.UTF8);

                if (!string.IsNullOrEmpty(package.PlainText))
                    File.WriteAllText(Path.Combine(CurrentDir, "text.txt"),
                        package.PlainText, Encoding.UTF8);

                File.WriteAllText(Path.Combine(CurrentDir, "manifest.json"),
                    JsonSerializer.Serialize(package, _json), Encoding.UTF8);
            }
            catch { }
        }

        // ── Load ─────────────────────────────────────────────────────

        public static ContentPackage? Load()
        {
            string path = Path.Combine(CurrentDir, "manifest.json");
            if (!File.Exists(path)) return null;

            try
            {
                var pkg = JsonSerializer.Deserialize<ContentPackage>(File.ReadAllText(path));
                if (pkg == null) return null;

                string f;
                f = Path.Combine(CurrentDir, "fragment.html");
                if (File.Exists(f)) pkg.HtmlFragment = File.ReadAllText(f);

                f = Path.Combine(CurrentDir, "content.rtf");
                if (File.Exists(f)) pkg.RtfContent = File.ReadAllText(f);

                f = Path.Combine(CurrentDir, "text.txt");
                if (File.Exists(f)) pkg.PlainText = File.ReadAllText(f);

                foreach (var img in pkg.Images)
                {
                    f = GetImagePath(img.FileName);
                    if (File.Exists(f)) img.Data = File.ReadAllBytes(f);
                }

                return pkg;
            }
            catch { return null; }
        }

        // ── Queries ──────────────────────────────────────────────────

        public static bool HasRecentContent(int maxAgeSeconds = 300)
        {
            string path = Path.Combine(CurrentDir, "manifest.json");
            if (!File.Exists(path)) return false;
            return (DateTime.Now - File.GetLastWriteTime(path)).TotalSeconds < maxAgeSeconds;
        }

        public static string GetImagePath(string fileName) => Path.Combine(ImagesDir, fileName);

        public static bool HasSelectionBitmap => File.Exists(SelectionBitmapPath);

        // ── CF_HTML envelope builder (shared by Copy & Paste) ────────

        /// <summary>
        /// Wraps an HTML fragment in a CF_HTML ("HTML Format") envelope.
        /// Offsets are byte offsets into the UTF-8 encoding of the returned string,
        /// which is how WPF writes DataFormats.Html to the clipboard. Header numbers
        /// are fixed-width so the header length does not depend on their values.
        /// </summary>
        public static string BuildCFHtml(string htmlFragment, string? sourceUrl)
        {
            const string prefix =
                "<html>\r\n<head><meta charset=\"utf-8\"></head>\r\n<body>\r\n<!--StartFragment-->";
            const string suffix = "<!--EndFragment-->\r\n</body>\r\n</html>";

            // The URL must never break the header: keep it on one line, no control chars.
            string sourceLine = "";
            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                var cleanUrl = new StringBuilder(sourceUrl.Length);
                foreach (char c in sourceUrl)
                    if (!char.IsControl(c)) cleanUrl.Append(c);
                if (cleanUrl.Length > 0)
                    sourceLine = $"SourceURL:{cleanUrl.ToString().Trim()}\r\n";
            }

            static string Header(int startHtml, int endHtml, int startFragment, int endFragment, string urlLine) =>
                "Version:0.9\r\n" +
                $"StartHTML:{startHtml:D10}\r\n" +
                $"EndHTML:{endHtml:D10}\r\n" +
                $"StartFragment:{startFragment:D10}\r\n" +
                $"EndFragment:{endFragment:D10}\r\n" +
                urlLine;

            int headerLength = Encoding.UTF8.GetByteCount(Header(0, 0, 0, 0, sourceLine));
            int startFragment = headerLength + Encoding.UTF8.GetByteCount(prefix);
            int endFragment = startFragment + Encoding.UTF8.GetByteCount(htmlFragment);
            int endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);

            return Header(headerLength, endHtml, startFragment, endFragment, sourceLine)
                   + prefix + htmlFragment + suffix;
        }

        // ── Cleanup ──────────────────────────────────────────────────

        public static void Clear()
        {
            try { if (Directory.Exists(CurrentDir)) Directory.Delete(CurrentDir, true); }
            catch { }
        }
    }
}
