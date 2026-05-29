using System.Windows;

using Godbert.Repositories;

namespace Godbert.Views {
    public partial class DefinitionServerSettingsWindow : Window {
        public DefinitionServerSettingsWindow() {
            InitializeComponent();

            UrlBox.Text = string.IsNullOrWhiteSpace(Settings.Default.DefinitionServerUrl)
                ? DefinitionSyncClient.DefaultServerUrl
                : Settings.Default.DefinitionServerUrl;
            UserBox.Text = Settings.Default.DefinitionServerUser ?? string.Empty;
            PinBox.Password = Settings.Default.DefinitionServerPin ?? string.Empty;
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            Settings.Default.DefinitionServerUrl = UrlBox.Text?.Trim();
            Settings.Default.DefinitionServerUser = UserBox.Text?.Trim();
            Settings.Default.DefinitionServerPin = PinBox.Password;
            Settings.Default.Save();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }
    }
}
