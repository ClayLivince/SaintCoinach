using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

using SaintCoinach.Ex;
using SaintCoinach.Ex.Relational;
using SaintCoinach.Ex.Relational.Definition;

namespace Godbert.ViewModels.Definition {
    public class DefinitionPreviewViewModel : ObservableBase {
        private readonly SheetEditorViewModel _Editor;
        private SheetDefinition _EditedDefinition;
        private IList<IRow> _Rows;
        private int? _SelectedColumnIndex;
        private bool _IsLoading;
        private bool _Loaded;

        public IRelationalSheet Sheet => _Editor.LiveSheet;

        public SheetDefinition EditedDefinition {
            get => _EditedDefinition;
            private set { _EditedDefinition = value; OnPropertyChanged(nameof(EditedDefinition)); }
        }

        public IList<IRow> Rows {
            get => _Rows;
            private set { _Rows = value; OnPropertyChanged(nameof(Rows)); }
        }

        public bool IsLoading {
            get => _IsLoading;
            private set { if (_IsLoading != value) { _IsLoading = value; OnPropertyChanged(nameof(IsLoading)); } }
        }

        public int? SelectedColumnIndex {
            get => _SelectedColumnIndex;
            set { if (_SelectedColumnIndex != value) { _SelectedColumnIndex = value; OnPropertyChanged(nameof(SelectedColumnIndex)); OnPropertyChanged(nameof(HasSelectedColumn)); } }
        }

        public bool HasSelectedColumn => _SelectedColumnIndex.HasValue;

        public DefinitionPreviewViewModel(SheetEditorViewModel editor) {
            _Editor = editor;
        }

        /// <summary>
        /// Materialize the whole sheet (all rows, no cap) on a background thread the first
        /// time the editor is shown, then publish to the grid on the UI thread. WPF DataGrid
        /// UI-virtualization means only visible cells run converters, so the full row list is
        /// cheap to hold; the cost we move off the UI thread is the one-time EXD page read
        /// (DataSheet.CreateAllPartialSheets) + row-wrapper allocation.
        /// </summary>
        public void EnsureLoaded() {
            if (_Loaded) return;
            _Loaded = true;

            // Build + compile the edited definition on the UI thread (cheap, no I/O).
            EditedDefinition = BuildCompiledDefinition();

            var sheet = _Editor.LiveSheet;
            if (sheet == null) { Rows = null; return; }

            IsLoading = true;
            var dispatcher = Dispatcher.CurrentDispatcher;
            Task.Run(() => {
                List<IRow> rows;
                try {
                    // Non-generic enumeration so Variant-2 sheets yield sub-rows, not parents.
                    rows = Godbert.SheetRows.AsRows((System.Collections.IEnumerable)sheet).ToList();
                } catch {
                    rows = new List<IRow>();
                }
                dispatcher.BeginInvoke(new System.Action(() => {
                    Rows = rows;
                    IsLoading = false;
                }));
            });
        }

        /// <summary>
        /// Re-apply the current edits to the already-loaded rows (called after an edit or save).
        /// The sheet is warm by now, so this is synchronous and fast — it only rebuilds the
        /// edited definition; the grid re-converts visible cells.
        /// </summary>
        public void Refresh() {
            EditedDefinition = BuildCompiledDefinition();
        }

        private SheetDefinition BuildCompiledDefinition() {
            var sheet = _Editor.LiveSheet;
            if (sheet == null) return null;

            var def = _Editor.BuildSheetDefinition();
            foreach (var d in def.DataDefinitions)
                d.ResolveReferences(def);
            try {
                def.Compile();
            } catch {
                // Compile can throw if a complexlink references a name that isn't present
                // — preview should still work for the unaffected columns.
            }
            return def;
        }

        /// <summary>
        /// Resolve a cell's display value through the row's own indexer (<c>row[i]</c>) —
        /// the exact path the Data tab uses, so links resolve to names and Variant-2
        /// sub-rows read correctly. Cell VALUES reflect the live/saved definition; the
        /// editor's unsaved changes appear after Save (which recompiles the live sheet
        /// definition). Headers still show the edited names/types.
        ///
        /// Classification for traffic-light coloring:
        ///   • CastError (red)         — typed read threw InvalidCastException (stale def).
        ///   • UnresolvedLink (yellow) — a reference column returned null for a non-zero
        ///     key (relation didn't resolve; probably a new link to add).
        ///   • Raw / Ok                — raw value shown.
        /// Never bubbles an exception so the grid stays alive on stale definitions.
        /// </summary>
        public CellResult GetCell(IRow row, int columnIndex) {
            var raw = SafeRaw(row, columnIndex);

            object typed;
            try {
                typed = row[columnIndex];
            } catch (System.InvalidCastException ex) {
                return new CellResult { Value = raw, State = CellState.CastError, ErrorMessage = ex.Message };
            } catch (System.Exception) {
                return new CellResult { Value = raw, State = CellState.Raw };
            }

            if (typed == null) {
                if (IsUnresolvedReference(columnIndex, raw))
                    return new CellResult { Value = raw, State = CellState.UnresolvedLink };
                // Plain null / key-0 link → fall back to raw, matching `row[i] ?? row.GetRaw(i)`.
                return new CellResult { Value = raw, State = CellState.Ok };
            }

            return new CellResult { Value = typed, State = CellState.Ok };
        }

        /// <summary>
        /// True when a reference-type column (link/multiref/complexlink/generic/tomestone —
        /// all resolve to IRelationalRow) returned null for a non-zero raw key, i.e. the
        /// relation didn't resolve and likely needs a new/extended link.
        /// </summary>
        private bool IsUnresolvedReference(int columnIndex, object raw) {
            try {
                if (EditedDefinition.GetValueType(columnIndex) != typeof(IRelationalRow))
                    return false;
                if (raw == null) return false;
                var key = System.Convert.ToInt64(raw);
                return key != 0;
            } catch {
                return false;
            }
        }

        public object GetRaw(IRow row, int columnIndex) => SafeRaw(row, columnIndex);

        private static object SafeRaw(IRow row, int columnIndex) {
            try { return row.GetRaw(columnIndex); }
            catch { return null; }
        }
    }

    public enum CellState { Ok, Raw, UnresolvedLink, CastError }

    public struct CellResult {
        public object Value;
        public CellState State;
        public string ErrorMessage;

        public bool IsError => State == CellState.CastError;
        public bool IsRaw => State == CellState.Raw;

        public string Display {
            get {
                var s = Value?.ToString() ?? "(null)";
                if (State == CellState.CastError) return "⚠ " + s;
                return s;
            }
        }
    }
}
