using Godbert.Models;
using Godbert.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SaintCoinach;
using SaintCoinach.Ex;
using SaintCoinach.Imaging;
using SaintCoinach.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Godbert.Repositories {
    public class IconRepository : BaseRepository<string> {
        const string UiImagePathFormat = "ui/icon/{0:D3}000{1}/{2:D6}.tex";
        const string DefaultSetName = "All";

        private ARealmReversed _Realm { get => Parent.Realm; }

        public IconRepository(MainViewModel parent) : base(parent) {
            _iconSetsByPatch[DefaultSetName] = new();
            LoadRemarks();
        }

        private Dictionary<string, Dictionary<string, IEnumerable<ScannedIcon>>> _iconSetsByPatch = new();
        private Dictionary<string, IEnumerable<ScannedIcon>> _iconsByImageSetName => _iconSetsByPatch[DefaultSetName];

        // nickname (label) → set id, derived from remarks so the set list is searchable by label.
        private Dictionary<string, string> _iconSetNickNames = new();

        // Shared icon resources (synced via the definition server).
        private readonly DefinitionSyncClient _sync = new();
        private Dictionary<string, (string label, string note)> _remarks = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _syncedVersionBaseline = new();

        private string ClientTypeName => Enum.GetName<ClientType>(Parent.ClientType);
        private static string RemarksPath => DefinitionSyncClient.LocalPath("IconRemarks.json");
        private string IconVersionsPath => DefinitionSyncClient.LocalPath($"IconVersions/{ClientTypeName}.json");

        public override IEnumerable<string> GetAvailableEntries() {
            return GetAvailableEntries(DefaultSetName);
        }
        public IEnumerable<string> GetAvailableEntries(string patch) {
            return _iconSetsByPatch[patch].Keys;
        }

        public IEnumerable<string> GetPatches() {
            return _iconSetsByPatch.Keys; 
        }

        public IEnumerable<ScannedIcon> GetIcons(string iconSet, string patch) {
            if (string.IsNullOrEmpty(patch)) {
                patch = DefaultSetName;
            }
            var iconSets = _iconSetsByPatch[patch];

            if (!string.IsNullOrWhiteSpace(iconSet)) {
                if (iconSets.TryGetValue(iconSet, out var set)) {
                    return set;
                }
            }
            return new List<ScannedIcon>();
        }

        public IEnumerable<ScannedIcon> GetIcons(string iconSet) {
            return GetIcons(iconSet, DefaultSetName);
        }

        public override IEnumerable<string> GetFilteredEntries(string query) {
            return GetFilteredEntries(query, DefaultSetName);
        }

        public IEnumerable<string> GetFilteredEntries(string query, string patch) {
            if (string.IsNullOrWhiteSpace(patch)) {
                patch = DefaultSetName;
            }
            var iconSets = _iconSetsByPatch[patch];
            
            if (string.IsNullOrWhiteSpace(query)) 
                return iconSets.Keys; 
            if (int.TryParse(query, out var iconIndex)) {
                return iconSets.Keys.Where(x => (int.Parse(x) / 1000).ToString().StartsWith(query));
            } else {
                return iconSets.Keys.Intersect(_iconSetNickNames.Where(p => p.Key.StartsWith(query)).Select(p => p.Value));
            }
        }

        public override void StartScan(object sender, DoWorkEventArgs e) {
            var worker = sender as BackgroundWorker;
            if (Parent.Realm == null) {
                return;
            }

            ScanIcons(worker);
        }

        public override void OnScanComplete(BackgroundWorker worker, RunWorkerCompletedEventArgs e) {
            Parent.Image.SelectedPatch = DefaultSetName;
            Parent.Image.Refresh();
            PatchDatabase.Save();
            AutoSubmitIconVersions();
        }

        internal void ScanIcons(BackgroundWorker worker) {
            int min = 0;
            int max = 999999;
            MergeSyncedIconVersionsFromDisk(); // pull-before-scan: consume the authoritative map first
            _iconSetsByPatch.Clear();
            _iconSetsByPatch[DefaultSetName] = new();

            for (int i = min; i <= max; i++) {
                if (i % 1000 == 0) {
                    worker.ReportProgress(i / 10000, $"Scanning Icon {i}.");
                    Parent.Image.Refresh();
                }
                try {
                    ScanIcon(i);
                }
                catch (Exception ex) {
                    Parent.LogToView($"Unexpected error happened when handling {i}, {ex.Message}");
                }
            }
        }
        private bool ScanIcon(int i) {
            var filePath = string.Format(UiImagePathFormat, i / 1000, "", i);

            ScannedIcon icon = null;
            if (_Realm.Packs.TryGetFile(filePath, out var file)) {
                if (file is ImageFile imgFile) {

                    var hqPath = string.Format(UiImagePathFormat, i / 1000, "/hq", i);

                    bool hasHqVariant = _Realm.Packs.TryGetFile(hqPath, out var hqFile);
                    icon = new ScannedIcon(this, i, imgFile, hasHQVariant: hasHqVariant);
                }
                else {
                    Parent.LogToView($"{filePath} is not an image.");
                }
            }
            else {
                var languageVariantPath = string.Format(UiImagePathFormat, i / 1000, "/" + Parent.ClientType.GetFirstLanguage().GetCode(), i);
                if (_Realm.Packs.TryGetFile(languageVariantPath, out file)) {
                    if (file is ImageFile imgFile) {
                        icon = new ScannedIcon(this, i, imgFile, isLanguageTyped: true);
                    } else {
                        Parent.LogToView($"{filePath} is not an image.");
                    }
                }
                
            }

            if (icon != null) {
                IndexIcon(i, icon);
                return true;
            }

            return false;
        }

        internal void ScanUld() {
            Pack uiPack = Parent.Realm.Packs.GetPack(new PackIdentifier("ui", "ffxiv", 0));
            IndexSource uiSource = uiPack.Source as IndexSource;
            SaintCoinach.IO.Directory dirUld = uiSource.GetDirectory("ui/uld");
            int count = dirUld.Count();
            int i = 1;
            Console.WriteLine($"{count} files in total for uld.");
            foreach (SaintCoinach.IO.File file in dirUld) {
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Processing {i}, {(float)i / (float)count}, {file.Path}\n");
                i++;
            }
        }

        public void IndexIcon(int i, ScannedIcon icon) {
            string patch = PatchDatabase.Get("icon", icon.ID.ToString(), Enum.GetName<ClientType>(Parent.ClientType), Parent.Realm.GameVersion);
            var setName = $"{i / 1000:D3}000";

            if (!_iconSetsByPatch.TryGetValue(patch, out var patchSet)) {
                patchSet = new Dictionary<string, IEnumerable<ScannedIcon>>();
                _iconSetsByPatch[patch] = patchSet;
            }

            if (!patchSet.TryGetValue(setName, out var patchedIconSet)) {
                patchedIconSet = new List<ScannedIcon>();
                patchSet[setName] = patchedIconSet;
            }

            if (!_iconsByImageSetName.TryGetValue(setName, out var iconSet)) {
                iconSet = new List<ScannedIcon>();
                _iconsByImageSetName[setName] = iconSet;
            }
            var iconList = iconSet as List<ScannedIcon>;
            iconList.Add(icon);

            var patchedIconList = patchedIconSet as List<ScannedIcon>;
            patchedIconList.Add(icon);
        }

        public static int GetIconId(ImageFile icon) {
            return int.Parse(System.IO.Path.GetFileNameWithoutExtension(icon.Path.Replace("_hr1", "")));
        }

        public string GetCurrentLanguageVariant() {
            return "/" + Parent.Realm.GameData.ActiveLanguageCode;
        }

        #region Shared resources (remarks + icon versions)

        /// <summary>
        /// Called (on the UI thread) by the launch sync when a shared icon file was downloaded.
        /// Remarks are reloaded immediately (separate from scan state). Downloaded icon-version maps
        /// are NOT merged live — the icon scan reads/writes the same PatchDatabase on a worker thread,
        /// so they are picked up at the start of the next scan instead (the history converges over
        /// launches regardless, since the server merges earliest-wins).
        /// </summary>
        public void OnSharedFilesUpdated(IEnumerable<string> relPaths) {
            foreach (var p in relPaths) {
                if (string.Equals(p, "IconRemarks.json", StringComparison.OrdinalIgnoreCase))
                    ReloadRemarks();
            }
        }

        public (string label, string note) GetRemark(string setId) =>
            _remarks.TryGetValue(setId ?? string.Empty, out var r) ? r : (string.Empty, string.Empty);

        public void ReloadRemarks() => LoadRemarks();

        private void LoadRemarks() {
            _remarks = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            _iconSetNickNames = new Dictionary<string, string>();
            try {
                if (!System.IO.File.Exists(RemarksPath)) return;
                var obj = JObject.Parse(System.IO.File.ReadAllText(RemarksPath));
                foreach (var p in obj.Properties()) {
                    var label = (string)p.Value["label"] ?? string.Empty;
                    var note = (string)p.Value["note"] ?? string.Empty;
                    _remarks[p.Name] = (label, note);
                    if (!string.IsNullOrWhiteSpace(label))
                        _iconSetNickNames[label] = p.Name;
                }
            } catch (Exception ex) {
                Parent.LogToView($"Failed to read icon remarks: {ex.Message}");
            }
        }

        /// <summary>Update a remark locally, persist the synced file, and return the full remarks JSON to submit.</summary>
        public string SetRemarkLocalAndSerialize(string setId, string label, string note) {
            _remarks[setId] = (label ?? string.Empty, note ?? string.Empty);
            _iconSetNickNames = new Dictionary<string, string>();
            foreach (var kv in _remarks)
                if (!string.IsNullOrWhiteSpace(kv.Value.label))
                    _iconSetNickNames[kv.Value.label] = kv.Key;

            var json = SerializeRemarks();
            try {
                System.IO.File.WriteAllText(RemarksPath, json, new UTF8Encoding(false));
            } catch (Exception ex) {
                Parent.LogToView($"Failed to save icon remarks: {ex.Message}");
            }
            return json;
        }

        // Mirrors the server's canonical form (sorted keys, label/note when non-empty, indented).
        private string SerializeRemarks() {
            var outObj = new JObject();
            foreach (var kv in _remarks.OrderBy(k => k.Key, StringComparer.Ordinal)) {
                var entry = new JObject();
                if (!string.IsNullOrEmpty(kv.Value.label)) entry["label"] = kv.Value.label;
                if (!string.IsNullOrEmpty(kv.Value.note)) entry["note"] = kv.Value.note;
                outObj[kv.Key] = entry;
            }
            return JsonConvert.SerializeObject(outObj, Formatting.Indented);
        }

        private void MergeSyncedIconVersionsFromDisk() {
            try {
                if (!System.IO.File.Exists(IconVersionsPath)) { _syncedVersionBaseline = new(); return; }
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText(IconVersionsPath))
                          ?? new Dictionary<string, string>();
                PatchDatabase.MergeSharedIconVersions(ClientTypeName, map);
                _syncedVersionBaseline = new Dictionary<string, string>(map);
            } catch (Exception ex) {
                Parent.LogToView($"Failed to read shared icon versions: {ex.Message}");
                _syncedVersionBaseline = new();
            }
        }

        /// <summary>After a scan, push any icon versions the server doesn't yet have (delta vs. the synced baseline).</summary>
        private async void AutoSubmitIconVersions() {
            if (!_sync.IsConfigured) return;
            if (string.IsNullOrWhiteSpace(Settings.Default.DefinitionServerUser)) return; // read-only user

            try {
                var ct = ClientTypeName;
                var current = PatchDatabase.GetIconVersionMap(ct);
                var delta = new Dictionary<string, string>();
                foreach (var kv in current)
                    if (!_syncedVersionBaseline.TryGetValue(kv.Key, out var b) ||
                        !string.Equals(b, kv.Value, StringComparison.Ordinal))
                        delta[kv.Key] = kv.Value;

                if (delta.Count == 0) return;

                var res = await _sync.SubmitAsync("iconversion", ct, JsonConvert.SerializeObject(delta));
                Parent.LogToView(res.Ok
                    ? $"Shared {delta.Count} icon version(s) for {ct}."
                    : $"Icon version share failed: {res.Error}");
            } catch (Exception ex) {
                Parent.LogToView($"Icon version share error: {ex.Message}");
            }
        }

        #endregion
    }
}
