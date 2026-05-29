using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Newtonsoft.Json;

using SaintCoinach.Ex.Relational;
using SaintCoinach.Ex.Relational.Definition;

namespace Godbert.ViewModels.Definition {
    using Godbert.Commands;

    public class SheetEditorViewModel : ObservableBase {
        private readonly DefinitionViewModel _Owner;
        private bool _IsDirty;
        private string _DefaultColumn;
        private bool _IsGenericReferenceTarget;
        private PositionedNodeViewModel _SelectedItem;

        public string SheetName { get; }
        public int RawColumnCount { get; }

        public ObservableCollection<PositionedNodeViewModel> Definitions { get; } = new();

        public bool IsDirty {
            get => _IsDirty;
            private set { if (_IsDirty != value) { _IsDirty = value; OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(Title)); } }
        }

        public string Title => IsDirty ? $"{SheetName} *" : SheetName;

        public string DefaultColumn {
            get => _DefaultColumn;
            set { if (_DefaultColumn != value) { _DefaultColumn = value; OnPropertyChanged(nameof(DefaultColumn)); MarkDirty(); } }
        }

        public bool IsGenericReferenceTarget {
            get => _IsGenericReferenceTarget;
            set { if (_IsGenericReferenceTarget != value) { _IsGenericReferenceTarget = value; OnPropertyChanged(nameof(IsGenericReferenceTarget)); MarkDirty(); } }
        }

        public PositionedNodeViewModel SelectedItem {
            get => _SelectedItem;
            set { _SelectedItem = value; OnPropertyChanged(nameof(SelectedItem)); }
        }

        public DefinitionPreviewViewModel Preview { get; }

        public SaintCoinach.ARealmReversed Realm => _Owner.Realm;

        public IRelationalSheet LiveSheet { get; }

        public SheetEditorViewModel(DefinitionViewModel owner, string sheetName, IRelationalSheet liveSheet, SheetDefinition source) {
            _Owner = owner;
            SheetName = sheetName;
            LiveSheet = liveSheet;
            RawColumnCount = liveSheet?.Header?.Columns.Count() ?? 0;

            if (source != null) {
                _DefaultColumn = source.DefaultColumn;
                _IsGenericReferenceTarget = source.IsGenericReferenceTarget;
                foreach (var def in source.DataDefinitions.OrderBy(d => d.Index))
                    Definitions.Add(PositionedNodeViewModel.FromDefinition(def, this));
            }

            Preview = new DefinitionPreviewViewModel(this);
        }

        internal void MarkDirty() {
            IsDirty = true;
        }

        #region Commands

        private ICommand _AddColumnCommand;
        private ICommand _InsertAndShiftCommand;
        private ICommand _DeleteCommand;
        private ICommand _MoveUpCommand;
        private ICommand _MoveDownCommand;
        private ICommand _SaveCommand;
        private ICommand _RevertCommand;
        private ICommand _RefreshPreviewCommand;
        private ICommand _ConvertToSingleCommand;
        private ICommand _ConvertToRepeatCommand;
        private ICommand _ConvertToGroupCommand;
        private ICommand _SubmitToServerCommand;

        public bool IsServerConfigured => _Owner.SyncClient.IsConfigured;
        public System.Windows.Visibility SubmitVisibility =>
            IsServerConfigured ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public ICommand AddColumnCommand => _AddColumnCommand ??= new DelegateCommand(OnAddColumn);
        public ICommand InsertAndShiftCommand => _InsertAndShiftCommand ??= new DelegateCommand(OnInsertAndShift);
        public ICommand DeleteCommand => _DeleteCommand ??= new DelegateCommand(OnDelete);
        public ICommand MoveUpCommand => _MoveUpCommand ??= new DelegateCommand(OnMoveUp);
        public ICommand MoveDownCommand => _MoveDownCommand ??= new DelegateCommand(OnMoveDown);
        public ICommand SaveCommand => _SaveCommand ??= new DelegateCommand(OnSave);
        public ICommand RevertCommand => _RevertCommand ??= new DelegateCommand(OnRevert);
        public ICommand RefreshPreviewCommand => _RefreshPreviewCommand ??= new DelegateCommand(() => Preview.Refresh());
        public ICommand ConvertToSingleCommand => _ConvertToSingleCommand ??= new DelegateCommand(() => ConvertSelectedTo("single"));
        public ICommand ConvertToRepeatCommand => _ConvertToRepeatCommand ??= new DelegateCommand(() => ConvertSelectedTo("repeat"));
        public ICommand ConvertToGroupCommand => _ConvertToGroupCommand ??= new DelegateCommand(() => ConvertSelectedTo("group"));
        public ICommand SubmitToServerCommand => _SubmitToServerCommand ??= new DelegateCommand(OnSubmitToServer);

        private async void OnSubmitToServer() {
            if (!_Owner.SyncClient.IsConfigured) return;

            if (IsDirty) {
                var ans = MessageBox.Show("Save changes before submitting?", "Submit to server",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (ans == MessageBoxResult.Cancel) return;
                if (ans == MessageBoxResult.Yes) OnSave();
            }

            string json;
            try {
                json = JsonConvert.SerializeObject(BuildSheetDefinition().ToJson(), Formatting.Indented);
            } catch (Exception ex) {
                MessageBox.Show($"Could not serialize definition: {ex.Message}", "Submit failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = await _Owner.SyncClient.SubmitAsync("definition", SheetName, json);
            if (result.Ok && result.IsPending)
                MessageBox.Show($"{SheetName} submitted for review. It will go live once an admin approves it.",
                    "Submitted for review", MessageBoxButton.OK, MessageBoxImage.Information);
            else if (result.Ok)
                MessageBox.Show($"{SheetName} accepted. New server version: {result.NewVersion}",
                    "Accepted", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show($"Submit failed: {result.Error}", "Submit failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnAddColumn() {
            var nextIndex = Definitions.Count == 0 ? 0 : Definitions.Max(d => d.Index + d.Node.Length);
            var node = new SingleNodeViewModel(this, "NewColumn", null);
            var positioned = new PositionedNodeViewModel(this, nextIndex, node);
            Definitions.Add(positioned);
            SelectedItem = positioned;
            MarkDirty();
        }

        private void OnInsertAndShift() {
            var anchor = SelectedItem?.Index ?? 0;
            foreach (var d in Definitions.Where(d => d.Index >= anchor).ToList())
                d.Index += 1;
            var node = new SingleNodeViewModel(this, "NewColumn", null);
            var positioned = new PositionedNodeViewModel(this, anchor, node);
            Definitions.Add(positioned);
            ResortDefinitions();
            SelectedItem = positioned;
            MarkDirty();
        }

        private void OnDelete() {
            if (SelectedItem == null) return;

            var msg = $"Delete column [{SelectedItem.Index}] {SelectedItem.Node?.DisplayName}?\n\n" +
                      "Yes: delete and shift later columns left by 1\n" +
                      "No: delete only (leaves a gap)\n" +
                      "Cancel: keep";

            var result = MessageBox.Show(msg, "Delete column", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
                return;

            var removed = SelectedItem;
            var removedIndex = removed.Index;
            Definitions.Remove(removed);

            if (result == MessageBoxResult.Yes) {
                foreach (var d in Definitions.Where(d => d.Index > removedIndex).ToList())
                    d.Index -= 1;
                ResortDefinitions();
            }

            SelectedItem = null;
            MarkDirty();
        }

        private void OnMoveUp() {
            if (SelectedItem == null) return;
            var i = Definitions.IndexOf(SelectedItem);
            if (i <= 0) return;
            Definitions.Move(i, i - 1);
            MarkDirty();
        }

        private void OnMoveDown() {
            if (SelectedItem == null) return;
            var i = Definitions.IndexOf(SelectedItem);
            if (i < 0 || i >= Definitions.Count - 1) return;
            Definitions.Move(i, i + 1);
            MarkDirty();
        }

        private void ResortDefinitions() {
            var sorted = Definitions.OrderBy(d => d.Index).ToList();
            Definitions.Clear();
            foreach (var d in sorted)
                Definitions.Add(d);
        }

        private void ConvertSelectedTo(string targetType) {
            if (SelectedItem?.Node == null) return;
            var current = SelectedItem.Node;
            DefinitionNodeViewModel replacement;
            switch (targetType) {
                case "single":
                    if (current is SingleNodeViewModel) return;
                    replacement = new SingleNodeViewModel(this, "NewColumn", null);
                    break;
                case "repeat":
                    if (current is RepeatNodeViewModel) return;
                    var inner = current is GroupNodeViewModel g ? (DefinitionNodeViewModel)g : new SingleNodeViewModel(this, (current as SingleNodeViewModel)?.Name ?? "Item", (current as SingleNodeViewModel)?.Converter);
                    replacement = new RepeatNodeViewModel(this, 2, inner);
                    break;
                case "group":
                    if (current is GroupNodeViewModel) return;
                    var grp = new GroupNodeViewModel(this);
                    grp.Children.Add(current);
                    replacement = grp;
                    break;
                default:
                    return;
            }
            SelectedItem.Node = replacement;
            MarkDirty();
        }

        private void OnSave() {
            try {
                var rebuilt = BuildSheetDefinition();

                if (Realm.GameData.Definition.TryGetSheet(SheetName, out var live)) {
                    live.DataDefinitions.Clear();
                    foreach (var d in rebuilt.DataDefinitions)
                        live.DataDefinitions.Add(d);
                    live.DefaultColumn = rebuilt.DefaultColumn;
                    live.IsGenericReferenceTarget = rebuilt.IsGenericReferenceTarget;
                    foreach (var def in live.DataDefinitions)
                        def.ResolveReferences(live);
                    live.Compile();
                    Realm.SaveSheetDefinition(live);
                } else {
                    Realm.GameData.Definition.SheetDefinitions.Add(rebuilt);
                    foreach (var def in rebuilt.DataDefinitions)
                        def.ResolveReferences(rebuilt);
                    rebuilt.Compile();
                    Realm.SaveSheetDefinition(rebuilt);
                }

                IsDirty = false;
                Preview.Refresh();
                _Owner.NotifySheetSaved(SheetName);
            } catch (Exception ex) {
                MessageBox.Show($"Save failed: {ex.Message}", "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRevert() {
            if (!IsDirty) return;
            if (MessageBox.Show("Discard unsaved changes?", "Revert", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            _Owner.ReloadSheet(SheetName);
        }

        public SheetDefinition BuildSheetDefinition() {
            var sheet = new SheetDefinition {
                Name = SheetName,
                DefaultColumn = string.IsNullOrWhiteSpace(_DefaultColumn) ? null : _DefaultColumn,
                IsGenericReferenceTarget = _IsGenericReferenceTarget
            };
            foreach (var d in Definitions.OrderBy(d => d.Index))
                sheet.DataDefinitions.Add(d.Build());
            return sheet;
        }

        #endregion
    }
}
