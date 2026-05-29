using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using System.ComponentModel;
using SaintCoinach.Ex.Relational;
using SaintCoinach.Ex;

namespace Godbert.Controls {
    public class RawDataGridTextColumn : DataGridTextColumn, IRawDataColumn, INavigatable {
        private RelationalColumn _Column;
        private ContextMenu _ContextMenu;

        public RawDataGridTextColumn(RelationalColumn column) {
            _Column = column;

            _ContextMenu = new ContextMenu();
            var mi = new MenuItem {
                Header = "Copy",
            };
            mi.Click += OnCopyClick;
            _ContextMenu.Items.Add(mi);
        }
        void OnCopyClick(object sender, RoutedEventArgs e) {
            var fe = sender as FrameworkElement;
            if (fe == null)
                return;

            var ctx = fe.DataContext as IRelationalRow;
            if (ctx == null)
                return;

            var val = ctx[_Column.Index];
            if (val == null)
                return;

            System.Windows.Clipboard.SetDataObject(val.ToString());
        }

        public IRow OnNavigate(object sender, RoutedEventArgs e) {
            var fe = sender as FrameworkElement;
            if (fe == null)
                return null;

            var ctx = fe.DataContext as IRow;
            if (ctx == null)
                return null;

            return ctx[_Column.Index] as IRow;
        }
 
        protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem) {
            var ele = base.GenerateElement(cell, dataItem);
            ele.ContextMenu = _ContextMenu;
            return ele;
        }

        #region IRawDataColumn Members
        public RelationalColumn Column => _Column;

        public IComparer<object> GetComparer(ListSortDirection direction) {
            return new Comparer {
                Column = _Column,
                Direction = direction,
            };
        }


        private class Comparer : IComparer<object> {
            private NaturalComparer _NaturalComparer = new NaturalComparer(StringComparer.OrdinalIgnoreCase);
            public ListSortDirection Direction;
            public RelationalColumn Column;

            #region IComparer<object> Members

            public int Compare(object x, object y) {
                if (Direction == ListSortDirection.Descending)
                    return -InnerCompare(x, y);
                return InnerCompare(x, y);
            }

            private int InnerCompare(object x, object y) {
                var rx = x as IRow;
                var ry = y as IRow;
                if (rx == ry)
                    return 0;
                if (rx == null)
                    return -1;
                if (ry == null)
                    return 1;


                var vx = SafeRead(rx);
                var vy = SafeRead(ry);

                if (vx == vy)
                    return 0;
                if (vx == null)
                    return -1;
                if (vy == null)
                    return 1;

                var sx = vx.ToString();
                var sy = vy.ToString();

                return _NaturalComparer.Compare(sx, sy);
            }

            // Reading a typed value can throw InvalidCastException when the definition no
            // longer matches the raw data. Fall back to the raw value so sorting a drifted
            // column never crashes the app.
            private object SafeRead(IRow row) {
                if (ColumnFactory.ForceRaw || RawDataGrid.ColumnSetToRaw[Column.Index])
                    return row.GetRaw(Column.Index);
                try {
                    return row[Column.Index];
                } catch {
                    return row.GetRaw(Column.Index);
                }
            }

            #endregion
        }
        #endregion
    }
}
