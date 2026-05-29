using System.Windows;

namespace Godbert.Views {
    public partial class RemarkDialog : Window {
        public string RemarkLabel { get; private set; }
        public string RemarkNote { get; private set; }

        public RemarkDialog(string setId, string label, string note) {
            InitializeComponent();
            HeaderText.Text = $"Remark for icon set {setId}";
            LabelBox.Text = label ?? string.Empty;
            NoteBox.Text = note ?? string.Empty;
            RemarkLabel = LabelBox.Text;
            RemarkNote = NoteBox.Text;
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            RemarkLabel = (LabelBox.Text ?? string.Empty).Trim();
            RemarkNote = (NoteBox.Text ?? string.Empty).Trim();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }
    }
}
