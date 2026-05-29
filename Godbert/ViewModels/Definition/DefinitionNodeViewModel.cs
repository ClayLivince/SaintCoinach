using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

using SaintCoinach.Ex.Relational.Definition;

namespace Godbert.ViewModels.Definition {
    public abstract class DefinitionNodeViewModel : ObservableBase {
        public SheetEditorViewModel Sheet { get; }

        public abstract string TypeKey { get; }
        public abstract string DisplayName { get; }
        public abstract int Length { get; }

        public ObservableCollection<DefinitionNodeViewModel> Children { get; } = new();

        public bool HasChildren => Children.Count > 0;
        public bool IsLeaf => Children.Count == 0;

        protected DefinitionNodeViewModel(SheetEditorViewModel sheet) {
            Sheet = sheet;
            Children.CollectionChanged += OnChildrenChanged;
        }

        private void OnChildrenChanged(object sender, NotifyCollectionChangedEventArgs e) {
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(IsLeaf));
            OnPropertyChanged(nameof(Length));
            OnPropertyChanged(nameof(DisplayName));
        }

        protected void MarkDirty() {
            Sheet?.MarkDirty();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Length));
        }

        public abstract IDataDefinition Build();

        public static DefinitionNodeViewModel FromInner(IDataDefinition def, SheetEditorViewModel sheet) {
            switch (def) {
                case SingleDataDefinition s:
                    return new SingleNodeViewModel(sheet, s.Name, ConverterEditorViewModel.FromConverter(s.Converter, sheet));
                case GroupDataDefinition g:
                    var grp = new GroupNodeViewModel(sheet);
                    foreach (var member in g.Members)
                        grp.Children.Add(FromInner(member, sheet));
                    return grp;
                case RepeatDataDefinition r:
                    var inner = FromInner(r.RepeatedDefinition, sheet);
                    return new RepeatNodeViewModel(sheet, r.RepeatCount, inner);
                default:
                    throw new ArgumentException($"Unknown definition type: {def?.GetType().Name}");
            }
        }
    }

    public sealed class SingleNodeViewModel : DefinitionNodeViewModel {
        private string _Name;
        private ConverterEditorViewModel _Converter;

        public override string TypeKey => "single";
        public override int Length => 1;
        public override string DisplayName => string.IsNullOrEmpty(_Name) ? "(unnamed)" : _Name;

        public string Name {
            get => _Name;
            set { if (_Name != value) { _Name = value; OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(DisplayName)); MarkDirty(); } }
        }

        public ConverterEditorViewModel Converter {
            get => _Converter;
            set { _Converter = value; OnPropertyChanged(nameof(Converter)); OnPropertyChanged(nameof(ConverterSummary)); MarkDirty(); }
        }

        public string ConverterSummary => _Converter?.Summary ?? "(no converter)";

        public SingleNodeViewModel(SheetEditorViewModel sheet, string name, ConverterEditorViewModel converter) : base(sheet) {
            _Name = name;
            _Converter = converter;
        }

        public void SetConverterByTypeKey(string typeKey) {
            Converter = string.IsNullOrEmpty(typeKey)
                ? null
                : ConverterEditorViewModel.CreateByTypeKey(typeKey, Sheet);
        }

        public override IDataDefinition Build() {
            return new SingleDataDefinition {
                Name = _Name,
                Converter = _Converter?.Build()
            };
        }
    }

    public sealed class GroupNodeViewModel : DefinitionNodeViewModel {
        public override string TypeKey => "group";
        public override int Length => Children.Sum(c => c.Length);
        public override string DisplayName => $"Group ({Children.Count})";

        public GroupNodeViewModel(SheetEditorViewModel sheet) : base(sheet) { }

        public override IDataDefinition Build() {
            var g = new GroupDataDefinition();
            foreach (var c in Children)
                g.Members.Add(c.Build());
            return g;
        }
    }

    public sealed class RepeatNodeViewModel : DefinitionNodeViewModel {
        private int _RepeatCount;

        public override string TypeKey => "repeat";
        public override int Length => _RepeatCount * (Children.FirstOrDefault()?.Length ?? 0);
        public override string DisplayName => $"Repeat × {_RepeatCount}";

        public int RepeatCount {
            get => _RepeatCount;
            set {
                var v = Math.Max(0, value);
                if (_RepeatCount != v) { _RepeatCount = v; OnPropertyChanged(nameof(RepeatCount)); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(Length)); MarkDirty(); }
            }
        }

        public DefinitionNodeViewModel Inner => Children.FirstOrDefault();

        public RepeatNodeViewModel(SheetEditorViewModel sheet, int repeatCount, DefinitionNodeViewModel inner) : base(sheet) {
            _RepeatCount = repeatCount;
            if (inner != null)
                Children.Add(inner);
        }

        public void ReplaceInner(DefinitionNodeViewModel newInner) {
            Children.Clear();
            if (newInner != null)
                Children.Add(newInner);
            OnPropertyChanged(nameof(Inner));
            OnPropertyChanged(nameof(Length));
            MarkDirty();
        }

        public override IDataDefinition Build() {
            return new RepeatDataDefinition {
                RepeatCount = _RepeatCount,
                RepeatedDefinition = Inner?.Build()
            };
        }
    }
}
