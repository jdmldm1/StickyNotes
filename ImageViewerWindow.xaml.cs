using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickyNotes__
{
    public partial class ImageViewerWindow : Window
    {
        private readonly BitmapImage _bitmap;
        private double _fitScale = 1.0;
        private double _zoomFactor = 1.0;
        private const double MinZoomFactor = 0.1;
        private const double MaxZoomFactor = 20.0;

        private Point? _panStart;
        private double _panStartH;
        private double _panStartV;

        public ImageViewerWindow(string imagePath)
        {
            InitializeComponent();

            _bitmap = new BitmapImage();
            _bitmap.BeginInit();
            _bitmap.CacheOption = BitmapCacheOption.OnLoad;
            _bitmap.UriSource = new Uri(imagePath);
            _bitmap.EndInit();

            ViewerImage.Source = _bitmap;
            ViewerImage.Width = _bitmap.PixelWidth;
            ViewerImage.Height = _bitmap.PixelHeight;

            Loaded += (s, e) => ResetZoom();
        }

        private void ComputeFitScale()
        {
            double availW = Math.Max(ImageScrollViewer.ActualWidth - 20, 50);
            double availH = Math.Max(ImageScrollViewer.ActualHeight - 20, 50);
            double scaleW = availW / _bitmap.PixelWidth;
            double scaleH = availH / _bitmap.PixelHeight;
            _fitScale = Math.Min(1.0, Math.Min(scaleW, scaleH));
        }

        private void ApplyZoom()
        {
            double scale = _fitScale * _zoomFactor;
            ViewerImage.LayoutTransform = new ScaleTransform(scale, scale);
            ZoomText.Text = $"{Math.Round(scale * 100)}%";
        }

        private void ResetZoom()
        {
            _zoomFactor = 1.0;
            ComputeFitScale();
            ApplyZoom();
        }

        private void ZoomBy(double factor)
        {
            ComputeFitScale();
            _zoomFactor = Math.Clamp(_zoomFactor * factor, MinZoomFactor, MaxZoomFactor);
            ApplyZoom();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ComputeFitScale();
            ApplyZoom();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25);

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

        private void ZoomResetButton_Click(object sender, RoutedEventArgs e) => ResetZoom();

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetImage(_bitmap);
                CopiedText.Visibility = Visibility.Visible;
                await Task.Delay(1600);
                CopiedText.Visibility = Visibility.Collapsed;
            }
            catch
            {
                MessageBox.Show("Couldn't copy the image to the clipboard.", "Copy Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15);
        }

        private void ImageScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ResetZoom();
                return;
            }

            _panStart = e.GetPosition(ImageScrollViewer);
            _panStartH = ImageScrollViewer.HorizontalOffset;
            _panStartV = ImageScrollViewer.VerticalOffset;
            ImageScrollViewer.CaptureMouse();
            ImageScrollViewer.Cursor = Cursors.SizeAll;
        }

        private void ImageScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_panStart == null || e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(ImageScrollViewer);
            var delta = pos - _panStart.Value;
            ImageScrollViewer.ScrollToHorizontalOffset(_panStartH - delta.X);
            ImageScrollViewer.ScrollToVerticalOffset(_panStartV - delta.Y);
        }

        private void ImageScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _panStart = null;
            ImageScrollViewer.ReleaseMouseCapture();
            ImageScrollViewer.Cursor = Cursors.Arrow;
        }
    }
}
