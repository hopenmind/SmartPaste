using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using WindowsInput;
using WindowsInput.Native;

namespace SmartPaste
{
    public class SmartCopyManager : IDisposable
    {
        private GlobalHotkey? _hotkey;
        private InputSimulator _simulator = new InputSimulator();
        private static readonly HttpClient _httpClient = new HttpClient();

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
            _simulator ??= new InputSimulator();
            _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
            await Task.Delay(300);

            try
            {
                IDataObject? clipboardData = Clipboard.GetDataObject();
                if (clipboardData == null) return;

                if (clipboardData.GetDataPresent(DataFormats.Html))
                {
                    await ProcessHtmlClipboard(clipboardData);
                }
                else if (clipboardData.GetDataPresent(DataFormats.Rtf))
                {
                    var rtfData = clipboardData.GetData(DataFormats.Rtf) as string;
                    if (!string.IsNullOrEmpty(rtfData))
                    {
                        DataObject newDataObject = new DataObject();
                        newDataObject.SetData(DataFormats.Rtf, rtfData);
                        if (clipboardData.GetDataPresent(DataFormats.UnicodeText))
                            newDataObject.SetData(DataFormats.UnicodeText, clipboardData.GetData(DataFormats.UnicodeText));
                        else if (clipboardData.GetDataPresent(DataFormats.Text))
                            newDataObject.SetData(DataFormats.Text, clipboardData.GetData(DataFormats.Text));
                        Clipboard.SetDataObject(newDataObject, true);
                    }
                }
            }
            catch
            {
                // Suppress exception
            }
        }

        private async Task ProcessHtmlClipboard(IDataObject clipboardData)
        {
            string? rawHtml = clipboardData.GetData(DataFormats.Html) as string;
            if (string.IsNullOrEmpty(rawHtml)) return;

            string newHtml = await EmbedImagesInHtmlAsync(rawHtml);
            if (string.IsNullOrWhiteSpace(newHtml)) return;

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    DataObject newDataObject = new DataObject();
                    newDataObject.SetData(DataFormats.Html, newHtml);

                    if (clipboardData.GetDataPresent(DataFormats.UnicodeText))
                        newDataObject.SetData(DataFormats.UnicodeText, clipboardData.GetData(DataFormats.UnicodeText));
                    else if (clipboardData.GetDataPresent(DataFormats.Text))
                        newDataObject.SetData(DataFormats.Text, clipboardData.GetData(DataFormats.Text));

                    if (clipboardData.GetDataPresent(DataFormats.Rtf))
                        newDataObject.SetData(DataFormats.Rtf, clipboardData.GetData(DataFormats.Rtf));

                    Clipboard.SetDataObject(newDataObject, true);
                    break;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    await Task.Delay(100);
                }
            }
        }

        private async Task<string> EmbedImagesInHtmlAsync(string rawHtml)
        {
            string? sourceUrl = null;
            var matchUrl = Regex.Match(rawHtml, @"SourceURL:(.+?)\r?\n");
            if (matchUrl.Success) sourceUrl = matchUrl.Groups[1].Value.Trim();

            string fragment = rawHtml;
            var matchFrag = Regex.Match(rawHtml, @"<!--StartFragment-->(.*?)<!--EndFragment-->", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchFrag.Success) fragment = matchFrag.Groups[1].Value;

            // Inline SVGs to Base64
            fragment = Regex.Replace(fragment, @"<svg[^>]*>.*?</svg>", matchSvg =>
            {
                try
                {
                    string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(matchSvg.Value));
                    return $"<img src=\"data:image/svg+xml;base64,{base64}\" alt=\"SVG/Math\" />";
                }
                catch { return matchSvg.Value; }
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // External images
            string pattern = @"<img[^>]+src\s*=\s*[""']([^""']+)[""'][^>]*>";
            var matches = Regex.Matches(fragment, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                string originalSrc = match.Groups[1].Value;
                if (originalSrc.StartsWith("data:image", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    string absoluteUrl = originalSrc;
                    if (!originalSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(sourceUrl))
                    {
                        if (Uri.TryCreate(new Uri(sourceUrl!), originalSrc, out Uri? resultUri))
                            absoluteUrl = resultUri.ToString();
                    }

                    if (absoluteUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] imageBytes = await _httpClient.GetByteArrayAsync(absoluteUrl);
                        string base64 = Convert.ToBase64String(imageBytes);

                        string mimeType = GetMimeType(absoluteUrl);
                        string dataUri = $"data:{mimeType};base64,{base64}";
                        fragment = fragment.Replace(match.Value, match.Value.Replace(originalSrc, dataUri));
                    }
                }
                catch { }
            }

            return GenerateCFHtml(fragment, sourceUrl!);
        }

        private string GetMimeType(string url)
        {
            string lower = url.ToLowerInvariant();
            if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
            if (lower.EndsWith(".gif")) return "image/gif";
            if (lower.EndsWith(".svg")) return "image/svg+xml";
            if (lower.EndsWith(".ico")) return "image/x-icon";
            if (lower.EndsWith(".bmp")) return "image/bmp";
            if (lower.EndsWith(".webp")) return "image/webp";
            return "image/png";
        }

        private string GenerateCFHtml(string htmlFragment, string sourceUrl)
        {
            string header = "Version:0.9\r\n" +
                "StartHTML:{0:0000000000}\r\n" +
                "EndHTML:{1:0000000000}\r\n" +
                "StartFragment:{2:0000000000}\r\n" +
                "EndFragment:{3:0000000000}\r\n";

            if (!string.IsNullOrEmpty(sourceUrl))
                header += $"SourceURL:{sourceUrl}\r\n";

            string prefix = "<html>\r\n<body>\r\n<!--StartFragment-->";
            string suffix = "<!--EndFragment-->\r\n</body>\r\n</html>";

            string dummyHeader = string.Format(header, 0, 0, 0, 0);
            int headerByteCount = Encoding.UTF8.GetByteCount(dummyHeader);
            int startFragment = headerByteCount + Encoding.UTF8.GetByteCount(prefix);
            int fragmentByteCount = Encoding.UTF8.GetByteCount(htmlFragment);
            int endFragment = startFragment + fragmentByteCount;
            int endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);

            string finalHeader = string.Format(header, headerByteCount, endHtml, startFragment, endFragment);
            return finalHeader + prefix + htmlFragment + suffix;
        }

        public void Dispose()
        {
            UnregisterHotkey();
        }
    }
}
