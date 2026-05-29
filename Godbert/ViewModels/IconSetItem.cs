namespace Godbert.ViewModels {
    /// <summary>
    /// A row in the icon-set list. Carries the set id plus its remark label so the list can render
    /// "&lt;id&gt; — &lt;label&gt;" directly (no ancestor/converter lookup), and updates in place when
    /// the label changes.
    /// </summary>
    public class IconSetItem : ObservableBase {
        public string Id { get; }

        private string _label;
        public string Label {
            get => _label;
            set {
                if (_label != value) {
                    _label = value;
                    OnPropertyChanged(nameof(Label));
                    OnPropertyChanged(nameof(Display));
                }
            }
        }

        public string Display => string.IsNullOrWhiteSpace(Label) ? Id : $"{Id} — {Label}";

        public IconSetItem(string id, string label) {
            Id = id;
            _label = label;
        }
    }
}
