using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace StickyNotes__
{
    public partial class NoteWindow : Window
    {
        private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Paste)
            {
                if (HandleClipboardPaste())
                {
                    e.Handled = true;
                }
            }
        }
        private bool HandleClipboardPaste()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files != null && files.Count > 0)
                    {
                        var list = new List<string>();
                        foreach (var f in files)
                        {
                            if (f != null) list.Add(f);
                        }
                        AttachFiles(list);
                        return true;
                    }
                }

                if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image != null)
                    {
                        string tempFileName = $"PastedImage_{Guid.NewGuid():N}.png";
                        string tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

                        using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                        {
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                            encoder.Save(fileStream);
                        }

                        AttachFiles(new[] { tempFilePath });

                        try { File.Delete(tempFilePath); } catch {}
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to process paste: " + ex.Message, "Paste Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }
    }
}
