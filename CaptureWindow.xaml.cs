using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace StickyNotes__
{
    public partial class CaptureWindow : Window
    {


        private System.Windows.Point _startPoint;
        private bool _isSelecting;
        private BitmapSource? _screenBitmap;
        
        public string? CapturedImagePath { get; private set; }

        public CaptureWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position fullscreen on primary monitor
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            this.Left = 0;
            this.Top = 0;
            this.Width = screenWidth;
            this.Height = screenHeight;

            DimOverlay.Width = screenWidth;
            DimOverlay.Height = screenHeight;

            // Capture screen
            _screenBitmap = CaptureScreen(0, 0, (int)screenWidth, (int)screenHeight);
            BackgroundImage.Source = _screenBitmap;
        }

        private BitmapSource CaptureScreen(int x, int y, int width, int height)
        {
            IntPtr hdcScreen = Win32Helper.GetDC(IntPtr.Zero);
            IntPtr hdcMem = Win32Helper.CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap = Win32Helper.CreateCompatibleBitmap(hdcScreen, width, height);
            IntPtr hOld = Win32Helper.SelectObject(hdcMem, hBitmap);

            Win32Helper.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, Win32Helper.SRCCOPY);
            Win32Helper.SelectObject(hdcMem, hOld);

            BitmapSource bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );

            // Clean up GDI objects
            Win32Helper.DeleteDC(hdcMem);
            Win32Helper.DeleteObject(hBitmap);
            Win32Helper.ReleaseDC(IntPtr.Zero, hdcScreen);

            return bmp;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _startPoint = e.GetPosition(CaptureCanvas);
                _isSelecting = true;
                
                SelectionBorder.Visibility = Visibility.Visible;
                CroppedImageDisplay.Visibility = Visibility.Visible;
            }
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isSelecting && _screenBitmap != null)
            {
                System.Windows.Point currentPoint = e.GetPosition(CaptureCanvas);
                
                double x = Math.Min(_startPoint.X, currentPoint.X);
                double y = Math.Min(_startPoint.Y, currentPoint.Y);
                double width = Math.Max(1, Math.Abs(_startPoint.X - currentPoint.X));
                double height = Math.Max(1, Math.Abs(_startPoint.Y - currentPoint.Y));

                // Position selection border
                Canvas.SetLeft(SelectionBorder, x);
                Canvas.SetTop(SelectionBorder, y);
                SelectionBorder.Width = width;
                SelectionBorder.Height = height;

                // Crop & position image cut-out overlay
                try
                {
                    var rect = new Int32Rect((int)x, (int)y, (int)width, (int)height);
                    var croppedBmp = new CroppedBitmap(_screenBitmap, rect);
                    
                    CroppedImageDisplay.Source = croppedBmp;
                    Canvas.SetLeft(CroppedImageDisplay, x);
                    Canvas.SetTop(CroppedImageDisplay, y);
                }
                catch
                {
                    // Ignore crops that exceed bounds
                }
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _isSelecting && _screenBitmap != null)
            {
                _isSelecting = false;
                System.Windows.Point endPoint = e.GetPosition(CaptureCanvas);

                double x = Math.Min(_startPoint.X, endPoint.X);
                double y = Math.Min(_startPoint.Y, endPoint.Y);
                double width = Math.Abs(_startPoint.X - endPoint.X);
                double height = Math.Abs(_startPoint.Y - endPoint.Y);

                if (width > 5 && height > 5)
                {
                    try
                    {
                        var rect = new Int32Rect((int)x, (int)y, (int)width, (int)height);
                        var croppedBmp = new CroppedBitmap(_screenBitmap, rect);

                        // Save to images directory
                        string filename = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0, 6)}.png";
                        string filepath = System.IO.Path.Combine(AppConfig.ImagesDir, filename);
                        using (var fileStream = new FileStream(filepath, FileMode.Create))
                        {
                            BitmapEncoder encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(croppedBmp));
                            encoder.Save(fileStream);
                        }

                        CapturedImagePath = filepath;
                        this.DialogResult = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to save screenshot: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        this.DialogResult = false;
                    }
                }
                else
                {
                    this.DialogResult = false;
                }
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
            }
        }
    }
}
