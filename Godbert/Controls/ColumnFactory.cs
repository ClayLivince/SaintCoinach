using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using SaintCoinach.Ex.Relational;
using SaintCoinach.Ex.Relational.Definition;
using SaintCoinach.Ex;
using SaintCoinach.Ex.DataReaders;

namespace Godbert.Controls {
    static class ColumnFactory {
        public static DataGridColumn Create(RelationalColumn column) {
            var sheetDef = column.Header.SheetDefinition;
            Type defType = null;
            if (sheetDef != null)
                defType = sheetDef.GetValueType(column.Index);
            var targetType = defType ?? column.Reader.Type;

            var header = BuildHeader(column);
            var binding = CreateCellBinding(column);

            DataGridColumn target = null;
            if (typeof(SaintCoinach.Imaging.ImageFile).IsAssignableFrom(targetType))
                target = new RawDataGridImageColumn(column) {
                    Binding = binding,
                };
            else if (typeof(System.Drawing.Color).IsAssignableFrom(targetType))
                target = new RawDataGridColorColumn(column) {
                    Binding = binding
                };

            target = target ?? new RawDataGridTextColumn(column) {
                Binding = binding
            };

            target.Header = header;
            target.IsReadOnly = true;
            target.CanUserSort = true;
            target.CellStyle = BuildErrorCellStyle(column.Index);
            return target;
        }

        private static readonly System.Windows.Media.Brush CastErrorBrush =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xC8, 0xC8));

        static ColumnFactory() {
            CastErrorBrush.Freeze();
        }

        /// <summary>
        /// CellStyle that paints a cell red when its typed read throws (stale definition vs.
        /// real data). Mirrors the raw-mode logic so cells shown raw aren't flagged.
        /// </summary>
        private static System.Windows.Style BuildErrorCellStyle(int columnIndex) {
            var style = new System.Windows.Style(typeof(DataGridCell));
            style.Setters.Add(new System.Windows.Setter(DataGridCell.BackgroundProperty, new Binding {
                Mode = BindingMode.OneWay,
                Converter = new ErrorBrushConverter(),
                ConverterParameter = columnIndex
            }));

            // Assigning an explicit cell style replaces the theme default (which carries the
            // selection-highlight trigger), so re-add it — otherwise selected rows render with
            // our background instead of the system highlight (the "white selection" bug).
            var selected = new System.Windows.Trigger {
                Property = DataGridCell.IsSelectedProperty,
                Value = true
            };
            selected.Setters.Add(new System.Windows.Setter(DataGridCell.BackgroundProperty, System.Windows.SystemColors.HighlightBrush));
            selected.Setters.Add(new System.Windows.Setter(DataGridCell.ForegroundProperty, System.Windows.SystemColors.HighlightTextBrush));
            selected.Setters.Add(new System.Windows.Setter(DataGridCell.BorderBrushProperty, System.Windows.SystemColors.HighlightBrush));
            style.Triggers.Add(selected);

            return style;
        }

        private class ErrorBrushConverter : System.Windows.Data.IValueConverter {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
                var row = value as IRow;
                if (row == null) return System.Windows.Media.Brushes.Transparent;
                var i = System.Convert.ToInt32(parameter);
                if (ForceRaw || (RawDataGrid.ColumnSetToRaw != null && i < RawDataGrid.ColumnSetToRaw.Length && RawDataGrid.ColumnSetToRaw[i]))
                    return System.Windows.Media.Brushes.Transparent;
                try {
                    var _ = row[i];
                    return System.Windows.Media.Brushes.Transparent;
                } catch {
                    return CastErrorBrush;
                }
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
                throw new NotImplementedException();
            }
        }

        private static string BuildHeader(RelationalColumn column) {
            var sb = new StringBuilder();

            sb.Append(column.Index);
            if (!string.IsNullOrWhiteSpace(column.Name))
                sb.AppendFormat(": {0}", column.Name);
            sb.Append(Environment.NewLine);
            sb.Append(column.Reader.Type.Name);

            if (column.ValueType != column.Reader.Name)
                sb.AppendFormat(" > {0}", column.ValueType);

            if (Settings.Default.ShowOffsets) {
                if (column.Reader is PackedBooleanDataReader)
                    sb.AppendFormat(" [{0:X}&{1:X2}]", column.Offset, ((PackedBooleanDataReader)column.Reader).Mask);
                else
                    sb.AppendFormat(" [{0:X}]", column.Offset);
            }

            return sb.ToString();
        }
        private static Binding CreateCellBinding(RelationalColumn column) {
            return new Binding {
                Converter = CellConverterInstance,
                ConverterParameter = column.Index,
                Mode = BindingMode.OneWay
            };
        }

        public static bool ForceRaw;
        public static readonly System.Windows.Data.IValueConverter CellConverterInstance = new CellConverter();

        private class CellConverter : System.Windows.Data.IValueConverter {



            #region IValueConverter Members

            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
                var row = value as IRow;
                if (row == null)
                    return null;

                var i = System.Convert.ToInt32(parameter);

                if (ForceRaw || RawDataGrid.ColumnSetToRaw[i])
                    return row.GetRaw(i);

                try {
                    return row[i] ?? row.GetRaw(i);
                } catch {
                    // Stale definition vs. real data: show the raw value instead of crashing.
                    // The cell is flagged red via ErrorBrushConverter.
                    return row.GetRaw(i);
                }
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
                throw new NotImplementedException();
            }

            #endregion
        }
    }
}
