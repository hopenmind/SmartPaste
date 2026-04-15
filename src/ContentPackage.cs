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

        /// <summary>Loaded on demand by FormatCache.Load() — not serialized.</summary>
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

        /// <summary>HTML fragment with file:// image references.</summary>
        [JsonIgnore] public string? HtmlFragment { get; set; }

        /// <summary>Pre-built RTF with \pict embedded images.</summary>
        [JsonIgnore] public string? RtfContent { get; set; }

        /// <summary>Plain text representation.</summary>
        public string? PlainText { get; set; }

        /// <summary>Image descriptors — binary data loaded separately.</summary>
        public List<PackagedImage> Images { get; set; } = new();

        [JsonIgnore] public bool HasImages => Images.Count > 0;
        [JsonIgnore] public bool HasRichContent => !string.IsNullOrEmpty(HtmlFragment) || HasImages;
    }

    // ── FormatCache — structured disk storage ────────────────────────
    //
    //   %TEMP%\SmartPaste\current\
    //   ├── manifest.json          metadata + image descriptors
    //   ├── fragment.html          HTML with file:// refs
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

        public static void Save(ContentPackage package)
        {
            try
            {
                if (Directory.Exists(CurrentDir))
                    Directory.Delete(CurrentDir, true);
                Directory.CreateDirectory(ImagesDir);

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

        public static string BuildCFHtml(string htmlFragment, string? sourceUrl)
        {
            string header =
                "Version:0.9\r\n" +
                "StartHTML:{0:0000000000}\r\n" +
                "EndHTML:{1:0000000000}\r\n" +
                "StartFragment:{2:0000000000}\r\n" +
                "EndFragment:{3:0000000000}\r\n";

            if (!string.IsNullOrEmpty(sourceUrl))
                header += $"SourceURL:{sourceUrl}\r\n";

            const string prefix = "<html>\r\n<body>\r\n<!--StartFragment-->";
            const string suffix = "<!--EndFragment-->\r\n</body>\r\n</html>";

            string dummy = string.Format(header, 0, 0, 0, 0);
            int hLen = Encoding.UTF8.GetByteCount(dummy);
            int sFrag = hLen + Encoding.UTF8.GetByteCount(prefix);
            int eFrag = sFrag + Encoding.UTF8.GetByteCount(htmlFragment);
            int eHtml = eFrag + Encoding.UTF8.GetByteCount(suffix);

            return string.Format(header, hLen, eHtml, sFrag, eFrag)
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
