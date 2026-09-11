using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace StickyNotes__
{
    public static class SecurityHelper
    {
        private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".msi", ".scr", ".com", ".js", ".hta", ".pif", ".wsf", ".cpl"
        };

        public static bool IsSafeWebUri(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return IsSafeWebUri(uri);
            }
            return false;
        }

        public static bool IsSafeWebUri(Uri? uri)
        {
            if (uri == null) return false;
            string scheme = uri.Scheme.ToLowerInvariant();
            return scheme == "http" || scheme == "https" || scheme == "mailto";
        }

        public static bool ConfirmDangerousFileExecution(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (DangerousExtensions.Contains(ext))
            {
                var result = MessageBox.Show(
                    $"The file \"{Path.GetFileName(filePath)}\" is an executable or script and could potentially be harmful.\n\nAre you sure you want to run it?",
                    "Security Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                return result == MessageBoxResult.Yes;
            }
            return true;
        }
    }
}
