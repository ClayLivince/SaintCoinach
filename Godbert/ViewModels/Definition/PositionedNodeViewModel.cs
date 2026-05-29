using SaintCoinach.Ex.Relational.Definition;

namespace Godbert.ViewModels.Definition {
    public sealed class PositionedNodeViewModel : ObservableBase {
        private int _Index;
        private DefinitionNodeViewModel _Node;

        public SheetEditorViewModel Sheet { get; }

        public int Index {
            get => _Index;
            set { if (_Index != value) { _Index = value; OnPropertyChanged(nameof(Index)); OnPropertyChanged(nameof(DisplayName)); Sheet?.MarkDirty(); } }
        }

        public DefinitionNodeViewModel Node {
            get => _Node;
            set { _Node = value; OnPropertyChanged(nameof(Node)); OnPropertyChanged(nameof(DisplayName)); Sheet?.MarkDirty(); }
        }

        public string DisplayName => $"[{_Index}] {_Node?.DisplayName}";

        public PositionedNodeViewModel(SheetEditorViewModel sheet, int index, DefinitionNodeViewModel node) {
            Sheet = sheet;
            _Index = index;
            _Node = node;
        }

        public PositionedDataDefinition Build() {
            return new PositionedDataDefinition {
                Index = _Index,
                InnerDefinition = _Node?.Build()
            };
        }

        public static PositionedNodeViewModel FromDefinition(PositionedDataDefinition def, SheetEditorViewModel sheet) {
            return new PositionedNodeViewModel(sheet, def.Index, DefinitionNodeViewModel.FromInner(def.InnerDefinition, sheet));
        }
    }
}
