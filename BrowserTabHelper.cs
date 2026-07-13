using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace StickyNotes__
{
    public record BrowserTab(string Title, string Url);

    public static class BrowserTabHelper
    {
        private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge"
        };

        private static readonly string[] TitleSuffixPatterns =
        {
            @"\s*-\s*Google Chrome$",
            @"\s*-\s*Microsoft\s*Edge$"
        };

        public static List<BrowserTab> GetOpenTabs()
        {
            var tabs = new List<BrowserTab>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Win32Helper.EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (!Win32Helper.IsWindowVisible(hwnd)) return true;

                    var classNameBuilder = new StringBuilder(256);
                    Win32Helper.GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity);
                    if (classNameBuilder.ToString() != "Chrome_WidgetWin_1") return true;

                    Win32Helper.GetWindowThreadProcessId(hwnd, out uint pid);
                    string processName;
                    try
                    {
                        processName = Process.GetProcessById((int)pid).ProcessName;
                    }
                    catch
                    {
                        return true;
                    }
                    if (!BrowserProcessNames.Contains(processName)) return true;

                    var titleBuilder = new StringBuilder(512);
                    Win32Helper.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                    string windowTitle = titleBuilder.ToString();
                    if (string.IsNullOrWhiteSpace(windowTitle)) return true;

                    string? url = TryGetAddressBarUrl(hwnd);
                    if (string.IsNullOrWhiteSpace(url)) return true;
                    if (!seenUrls.Add(url)) return true;

                    tabs.Add(new BrowserTab(CleanTitle(windowTitle), url));
                }
                catch
                {
                }
                return true;
            }, IntPtr.Zero);

            return tabs;
        }

        private static string? TryGetAddressBarUrl(IntPtr hwnd)
        {
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                if (root == null) return null;

                var condition = new PropertyCondition(AutomationElement.ClassNameProperty, "OmniboxViewViews");

                var addressBar = root.FindFirst(TreeScope.Descendants, condition);
                if (addressBar == null) return null;

                if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) && patternObj is ValuePattern valuePattern)
                {
                    return NormalizeUrl(valuePattern.Current.Value?.Trim() ?? "");
                }
            }
            catch
            {
            }
            return null;
        }

        private static string? NormalizeUrl(string text)
        {
            text = text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;

            if (!Regex.IsMatch(text, @"^[a-zA-Z][a-zA-Z\d+\-.]*://"))
            {
                if (!text.Contains(' '))
                    text = "https://" + text;
                else
                    return null;
            }

            return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : null;
        }

        private static string CleanTitle(string windowTitle)
        {
            string title = windowTitle;
            foreach (var pattern in TitleSuffixPatterns)
                title = Regex.Replace(title, pattern, "", RegexOptions.IgnoreCase);

            title = title.Trim();
            return string.IsNullOrWhiteSpace(title) ? "Untitled Tab" : title;
        }
    }
}
