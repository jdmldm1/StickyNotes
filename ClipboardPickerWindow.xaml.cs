using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StickyNotes__
{
    // Lets the user pick one item from clipboard history and convert it into a note, without
    // permanently reserving sidebar space for a clipboard history list.
    public partial class ClipboardPickerWindow : Window
    {
        private readonly MainWindow _owner;

        public ClipboardPickerWindow(MainWindow owner)
        {
            InitializeComponent();
            _owner = owner;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) => RefreshItems();

        public void RefreshItems()
        {
            var history = _owner.ClipboardHistory;
            ClipboardItemsControl.ItemsSource = null;
            ClipboardItemsControl.ItemsSource = history;
            EmptyStateText.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();

        private async void AddNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id) return;

            var item = _owner.ClipboardHistory.FirstOrDefault(i => i.Id == id);
            if (item == null) return;

            btn.IsEnabled = false;
            try
            {
                int noteId = await _owner.CreateNoteFromClipboardItemAsync(item);
                _owner.OpenNoteWindow(noteId);
                this.Close();
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }
}
