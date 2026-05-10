using Godbert.Models;
using Godbert.ViewModels;
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
        }

        private Dictionary<string, Dictionary<string, IEnumerable<ScannedIcon>>> _iconSetsByPatch = new();
        private Dictionary<string, IEnumerable<ScannedIcon>> _iconsByImageSetName => _iconSetsByPatch[DefaultSetName];

        private Dictionary<string, string> _iconSetNickNames = new();

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
        }

        internal void ScanIcons(BackgroundWorker worker) {
            int min = 0;
            int max = 999999;
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
    }
}
