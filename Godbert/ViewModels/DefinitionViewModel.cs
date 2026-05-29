using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Godbert.Commands;
using Godbert.Models;
using Godbert.Repositories;
using Godbert.ViewModels.Definition;

using SaintCoinach;
using SaintCoinach.Ex.Relational;

namespace Godbert.ViewModels {
    public class DefinitionViewModel : ObservableBase {
        private readonly Dictionary<string, SheetEditorViewModel> _OpenEditors = new(StringComparer.OrdinalIgnoreCase);

        private string _SelectedSheetName;
        private SheetEditorViewModel _CurrentEditor;
        private string _FilterTerm;
        private bool _ShowOnlyDrifted;
        private DriftReport[] _AllReports = Array.Empty<DriftReport>();

        // Coordination for same-session apply of server-downloaded definitions. Downloads write to
        // disk immediately, but the in-memory definition is only mutated AFTER the drift scan
        // completes — the scan reads the same definition objects on a worker thread.
        private readonly object _ApplyLock = new();
        private volatile bool _DriftScanComplete;
        private List<string> _PendingDownloadedPaths;
        private string _PendingRemoteVersion;

        public MainViewModel Parent { get; }
        public ARealmReversed Realm => Parent.Realm;
        public DefinitionDriftRepository DriftRepository { get; }
        public DefinitionSyncClient SyncClient { get; } = new DefinitionSyncClient();

        public bool IsServerConfigured => SyncClient.IsConfigured;

        public DefinitionViewModel(MainViewModel parent) {
            Parent = parent;
            DriftRepository = new DefinitionDriftRepository(parent);
        }

        /// <summary>
        /// Fire-and-forget launch update check. No-op when no server is configured; on failure it
        /// logs and does nothing (never blocks startup). Downloaded files are applied to the live
        /// in-memory definition this session via <see cref="TryApplyPendingUpdates"/> — but only
        /// after the drift scan finishes, since both touch the same definition objects.
        /// </summary>
        public async void CheckForUpdatesOnLaunch() {
            if (!SyncClient.IsConfigured) return;
            try {
                var res = await SyncClient.CheckForUpdatesAsync();
                if (!res.Reachable) {
                    Parent.LogToView($"Definition server unreachable: {res.Error}");
                    return;
                }
                if (res.HasUpdates) {
                    var n = await SyncClient.DownloadChangedAsync(res.ChangedPaths);
                    Parent.LogToView($"Definition server v{res.RemoteVersion}: downloaded {n} updated file(s).");
                    lock (_ApplyLock) {
                        _PendingDownloadedPaths = res.ChangedPaths.ToList();
                        _PendingRemoteVersion = res.RemoteVersion;
                    }
                    TryApplyPendingUpdates();
                } else {
                    Parent.LogToView($"Definitions up to date (server v{res.RemoteVersion}).");
                }
            } catch (Exception ex) {
                Parent.LogToView($"Definition update check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply server-downloaded definition files to the live in-memory definition, then re-scan
        /// drift so badges reflect the new definitions. Runs only once BOTH the download has
        /// finished and the initial drift scan has completed (whichever is later), and always on
        /// the UI thread, so it never races the worker-thread scan.
        /// </summary>
        private void TryApplyPendingUpdates() {
            List<string> paths;
            string version;
            lock (_ApplyLock) {
                if (!_DriftScanComplete || _PendingDownloadedPaths == null || _PendingDownloadedPaths.Count == 0)
                    return;
                paths = _PendingDownloadedPaths;
                version = _PendingRemoteVersion;
                _PendingDownloadedPaths = null; // claim so only one caller applies
            }

            const string defPrefix = "Definitions/";

            void Apply() {
                var appliedDefs = 0;
                var anyNew = false;
                var reloadedSheets = new List<string>();
                var iconPaths = new List<string>();

                foreach (var path in paths) {
                    if (path.StartsWith(defPrefix, StringComparison.OrdinalIgnoreCase) &&
                        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                        var sheet = path.Substring(defPrefix.Length, path.Length - defPrefix.Length - ".json".Length);
                        try {
                            if (Realm.ReloadSheetDefinition(sheet, out var wasNew)) {
                                appliedDefs++;
                                anyNew |= wasNew;
                                _OpenEditors.Remove(sheet); // stale editor tree
                                reloadedSheets.Add(sheet);
                            }
                        } catch (Exception ex) {
                            Parent.LogToView($"Failed to apply updated definition {sheet}: {ex.Message}");
                        }
                    } else {
                        iconPaths.Add(path); // IconRemarks.json, IconVersions/*.json
                    }
                }

                if (anyNew)
                    Realm.GameData.Definition.Compile();

                if (appliedDefs > 0) {
                    Parent.LogToView($"Applied {appliedDefs} updated definition(s) from server v{version}.");
                    if (!string.IsNullOrWhiteSpace(_SelectedSheetName) &&
                        reloadedSheets.Contains(_SelectedSheetName, StringComparer.OrdinalIgnoreCase))
                        LoadEditor(_SelectedSheetName);
                }

                if (iconPaths.Count > 0) {
                    try { Parent.Image?.OnSharedFilesUpdated(iconPaths); }
                    catch (Exception ex) { Parent.LogToView($"Failed to apply icon resources: {ex.Message}"); }
                }

                if (appliedDefs > 0) {
                    _DriftScanComplete = false;    // a fresh scan is starting
                    DriftRepository.Initialize();  // refresh drift badges against the new definitions
                }
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        #region Sheet list

        public ObservableCollection<DriftReport> FilteredSheets { get; } = new();

        public string FilterTerm {
            get => _FilterTerm;
            set { if (_FilterTerm != value) { _FilterTerm = value; OnPropertyChanged(nameof(FilterTerm)); RebuildFilteredList(); } }
        }

        public bool ShowOnlyDrifted {
            get => _ShowOnlyDrifted;
            set { if (_ShowOnlyDrifted != value) { _ShowOnlyDrifted = value; OnPropertyChanged(nameof(ShowOnlyDrifted)); RebuildFilteredList(); } }
        }

        public string SelectedSheetName {
            get => _SelectedSheetName;
            set {
                if (_SelectedSheetName == value) return;
                _SelectedSheetName = value;
                OnPropertyChanged(nameof(SelectedSheetName));
                LoadEditor(value);
            }
        }

        public SheetEditorViewModel CurrentEditor {
            get => _CurrentEditor;
            private set { _CurrentEditor = value; OnPropertyChanged(nameof(CurrentEditor)); OnPropertyChanged(nameof(HasEditor)); }
        }

        public bool HasEditor => _CurrentEditor != null;

        private void LoadEditor(string sheetName) {
            if (string.IsNullOrWhiteSpace(sheetName)) {
                CurrentEditor = null;
                return;
            }

            if (_OpenEditors.TryGetValue(sheetName, out var existing)) {
                CurrentEditor = existing;
                existing.Preview.EnsureLoaded();
                return;
            }

            IRelationalSheet liveSheet;
            try {
                liveSheet = Realm.GameData.GetSheet(sheetName) as IRelationalSheet;
            } catch (Exception ex) {
                MessageBox.Show($"Failed to open sheet {sheetName}: {ex.Message}", "Open error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Realm.GameData.Definition.TryGetSheet(sheetName, out var sourceDef);
            var editor = new SheetEditorViewModel(this, sheetName, liveSheet, sourceDef);
            _OpenEditors[sheetName] = editor;
            CurrentEditor = editor;
            // Heavy row materialization happens off the UI thread inside EnsureLoaded.
            editor.Preview.EnsureLoaded();
            // Lazily cast-test just this sheet (cheap) and refresh its badge.
            CastTestSheetAsync(sheetName);
        }

        public void ReloadSheet(string sheetName) {
            _OpenEditors.Remove(sheetName);
            if (string.Equals(_SelectedSheetName, sheetName, StringComparison.OrdinalIgnoreCase))
                LoadEditor(sheetName);
        }

        internal void NotifySheetSaved(string sheetName) {
            // Update the drift report for this sheet since its DefinedMax may have changed.
            try {
                if (DriftRepository.TryGet(sheetName, out var report) &&
                    Realm.GameData.Definition.TryGetSheet(sheetName, out var def)) {
                    report.DefinedMax = def.DataDefinitions.Count == 0 ? 0 : def.DataDefinitions.Max(d => d.Index + d.Length);
                    report.HasDefinition = true;
                    report.CastTested = false;          // re-run cast test against the new definition
                    report.NotifyChanged();             // refresh just this badge (no list rebuild → keeps selection)
                    CastTestSheetAsync(sheetName);
                }
            } catch { }
        }

        /// <summary>Cast-test a single sheet off the UI thread, then refresh its badge.</summary>
        private void CastTestSheetAsync(string sheetName) {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            System.Threading.Tasks.Task.Run(() => {
                try {
                    if (DriftRepository.CastTestSheet(sheetName) &&
                        DriftRepository.TryGet(sheetName, out var report)) {
                        if (dispatcher != null)
                            dispatcher.BeginInvoke(new Action(report.NotifyChanged));
                        else
                            report.NotifyChanged();
                    }
                } catch { }
            });
        }

        public void OnDriftScanComplete() {
            _AllReports = DriftRepository.AllReports.OrderBy(r => r.SheetName, StringComparer.OrdinalIgnoreCase).ToArray();
            RebuildFilteredList();
            _DriftScanComplete = true;
            TryApplyPendingUpdates();
        }

        private void RebuildFilteredList() {
            var src = _AllReports.AsEnumerable();
            if (_ShowOnlyDrifted)
                src = src.Where(r => r.HasDrift || r.MissingDefinition || r.HasCastFailure);
            if (!string.IsNullOrWhiteSpace(_FilterTerm))
                src = src.Where(r => r.SheetName.IndexOf(_FilterTerm, StringComparison.OrdinalIgnoreCase) >= 0);

            FilteredSheets.Clear();
            foreach (var r in src)
                FilteredSheets.Add(r);
            OnPropertyChanged(nameof(FilteredSheets));
        }

        #endregion

        #region Top-level commands

        private ICommand _SaveAllCommand;
        private ICommand _RescanCommand;
        private ICommand _CastTestAllCommand;

        public ICommand SaveAllCommand => _SaveAllCommand ??= new DelegateCommand(OnSaveAll);
        public ICommand RescanCommand => _RescanCommand ??= new DelegateCommand(OnRescan);
        public ICommand CastTestAllCommand => _CastTestAllCommand ??= new DelegateCommand(OnCastTestAll);

        private void OnSaveAll() {
            var dirty = _OpenEditors.Values.Where(e => e.IsDirty).ToList();
            if (dirty.Count == 0) return;
            foreach (var e in dirty)
                e.SaveCommand.Execute(null);
        }

        private void OnRescan() {
            DriftRepository.Initialize();
        }

        /// <summary>
        /// On-demand full cast-test sweep (reads one row of every defined sheet). Runs off the
        /// UI thread and lights up red badges as failures are found.
        /// </summary>
        private bool _CastSweepRunning;
        private void OnCastTestAll() {
            if (_CastSweepRunning) return;
            _CastSweepRunning = true;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            Parent.SetStatusLoading("Cast testing all sheets…", -1);
            System.Threading.Tasks.Task.Run(() => {
                try {
                    DriftRepository.CastTestAll(report => {
                        if (report.HasCastFailure && dispatcher != null)
                            dispatcher.BeginInvoke(new Action(report.NotifyChanged));
                    });
                } finally {
                    _CastSweepRunning = false;
                    if (dispatcher != null)
                        dispatcher.BeginInvoke(new Action(() => Parent.SetStatusReady()));
                }
            });
        }

        #endregion
    }
}
