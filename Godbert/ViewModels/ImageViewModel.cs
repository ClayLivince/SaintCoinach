using Godbert.Commands;
using Godbert.Models;
using Godbert.Repositories;
using SaintCoinach.Ex.Relational;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;

namespace Godbert.ViewModels {
    public class ImageViewModel : ObservableBase
    {
        #region Fields
        private string _SelectedIconSet;
        private string _SelectedPatch;
        private ScannedIcon _SelectedIcon;
        private IList _SelectedIcons;
        private DelegateCommand<string> _ExportIconCommand;
        private DelegateCommand<string> _ExportIconSetCommand;
        private DelegateCommand<string> _AddRemarkCommand;

        public IconRepository IconRepository { get; private set; }
        public IEnumerable<ScannedIcon> Icons => IconRepository.GetIcons(_SelectedIconSet, _SelectedPatch);

        private string _FilterImageTerm;
        #endregion

        public ICommand ExportIconCommand { get { return _ExportIconCommand ?? (_ExportIconCommand = new DelegateCommand<string>(OnExportImage)); } }
        public ICommand ExportIconSetCommand { get { return _ExportIconSetCommand ?? (_ExportIconSetCommand = new DelegateCommand<string>(OnExportIconSet)); } }
        public ICommand AddRemarkCommand { get { return _AddRemarkCommand ?? (_AddRemarkCommand = new DelegateCommand<string>(OnAddRemark)); } }
        
        public SaintCoinach.ARealmReversed Realm { get; private set; }
        public MainViewModel Parent { get; private set; }

        public IEnumerable<IconSetItem> FilteredIconSets {
            get {
                return IconRepository.GetFilteredEntries(_FilterImageTerm, _SelectedPatch)
                    .Select(id => new IconSetItem(id, IconRepository.GetRemark(id).label));
            }
        }
        public IEnumerable<string> Patches => IconRepository.GetPatches();
        public string SelectedIconSet {
            get { return _SelectedIconSet; }
            set {
                _SelectedIconSet = value;
                OnPropertyChanged(() => SelectedIconSet);
                OnPropertyChanged(() => Icons);

                Settings.Default.SelectedIconSet = value;
            }
        }

        public string SelectedPatch {
            get { return _SelectedPatch; }
            set {
                _SelectedPatch = value;
                OnPropertyChanged(() => SelectedPatch);
                OnPropertyChanged(() => FilteredIconSets);
                OnPropertyChanged(() => Icons);
            }
        }

        public ScannedIcon SelectedIcon {
            get => _SelectedIcon;
            set {
                _SelectedIcon = value;
                OnPropertyChanged(() => SelectedIcon);
            }
        }

        public IList SelectedIcons {
            get => _SelectedIcons;
            set {
                _SelectedIcons = value;
                OnPropertyChanged(() => SelectedIcons);
            }
        }

        public string FilterImageTerm {
            get { return _FilterImageTerm; }
            set {
                _FilterImageTerm = value;


                //if (string.IsNullOrWhiteSpace(value))
                //    _FilteredSheets = Realm.GameData.AvailableSheets;
                //else
                //    _FilteredSheets = Realm.GameData.AvailableSheets.Where(s => s.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();

                //OnPropertyChanged(() => FilterSheetTerm);
                //OnPropertyChanged(() => FilteredSheetNames);

                Settings.Default.FilterImageTerm = value;
            }
        }

        #region Constructor

        public ImageViewModel(SaintCoinach.ARealmReversed realm, MainViewModel parent) {
            Realm = realm;
            Parent = parent;

            IconRepository = new IconRepository(parent);
            IconRepository.Initialize();

            // TODO: Initialize the filter, or not.

        }
        
        #endregion

        public void Refresh() {
            OnPropertyChanged(() => FilteredIconSets);
            var set = _SelectedIconSet;
            _SelectedIconSet = null;
            OnPropertyChanged(() => Icons);
            _SelectedIconSet = set;
            OnPropertyChanged(() => Icons);
        }

        #region Export
        private void OnExportImage(string res) {
            if (SelectedIcon == null)
                return;
            if (_SelectedIcons != null && _SelectedIcons.Count > 1) {
                ExportIcons(_SelectedIcons.Cast<ScannedIcon>(), res);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog {
                DefaultExt = ".png",
                Filter = "PNG Files (*.png)|*.png",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = FixName(SelectedIcon.DisplayId) + res + ".png"
            };

            if (dlg.ShowDialog().GetValueOrDefault(false)) {
                SelectedIcon.Save(dlg.FileName, "", res);
            }
                
        }
        private void OnExportIconSet(string res) {
            if (string.IsNullOrEmpty(SelectedIconSet))
                return;

            ExportIcons(Icons, res);
        }

        private void ExportIcons(IEnumerable<ScannedIcon> icons, string res) {
            using FolderBrowserDialog folderDialog = new FolderBrowserDialog();
            folderDialog.Description = "Select a folder to save";
            folderDialog.ShowNewFolderButton = true; // Allow creating new folders
            folderDialog.RootFolder = Environment.SpecialFolder.Recent;

            DialogResult result = folderDialog.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath)) {
                Parent.SetStatusLoading("Exporting Icons...");
                foreach (ScannedIcon icon in icons) {
                    var target = new FileInfo(Path.Combine(folderDialog.SelectedPath, SelectedIconSet, $"{icon.DisplayId}{res}.png"));
                    if (!target.Directory.Exists)
                        target.Directory.Create();
                    icon.Save(target.FullName, "", res);
                    if (icon.HasHQVariant) {
                        var hqTarget = new FileInfo(Path.Combine(folderDialog.SelectedPath, SelectedIconSet, "hq", $"{icon.DisplayId}{res}.png"));
                        if (!target.Directory.Exists)
                            target.Directory.Create();
                        icon.Save(target.FullName, "/hq", res);
                    }
                }
                Parent.SetStatusReady();
            }
        }

        private static string FixName(string original) {
            var idx = original.LastIndexOf('/');
            if (idx >= 0)
                return original.Substring(idx + 1);
            return original;
        }
        #endregion

        #region Remarks

        private async void OnAddRemark(string _) {
            var setId = SelectedIconSet;
            if (string.IsNullOrWhiteSpace(setId)) {
                System.Windows.MessageBox.Show("Select an icon set first.", "Add remark",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var (label, note) = IconRepository.GetRemark(setId);
            var dlg = new Godbert.Views.RemarkDialog(setId, label, note) {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            if (dlg.ShowDialog() != true) return;

            var json = IconRepository.SetRemarkLocalAndSerialize(setId, dlg.RemarkLabel, dlg.RemarkNote);
            OnPropertyChanged(() => FilteredIconSets); // refresh nickname search

            var sync = new DefinitionSyncClient();
            if (!sync.IsConfigured) {
                Parent.LogToView($"Remark saved locally for set {setId} (no server configured).");
                return;
            }

            var res = await sync.SubmitAsync("remark", "IconRemarks", json);
            if (res.Ok && res.IsPending)
                System.Windows.MessageBox.Show($"Remark for set {setId} submitted for review.",
                    "Submitted for review", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            else if (res.Ok)
                System.Windows.MessageBox.Show($"Remark for set {setId} accepted (server v{res.NewVersion}).",
                    "Accepted", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            else
                System.Windows.MessageBox.Show($"Remark submit failed: {res.Error}",
                    "Submit failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }

        /// <summary>Apply downloaded shared icon files (remarks / versions) this session.</summary>
        public void OnSharedFilesUpdated(IEnumerable<string> relPaths) {
            IconRepository.OnSharedFilesUpdated(relPaths);
            OnPropertyChanged(() => FilteredIconSets);
            Refresh();
        }

        #endregion

    }
}
