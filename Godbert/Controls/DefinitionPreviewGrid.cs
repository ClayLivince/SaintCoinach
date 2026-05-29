using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using Godbert.ViewModels.Definition;

using SaintCoinach.Ex;

namespace Godbert.Controls {
    /// <summary>
    /// Small DataGrid dedicated to the Definition tab preview. Tolerant by design:
    /// each cell routes through <see cref="DefinitionPreviewViewModel.GetCell"/>
    /// which swallows converter exceptions. Header click sets the VM's
    /// SelectedColumnIndex (drives the raw-bytes inspector below).
    /// </summary>
    public class DefinitionPreviewGrid : DataGrid {
        public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
            nameof(Preview), typeof(DefinitionPreviewViewModel), typeof(DefinitionPreviewGrid),
            new PropertyMetadata(null, OnPreviewChanged));

        public DefinitionPreviewViewModel Preview {
            get => (DefinitionPreviewViewModel)GetValue(PreviewProperty);
            set => SetValue(PreviewProperty, value);
        }

        /// <summary>
        /// Hard cap on preview columns. Some sheets are pathologically wide (CharaMakeType
        /// ~3500 cols, Quest ~1650) — building that many DataGrid columns + per-column styles
        /// freezes/crashes the UI even with virtualization. The preview is for spot-checking;
        /// the editor tree shows every definition regardless.
        /// </summary>
        private const int MaxPreviewColumns = 512;

        public DefinitionPreviewGrid() {
            AutoGenerateColumns = false;
            IsReadOnly = true;
            CanUserAddRows = false;
            CanUserDeleteRows = false;
            HeadersVisibility = DataGridHeadersVisibility.All;
            GridLinesVisibility = DataGridGridLinesVisibility.All;
            // Virtualize both axes and use fixed widths so wide/tall sheets don't measure
            // every cell (the cause of the freeze/crash on Quest / CharaMakeType).
            EnableColumnVirtualization = true;
            EnableRowVirtualization = true;
            ColumnWidth = new DataGridLength(110);
        }

        private static void OnPreviewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var grid = (DefinitionPreviewGrid)d;
            if (e.OldValue is DefinitionPreviewViewModel oldVm)
                oldVm.PropertyChanged -= grid.OnVmPropertyChanged;
            if (e.NewValue is DefinitionPreviewViewModel newVm)
                newVm.PropertyChanged += grid.OnVmPropertyChanged;
            grid.Rebuild();
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(DefinitionPreviewViewModel.EditedDefinition) ||
                e.PropertyName == nameof(DefinitionPreviewViewModel.Rows))
                Rebuild();
        }

        private void Rebuild() {
            try {
                RebuildCore();
            } catch (Exception ex) {
                Columns.Clear();
                ItemsSource = null;
                System.Diagnostics.Debug.WriteLine($"DefinitionPreviewGrid.Rebuild failed: {ex}");
            }
        }

        private void RebuildCore() {
            Columns.Clear();
            ItemsSource = null;

            var vm = Preview;
            if (vm?.Sheet == null || vm.EditedDefinition == null)
                return;

            var keyPath = vm.Sheet.Header.Variant == 1 ? "Key" : "FullKey";
            Columns.Add(new DataGridTextColumn {
                Header = "Key",
                Binding = new Binding(keyPath) { Mode = BindingMode.OneWay },
                IsReadOnly = true
            });

            var rawColumnCount = vm.Sheet.Header.Columns.Count();
            var shown = Math.Min(rawColumnCount, MaxPreviewColumns);
            for (int i = 0; i < shown; i++) {
                var idx = i;
                var name = vm.EditedDefinition.GetColumnName(idx);
                var typeName = vm.EditedDefinition.GetValueTypeName(idx);

                var header = string.IsNullOrEmpty(name) ? $"[{idx}]" : $"[{idx}] {name}";
                if (!string.IsNullOrEmpty(typeName))
                    header += $"\n{typeName}";

                var column = new DataGridTextColumn {
                    Header = header,
                    IsReadOnly = true,
                    Binding = new Binding {
                        Mode = BindingMode.OneWay,
                        Converter = new CellConverter(vm, idx)
                    },
                    CellStyle = BuildCellStyle(vm, idx)
                };
                column.SetValue(TagProperty, idx);
                Columns.Add(column);
            }

            if (rawColumnCount > MaxPreviewColumns) {
                Columns.Add(new DataGridTextColumn {
                    Header = $"(+{rawColumnCount - MaxPreviewColumns} more columns — preview truncated)",
                    IsReadOnly = true,
                    Width = new DataGridLength(260)
                });
            }

            ItemsSource = vm.Rows;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
            base.OnMouseLeftButtonDown(e);

            var header = FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
            if (header?.Column == null) return;

            var tag = header.Column.GetValue(TagProperty);
            if (tag is int idx && Preview != null) {
                Preview.SelectedColumnIndex = idx;
                e.Handled = true;
            }
        }

        private static T FindAncestor<T>(DependencyObject start) where T : DependencyObject {
            while (start != null && !(start is T))
                start = VisualTreeHelper.GetParent(start);
            return start as T;
        }

        private static readonly Brush CastErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0xC8));
        private static readonly Brush UnresolvedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xB0));

        static DefinitionPreviewGrid() {
            CastErrorBrush.Freeze();
            UnresolvedBrush.Freeze();
        }

        private static Style BuildCellStyle(DefinitionPreviewViewModel vm, int index) {
            var style = new Style(typeof(DataGridCell));
            // Empty-path binding resolves against the cell's DataContext = the row item.
            style.Setters.Add(new Setter(BackgroundProperty, new Binding {
                Mode = BindingMode.OneWay,
                Converter = new StateBrushConverter(vm, index)
            }));

            // An explicit cell style replaces the theme default, which carries the selection
            // highlight — re-add it so selecting a row stays visible instead of showing our
            // state background.
            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(BackgroundProperty, SystemColors.HighlightBrush));
            selected.Setters.Add(new Setter(ForegroundProperty, SystemColors.HighlightTextBrush));
            style.Triggers.Add(selected);

            return style;
        }

        private sealed class CellConverter : IValueConverter {
            private readonly DefinitionPreviewViewModel _Preview;
            private readonly int _Index;

            public CellConverter(DefinitionPreviewViewModel preview, int index) {
                _Preview = preview;
                _Index = index;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                var row = value as IRow;
                if (row == null) return null;
                return _Preview.GetCell(row, _Index).Display;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                throw new NotImplementedException();
            }
        }

        private sealed class StateBrushConverter : IValueConverter {
            private readonly DefinitionPreviewViewModel _Preview;
            private readonly int _Index;

            public StateBrushConverter(DefinitionPreviewViewModel preview, int index) {
                _Preview = preview;
                _Index = index;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                var row = value as IRow;
                if (row == null) return Brushes.Transparent;
                switch (_Preview.GetCell(row, _Index).State) {
                    case CellState.CastError: return CastErrorBrush;
                    case CellState.UnresolvedLink: return UnresolvedBrush;
                    default: return Brushes.Transparent;
                }
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                throw new NotImplementedException();
            }
        }
    }

    /// <summary>
    /// Companion control: untyped raw-bytes inspector for whichever column the user
    /// last clicked. One row per preview row, two columns: row Key and raw value.
    /// </summary>
    public class DefinitionRawInspectorGrid : DataGrid {
        public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
            nameof(Preview), typeof(DefinitionPreviewViewModel), typeof(DefinitionRawInspectorGrid),
            new PropertyMetadata(null, OnPreviewChanged));

        public DefinitionPreviewViewModel Preview {
            get => (DefinitionPreviewViewModel)GetValue(PreviewProperty);
            set => SetValue(PreviewProperty, value);
        }

        public DefinitionRawInspectorGrid() {
            AutoGenerateColumns = false;
            IsReadOnly = true;
            CanUserAddRows = false;
            CanUserDeleteRows = false;
            HeadersVisibility = DataGridHeadersVisibility.All;
            EnableColumnVirtualization = true;
            EnableRowVirtualization = true;
        }

        private static void OnPreviewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var grid = (DefinitionRawInspectorGrid)d;
            if (e.OldValue is DefinitionPreviewViewModel oldVm)
                oldVm.PropertyChanged -= grid.OnVmPropertyChanged;
            if (e.NewValue is DefinitionPreviewViewModel newVm)
                newVm.PropertyChanged += grid.OnVmPropertyChanged;
            grid.Rebuild();
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(DefinitionPreviewViewModel.SelectedColumnIndex) ||
                e.PropertyName == nameof(DefinitionPreviewViewModel.Rows))
                Rebuild();
        }

        private void Rebuild() {
            Columns.Clear();
            ItemsSource = null;

            var vm = Preview;
            if (vm?.Rows == null || !vm.SelectedColumnIndex.HasValue) return;

            var idx = vm.SelectedColumnIndex.Value;

            var keyPath = vm.Sheet != null && vm.Sheet.Header.Variant == 1 ? "Key" : "FullKey";
            Columns.Add(new DataGridTextColumn {
                Header = "Key",
                Binding = new Binding(keyPath) { Mode = BindingMode.OneWay },
                IsReadOnly = true
            });

            string typeName = "";
            try {
                if (vm.Sheet != null) {
                    var col = vm.Sheet.Header.Columns.FirstOrDefault(c => c.Index == idx);
                    if (col != null)
                        typeName = $" ({col.Reader.Type.Name})";
                }
            } catch { }

            Columns.Add(new DataGridTextColumn {
                Header = $"Raw [{idx}]{typeName}",
                IsReadOnly = true,
                Binding = new Binding {
                    Mode = BindingMode.OneWay,
                    Converter = new RawConverter(vm, idx)
                }
            });

            ItemsSource = vm.Rows;
        }

        private sealed class RawConverter : IValueConverter {
            private readonly DefinitionPreviewViewModel _Preview;
            private readonly int _Index;

            public RawConverter(DefinitionPreviewViewModel preview, int index) {
                _Preview = preview;
                _Index = index;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                var row = value as IRow;
                if (row == null) return null;
                var raw = _Preview.GetRaw(row, _Index);
                return raw?.ToString() ?? "(null)";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                throw new NotImplementedException();
            }
        }
    }
}
