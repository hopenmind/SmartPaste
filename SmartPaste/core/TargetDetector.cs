using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartPaste
{
    // ── Target application classification ────────────────────────────
    //
    // Each type maps to an optimal clipboard format strategy
    // (see SmartPasteManager.SmartInject):
    //   Office      → self-contained CF_HTML (data: URI images, SVG preserved);
    //                 CF_RTF only when no HTML fragment exists
    //   Browser     → self-contained CF_HTML
    //   Electron    → self-contained CF_HTML (Chromium-based rendering)
    //   RichText    → self-contained CF_HTML, except RTF-only editors (WordPad) → CF_RTF
    //   Markdown    → CF_UNICODETEXT as Markdown (HtmlToMarkdown)
    //   PlainText   → CF_UNICODETEXT (or simulated typing)
    //   ImageEditor → CF_BITMAP
    //   Unknown     → Multi-format (all formats, let app choose)

    public enum TargetType
    {
        Office,
        Browser,
        Electron,
        RichText,
        Markdown,
        PlainText,
        ImageEditor,
        Unknown
    }

    /// <summary>
    /// Detects the foreground application and classifies it for
    /// optimal clipboard format selection during SmartInject.
    /// </summary>
    public static class TargetDetector
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// Returns the target type and process name of the current foreground window.
        /// </summary>
        public static (TargetType type, string processName) Detect()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return (TargetType.Unknown, "unknown");

                GetWindowThreadProcessId(hwnd, out uint pid);
                string name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                return (Classify(name), name);
            }
            catch
            {
                return (TargetType.Unknown, "unknown");
            }
        }

        /// <summary>
        /// True for rich-text editors whose paste path cannot consume CF_HTML at all
        /// (RichEdit-based, e.g. WordPad). CF_RTF must stay the primary format for them.
        /// </summary>
        public static bool IsRtfOnlyEditor(string processName) =>
            processName == "wordpad";

        private static TargetType Classify(string name) => name switch
        {
            // ── Microsoft Office ──
            "winword" or "excel" or "powerpnt" or "outlook"
                or "onenote" or "mspub" or "msaccess"
                => TargetType.Office,

            // ── Mail composers (HTML compose → CF_HTML) ──
            "thunderbird" or "betterbird"
                => TargetType.Office,

            // ── Browsers (native Chromium / Gecko / WebKit) ──
            "chrome" or "firefox" or "msedge" or "opera"
                or "brave" or "vivaldi" or "arc" or "waterfox"
                or "iridium" or "thorium" or "floorp"
                => TargetType.Browser,

            // ── Electron / Chromium-embedded apps ──
            "code" or "slack" or "discord" or "teams"
                or "notion" or "figma"
                or "postman" or "spotify" or "signal"
                or "telegram" or "whatsapp" or "todoist"
                or "linear" or "clickup" or "asana"
                or "trello" or "miro" or "excalidraw"
                => TargetType.Electron,

            // ── Markdown-first editors (receive Markdown text) ──
            "obsidian" or "typora" or "marktext" or "zettlr"
                or "ghostwriter" or "joplin" or "logseq" or "notable"
                => TargetType.Markdown,

            // ── Rich text editors ──
            "wordpad" or "soffice" or "libreoffice"
                or "abiword" or "wps"
                => TargetType.RichText,

            // ── Plain text / terminals ──
            "notepad" or "notepad++" or "sublime_text"
                or "cmd" or "powershell" or "windowsterminal"
                or "conhost" or "mintty" or "alacritty"
                or "wezterm" or "wt" or "vim" or "nano"
                or "helix" or "micro"
                => TargetType.PlainText,

            // ── Image editors ──
            "mspaint" or "photoshop" or "gimp-2.10" or "gimp"
                or "paint" or "krita" or "inkscape"
                or "clip studio" or "aseprite"
                => TargetType.ImageEditor,

            _ => TargetType.Unknown
        };
    }
}
