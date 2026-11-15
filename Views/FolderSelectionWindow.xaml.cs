using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace IrrModWorkbench.Views
{
    public partial class FolderPickerWindow : Window
    {
        public string? SelectedFolder { get; private set; }
        private string? _initialDirectory;

        public FolderPickerWindow(string prompt, string? initialDirectory)
        {
            InitializeComponent();
            PromptText.Text = prompt;
            _initialDirectory = initialDirectory;
            Title = prompt;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = PromptText.Text };
            if (!string.IsNullOrWhiteSpace(_initialDirectory) && Directory.Exists(_initialDirectory))
                dialog.InitialDirectory = _initialDirectory;
            if (dialog.ShowDialog() == true)
            {
                SelectedFolder = dialog.FolderName;
                PathBox.Text = SelectedFolder;
                OkButton.IsEnabled = true;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}