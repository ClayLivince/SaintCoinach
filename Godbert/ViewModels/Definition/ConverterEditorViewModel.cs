using System;
using System.Collections.ObjectModel;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using SaintCoinach.Ex.Relational;
using SaintCoinach.Ex.Relational.ValueConverters;

namespace Godbert.ViewModels.Definition {
    public abstract class ConverterEditorViewModel : ObservableBase {
        public SheetEditorViewModel Sheet { get; }

        public abstract string TypeKey { get; }

        public string TypeLabel => TypeKey;

        protected ConverterEditorViewModel(SheetEditorViewModel sheet) {
            Sheet = sheet;
        }

        protected void MarkDirty() {
            Sheet?.MarkDirty();
            OnPropertyChanged(nameof(Summary));
        }

        public abstract IValueConverter Build();

        public virtual string Summary => TypeKey;

        public static ConverterEditorViewModel FromConverter(IValueConverter converter, SheetEditorViewModel sheet) {
            switch (converter) {
                case null:
                    return null;
                case SheetLinkConverter link:
                    return new LinkConverterEditorViewModel(sheet, link.TargetSheet);
                case MultiReferenceConverter multi:
                    return new MultiRefConverterEditorViewModel(sheet, multi.Targets);
                case ColorConverter color:
                    return new ColorConverterEditorViewModel(sheet, color.IncludesAlpha);
                case IconConverter _:
                    return new SimpleConverterEditorViewModel(sheet, "icon", () => new IconConverter());
                case GenericReferenceConverter _:
                    return new SimpleConverterEditorViewModel(sheet, "generic", () => new GenericReferenceConverter());
                case TomestoneOrItemReferenceConverter _:
                    return new SimpleConverterEditorViewModel(sheet, "tomestone", () => new TomestoneOrItemReferenceConverter());
                case ComplexLinkConverter complex:
                    return new ComplexLinkConverterEditorViewModel(sheet, complex);
                default:
                    return new RawJsonConverterEditorViewModel(sheet, converter);
            }
        }

        public static ConverterEditorViewModel CreateByTypeKey(string typeKey, SheetEditorViewModel sheet) {
            switch (typeKey) {
                case "link":      return new LinkConverterEditorViewModel(sheet, null);
                case "multiref":  return new MultiRefConverterEditorViewModel(sheet, Array.Empty<string>());
                case "color":     return new ColorConverterEditorViewModel(sheet, false);
                case "icon":      return new SimpleConverterEditorViewModel(sheet, "icon", () => new IconConverter());
                case "generic":   return new SimpleConverterEditorViewModel(sheet, "generic", () => new GenericReferenceConverter());
                case "tomestone": return new SimpleConverterEditorViewModel(sheet, "tomestone", () => new TomestoneOrItemReferenceConverter());
                default: throw new ArgumentException($"Unknown converter type '{typeKey}'.", nameof(typeKey));
            }
        }

        public static readonly string[] EditableTypeKeys = new[] {
            "link", "multiref", "color", "icon", "generic", "tomestone"
        };
    }

    public sealed class LinkConverterEditorViewModel : ConverterEditorViewModel {
        private string _TargetSheet;

        public override string TypeKey => "link";

        public string TargetSheet {
            get => _TargetSheet;
            set { if (_TargetSheet != value) { _TargetSheet = value; OnPropertyChanged(nameof(TargetSheet)); MarkDirty(); } }
        }

        public override string Summary => $"link → {_TargetSheet ?? "(unset)"}";

        public LinkConverterEditorViewModel(SheetEditorViewModel sheet, string targetSheet) : base(sheet) {
            _TargetSheet = targetSheet;
        }

        public override IValueConverter Build() {
            return new SheetLinkConverter { TargetSheet = _TargetSheet };
        }
    }

    public sealed class MultiRefConverterEditorViewModel : ConverterEditorViewModel {
        public override string TypeKey => "multiref";

        public ObservableCollection<MultiRefTarget> Targets { get; } = new();

        public override string Summary => $"multiref → [{string.Join(", ", Targets.Select(t => t.Name))}]";

        public MultiRefConverterEditorViewModel(SheetEditorViewModel sheet, string[] targets) : base(sheet) {
            if (targets != null) {
                foreach (var t in targets)
                    Targets.Add(new MultiRefTarget(this, t));
            }
        }

        public void AddTarget(string name = null) {
            Targets.Add(new MultiRefTarget(this, name));
            NotifyTargetsChanged();
            MarkDirty();
        }

        public void RemoveTarget(MultiRefTarget target) {
            Targets.Remove(target);
            NotifyTargetsChanged();
            MarkDirty();
        }

        internal void NotifyTargetsChanged() {
            OnPropertyChanged(nameof(Summary));
            MarkDirty();
        }

        public override IValueConverter Build() {
            return new MultiReferenceConverter {
                Targets = Targets.Select(t => t.Name).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
            };
        }
    }

    public sealed class MultiRefTarget : ObservableBase {
        private readonly MultiRefConverterEditorViewModel _Parent;
        private string _Name;
        public string Name {
            get => _Name;
            set { if (_Name != value) { _Name = value; OnPropertyChanged(nameof(Name)); _Parent.NotifyTargetsChanged(); } }
        }

        public MultiRefTarget(MultiRefConverterEditorViewModel parent, string name) {
            _Parent = parent;
            _Name = name;
        }
    }

    public sealed class ColorConverterEditorViewModel : ConverterEditorViewModel {
        private bool _IncludesAlpha;

        public override string TypeKey => "color";

        public bool IncludesAlpha {
            get => _IncludesAlpha;
            set { if (_IncludesAlpha != value) { _IncludesAlpha = value; OnPropertyChanged(nameof(IncludesAlpha)); MarkDirty(); } }
        }

        public override string Summary => _IncludesAlpha ? "color (with alpha)" : "color";

        public ColorConverterEditorViewModel(SheetEditorViewModel sheet, bool includesAlpha) : base(sheet) {
            _IncludesAlpha = includesAlpha;
        }

        public override IValueConverter Build() {
            return new ColorConverter { IncludesAlpha = _IncludesAlpha };
        }
    }

    public sealed class SimpleConverterEditorViewModel : ConverterEditorViewModel {
        private readonly string _TypeKey;
        private readonly Func<IValueConverter> _Factory;

        public override string TypeKey => _TypeKey;

        public SimpleConverterEditorViewModel(SheetEditorViewModel sheet, string typeKey, Func<IValueConverter> factory) : base(sheet) {
            _TypeKey = typeKey;
            _Factory = factory;
        }

        public override IValueConverter Build() => _Factory();
    }

    /// <summary>
    /// Read-only fallback editor for converters whose shape is too internal to surface
    /// safely as form fields (currently <see cref="ComplexLinkConverter"/>). Stores the
    /// original instance and rebuilds it verbatim on save.
    /// </summary>
    public sealed class ComplexLinkConverterEditorViewModel : ConverterEditorViewModel {
        private readonly ComplexLinkConverter _Original;

        public override string TypeKey => "complexlink";

        public string Json { get; }

        public override string Summary => "complexlink (read-only — edit JSON directly)";

        public ComplexLinkConverterEditorViewModel(SheetEditorViewModel sheet, ComplexLinkConverter original) : base(sheet) {
            _Original = original;
            Json = original == null
                ? "{}"
                : JsonConvert.SerializeObject(original.ToJson(), Formatting.Indented);
        }

        public override IValueConverter Build() {
            return _Original;
        }
    }

    public sealed class RawJsonConverterEditorViewModel : ConverterEditorViewModel {
        private readonly IValueConverter _Original;

        public override string TypeKey => "raw";

        public string Json { get; }

        public override string Summary => $"({_Original?.GetType().Name ?? "null"}) — read-only";

        public RawJsonConverterEditorViewModel(SheetEditorViewModel sheet, IValueConverter original) : base(sheet) {
            _Original = original;
            Json = original == null ? "null" : JsonConvert.SerializeObject(original.ToJson(), Formatting.Indented);
        }

        public override IValueConverter Build() => _Original;
    }
}
