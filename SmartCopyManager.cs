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
        private GlobalHotkey _hotkey;
        private InputSimulator _simulator;
        private static readonly HttpClient _httpClient = new HttpClient();

        public SmartCopyManager()
        {
            _simulator = new InputSimulator();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow ?? new Window()).EnsureHandle();
            
            // Ctrl + Shift + C (Smart Copy - Base64 Embed)
            _hotkey = new GlobalHotkey(GlobalHotkey.MOD_CONTROL | GlobalHotkey.MOD_SHIFT, (uint)VirtualKeyCode.VK_C, hwnd, 9006);
            _hotkey.HotkeyPressed += (s, e) => PerformSmartCopy();
        }

        private async void PerformSmartCopy()
        {
            // Simulate normal copy (Ctrl+C)
            _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
            
            await Task.Delay(300); // Give OS time to copy to clipboard
            
            try
            {
                IDataObject clipboardData = Clipboard.GetDataObject();
                if (clipboardData != null && clipboardData.GetDataPresent(DataFormats.Html))
                {
                    string? rawHtml = clipboardData.GetData(DataFormats.Html) as string;
                    if (string.IsNullOrEmpty(rawHtml)) return;

                    string newHtml = await EmbedImagesInHtmlAsync(rawHtml);
                    
                    if (!string.IsNullOrWhiteSpace(newHtml))
                    {
                        // To avoid locking issues and OLE Wrapper problems, we clear and precisely set what we need
                        for (int i = 0; i < 5; i++)
                        {
                            try
                            {
                                DataObject newDataObject = new DataObject();
                                newDataObject.SetData(DataFormats.Html, newHtml);

                                if (clipboardData.GetDataPresent(DataFormats.Rtf))
                                {
                                    newDataObject.SetData(DataFormats.Rtf, clipboardData.GetData(DataFormats.Rtf));
                                }
                                if (clipboardData.GetDataPresent(DataFormats.UnicodeText))
                                {
                                    newDataObject.SetData(DataFormats.UnicodeText, clipboardData.GetData(DataFormats.UnicodeText));
                                }
                                else if (clipboardData.GetDataPresent(DataFormats.Text))
                                {
                                    newDataObject.SetData(DataFormats.Text, clipboardData.GetData(DataFormats.Text));
                                }

                                Clipboard.SetDataObject(newDataObject, true);
                                break; // Success
                            }
                            catch (System.Runtime.InteropServices.COMException)
                            {
                                await Task.Delay(100); // Retry if clipboard is locked
                            }
                        }
                    }
                }
            }
            catch
            {
                // Suppress exception to not crash the background tool
            }
        }

        private async Task<string> EmbedImagesInHtmlAsync(string rawHtml)
        {
            string? sourceUrl = null;
            var matchUrl = Regex.Match(rawHtml, @"SourceURL:(.+?)\r?\n");
            if (matchUrl.Success)
            {
                sourceUrl = matchUrl.Groups[1].Value.Trim();
            }

            string fragment = rawHtml;
            var matchFrag = Regex.Match(rawHtml, @"<!--StartFragment-->(.*?)<!--EndFragment-->", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (matchFrag.Success)
            {
                fragment = matchFrag.Groups[1].Value;
            }

            // Convert inline SVGs (often used by MathJax for equations) to Base64 embedded img tags
            fragment = Regex.Replace(fragment, @"<svg[^>]*>.*?</svg>", matchSvg =>
            {
                try
                {
                    string svgContent = matchSvg.Value;
                    string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svgContent));
                    return $"<img src=\"data:image/svg+xml;base64,{base64}\" alt=\"SVG/Math\" />";
                }
                catch
                {
                    return matchSvg.Value;
                }
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Find all image tags and their src attributes
            string pattern = @"<img[^>]+src\s*=\s*[""']([^""']+)[""'][^>]*>";
            var matches = Regex.Matches(fragment, pattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                string originalSrc = match.Groups[1].Value;
                if (originalSrc.StartsWith("data:image", StringComparison.OrdinalIgnoreCase)) 
                    continue; // Already embedded

                try
                {
                    string absoluteUrl = originalSrc;
                    
                    // Resolve relative URLs using SourceURL
                    if (!originalSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(sourceUrl))
                    {
                        if (Uri.TryCreate(new Uri(sourceUrl!), originalSrc, out Uri? resultUri))
                        {
                            absoluteUrl = resultUri.ToString();
                        }
                    }

                    if (absoluteUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        // Download the image bytes
                        byte[] imageBytes = await _httpClient.GetByteArrayAsync(absoluteUrl);
                        string base64 = Convert.ToBase64String(imageBytes);
                        
                        string mimeType = "image/png"; // default
                        if (absoluteUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || absoluteUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                            mimeType = "image/jpeg";
                        else if (absoluteUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                            mimeType = "image/gif";
                        else if (absoluteUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                            mimeType = "image/svg+xml";

                        string dataUri = $"data:{mimeType};base64,{base64}";
                        
                        // Replace the src attribute in the HTML fragment
                        string newImgTag = match.Value.Replace(originalSrc, dataUri);
                        fragment = fragment.Replace(match.Value, newImgTag);
                    }
                }
                catch
                {
                    // If image fails to download, keep original src and continue silently
                }
            }

            return GenerateCFHtml(fragment, sourceUrl!);
        }

        private string GenerateCFHtml(string htmlFragment, string sourceUrl)
        {
            string header = 
                "Version:0.9\r\n" +
                "StartHTML:{0:0000000000}\r\n" +
                "EndHTML:{1:0000000000}\r\n" +
                "StartFragment:{2:0000000000}\r\n" +
                "EndFragment:{3:0000000000}\r\n";
            
            if (!string.IsNullOrEmpty(sourceUrl))
            {
                header += $"SourceURL:{sourceUrl}\r\n";
            }

            string prefix = "<html>\r\n<body>\r\n<!--StartFragment-->";
            string suffix = "<!--EndFragment-->\r\n</body>\r\n</html>";

            // Generate a dummy header to calculate exact byte counts
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
            _hotkey?.Dispose();
        }
    }
}