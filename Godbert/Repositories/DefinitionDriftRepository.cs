using Godbert.Models;
using Godbert.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Godbert.Repositories {
    public class DefinitionDriftRepository : BaseRepository<string> {
        private readonly ConcurrentDictionary<string, DriftReport> _Reports = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> DialogOnlySheetsToIgnorePrefix = new List<string>() { 
            "quest/",  "custom/", "cut_scene/", "dungeon/", "content/", "raid/", "opening/", "warp/", "transport/", "leve/"
        };

        public DefinitionDriftRepository(MainViewModel parent) : base(parent) { }

        public bool TryGet(string sheetName, out DriftReport report) {
            return _Reports.TryGetValue(sheetName, out report);
        }

        public IEnumerable<DriftReport> AllReports => _Reports.Values;

        public IEnumerable<DriftReport> DriftedReports =>
            _Reports.Values.Where(r => r.HasDrift || r.MissingDefinition);

        public override IEnumerable<string> GetAvailableEntries() {
            return _Reports.Keys;
        }

        public override IEnumerable<string> GetFilteredEntries(string query) {
            if (string.IsNullOrWhiteSpace(query))
                return _Reports.Keys;
            return _Reports.Keys.Where(n => n.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public override void StartScan(object sender, DoWorkEventArgs e) {
            var worker = sender as BackgroundWorker;
            if (Parent.Realm == null)
                return;

            ScanAll(worker);
        }

        public override void OnScanComplete(BackgroundWorker worker, RunWorkerCompletedEventArgs e) {
            _IsReady = true;
            Parent.Definition?.OnDriftScanComplete();
        }

        private void ScanAll(BackgroundWorker worker) {
            _Reports.Clear();

            var realm = Parent.Realm;
            var defContainer = realm.GameData.Definition;
            var sheetNames = realm.GameData.AvailableSheets.ToList();
            var total = sheetNames.Count;
            var i = 0;

            foreach (var name in sheetNames) {
                if (DialogOnlySheetsToIgnorePrefix.Any(s => name.StartsWith(s))) {
                    continue;
                }

                if (worker != null && worker.CancellationPending)
                    return;

                try {
                    var header = realm.GameData.GetHeader(name);
                    var rawCount = header == null ? 0 : header.Columns.Count();

                    SaintCoinach.Ex.Relational.Definition.SheetDefinition def = null;
                    var hasDef = defContainer != null && defContainer.TryGetSheet(name, out def);

                    var definedMax = 0;
                    if (hasDef && def.DataDefinitions != null && def.DataDefinitions.Count > 0)
                        definedMax = def.DataDefinitions.Max(d => d.Index + d.Length);

                    // Startup scan is header-only (fast). The cast test (which must read a
                    // data row per sheet) is deferred — run lazily when a sheet is opened, or
                    // on demand via CastTestAll — so startup doesn't stall on EXD I/O for ~800 sheets.
                    var report = new DriftReport {
                        SheetName = name,
                        RawColumns = rawCount,
                        DefinedMax = definedMax,
                        HasDefinition = hasDef
                    };

                    _Reports[name] = report;
                }
                catch (Exception ex) {
                    Parent.LogToView($"Drift scan failed for {name}: {ex.Message}");
                }

                i++;
                if (i % 25 == 0 || i == total) {
                    var pct = total == 0 ? 0 : (int)(i * 100L / total);
                    worker?.ReportProgress(pct, $"Scanning definitions… {i}/{total}");
                }
            }
        }

        /// <summary>
        /// Cast-test a single sheet (lazy, on open). Reads one data row and checks each defined
        /// column through the row indexer; InvalidCastException = stale definition. Cheap for one
        /// sheet. Returns true if the report changed (so the caller can refresh the badge).
        /// </summary>
        public bool CastTestSheet(string name) {
            if (!_Reports.TryGetValue(name, out var report) || !report.HasDefinition)
                return false;
            if (report.CastTested)
                return false;

            var realm = Parent.Realm;
            if (realm?.GameData?.Definition == null)
                return false;
            if (!realm.GameData.Definition.TryGetSheet(name, out var def))
                return false;

            var failing = new List<int>();
            try {
                var header = realm.GameData.GetHeader(name);
                var rawCount = header == null ? 0 : header.Columns.Count();
                RunCastTest(realm, name, def, rawCount, failing);
            } catch (Exception ex) {
                Parent.LogToView($"Cast test failed for {name}: {ex.Message}");
            }

            report.CastFailingColumns = failing;
            report.CastTested = true;
            return true;
        }

        /// <summary>Cast-test every defined sheet (on-demand sweep), invoking <paramref name="onSheetTested"/>
        /// after each so the UI can refresh that badge. Intended to run on a background thread.</summary>
        public void CastTestAll(Action<DriftReport> onSheetTested, BackgroundWorker worker = null) {
            var names = _Reports.Keys.ToList();
            var total = names.Count;
            var i = 0;
            foreach (var name in names) {
                if (worker != null && worker.CancellationPending) return;
                if (CastTestSheet(name) && _Reports.TryGetValue(name, out var report))
                    onSheetTested?.Invoke(report);
                i++;
                worker?.ReportProgress(total == 0 ? 0 : (int)(i * 100L / total), $"Cast testing… {i}/{total}");
            }
        }

        /// <summary>
        /// Cast failures are structural, not per-row: a column's raw .NET type is fixed by
        /// the EXH datatype, so a converter that throws InvalidCastException throws for every
        /// row. Sampling the FIRST row per defined column is enough to flag a stale definition.
        /// </summary>
        private void RunCastTest(SaintCoinach.ARealmReversed realm, string name,
                                 SaintCoinach.Ex.Relational.Definition.SheetDefinition def,
                                 int rawCount, List<int> failing) {
            SaintCoinach.Ex.IRow firstRow;
            try {
                var sheet = realm.GameData.GetSheet(name);
                // Non-generic enumeration so Variant-2 sheets yield a readable sub-row, not a
                // parent row whose indexer throws.
                firstRow = sheet == null ? null
                    : Godbert.SheetRows.AsRows((System.Collections.IEnumerable)sheet).FirstOrDefault();
            } catch {
                return; // sheet has no rows / failed to load — nothing to cast-test
            }
            if (firstRow == null)
                return;

            foreach (var dd in def.DataDefinitions) {
                for (var k = 0; k < dd.Length; k++) {
                    var idx = dd.Index + k;
                    if (idx >= rawCount)
                        continue; // definition points past the actual columns (drift) — not a cast failure

                    try {
                        // Read through the row's own indexer — the exact path the app uses,
                        // applying the live definition's converter to real data.
                        var _ = firstRow[idx];
                    } catch (InvalidCastException) {
                        failing.Add(idx);
                    } catch {
                        // Non-cast errors (e.g. a link target sheet missing) are not cast failures;
                        // ignore them here so we only flag genuine type mismatches.
                    }
                }
            }
        }
    }
}
