using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows.Input;

namespace Godbert.ViewModels {
    using Commands;
    using Godbert.Repositories;
    using Newtonsoft.Json.Linq;
    using Ookii.Dialogs.Wpf;
    using SaintCoinach;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Threading;

    public class MainViewModel : ObservableBase {
        #region Properties
        public ARealmReversed Realm { get; private set; }
        public EngineHelper EngineHelper { get; private set; }
        public EquipmentViewModel Equipment { get; private set; }
        public FurnitureViewModel Furniture { get; private set; }
        public MonstersViewModel Monsters { get; private set; }
        public TerritoryViewModel Territories { get; private set; }
        public DemihumanViewModel Demihuman { get; private set; }
        public DataViewModel Data { get; private set; }

        public ImageViewModel Image { get; private set; }
        

        public bool IsEnglish { get { return Realm?.GameData.ActiveLanguage == SaintCoinach.Ex.Language.English; } }
        public bool IsJapanese { get { return Realm?.GameData.ActiveLanguage == SaintCoinach.Ex.Language.Japanese; } }
        public bool IsFrench { get { return Realm?.GameData.ActiveLanguage == SaintCoinach.Ex.Language.French; } }
        public bool IsGerman { get { return Realm?.GameData.ActiveLanguage == SaintCoinach.Ex.Language.German; } }
        public bool IsChineseSimplified { get { return Realm?.GameData.ActiveLanguage == SaintCoinach.Ex.Language.ChineseSimplified; } }
        public bool IsKorean { get { return Realm?.GameData.ActiveLanguage == SaintCoinach.Ex.Language.Korean; } }
        public bool IsChineseTraditional { get { return Realm?.GameData.ActiveLanguage == SaintCoinach.Ex.Language.TraditionalChinese; } }

        public bool IsIconHr1 { get { return Settings.Default.ShowIconHr1;  } }
        public bool IsIconHQ { get { return Settings.Default.ShowIconHQ; } }

        public bool SortByOffsets { get { return Settings.Default.SortByOffsets;} }
        public bool ShowOffsets { get { return Settings.Default.ShowOffsets; } }

        public string BuildVersion { get { return "bCL-251231"; } }

        public ClientType ClientType { get; private set; }

        public string GameVersion { get { return Realm is null ? "Game Path Not Provided." : Realm.GameVersion; } }
        public string DefinitionVersion { get { return Realm is null ?  "Game Path Not Provided." : Realm.DefinitionVersion; }  }
        public string GamePath { get { return String.IsNullOrWhiteSpace(Properties.Settings.Default.GamePath) ? "Please Select Your Game Path." : Properties.Settings.Default.GamePath; } }

        private string _logText = "";

        public string LogText {
            get => _logText;
            set {
                _logText = value; 
                OnPropertyChanged(nameof(LogText));
            }
        }

        private int _processProgress = 0;
        private bool _processProgressIndeterminate = false;
        private string _processStatus = "Ready";
        private Visibility _progressBarVisibility = Visibility.Hidden;

        public int ProcessProgress { get => _processProgress; }

        public bool ProcessProgressIndeterminate { get => _processProgressIndeterminate; }

        public string ProcessStatus {get => _processStatus;}

        public Visibility ProcessProgressBarVisibility {
            get => _progressBarVisibility;
        }


        
        #endregion

        #region Events
        public event EventHandler DataGridChanged;
        #endregion

        #region Constructor
        public MainViewModel() {
            if (!IsValidGamePath(Properties.Settings.Default.GamePath))
                return;

            Console.SetOut(new TextBoxWriter(this));
            Trace.Listeners.Add(new TextBoxTraceListener(this));
            InitializeRealm();
        }

        public MainViewModel(ARealmReversed realm) {
            Initialize(realm);
        }

        void InitializeRealm() {
            NotSupportedException lastException = null;

            foreach (ClientType clientType in Enum.GetValues(typeof(ClientType))) {
                try {
                    var realm = new ARealmReversed(Properties.Settings.Default.GamePath, clientType.GetFirstLanguage());
                    ClientType = clientType;
                    Initialize(realm);
                    lastException = null;
                    break;
                }
                catch (NotSupportedException e) {
                    lastException = e;
                    continue;
                }
            }

            if (lastException != null) {
                throw new AggregateException(new[] { lastException });
            }
        }

        void Initialize(ARealmReversed realm) {
            PatchDatabase.Load();
            realm.Packs.GetPack(new SaintCoinach.IO.PackIdentifier("exd", SaintCoinach.IO.PackIdentifier.DefaultExpansion, 0)).KeepInMemory = true;

            Realm = realm;
            EngineHelper = new EngineHelper();
            Equipment = new EquipmentViewModel(this);
            Furniture = new FurnitureViewModel(this);
            Monsters = new MonstersViewModel(this);
            Territories = new TerritoryViewModel(this);
            Demihuman = new DemihumanViewModel(this);
            Data = new DataViewModel(Realm, this);
            Image = new ImageViewModel(Realm, this);

            OnPropertyChanged(() => Realm);
            OnPropertyChanged(() => EngineHelper);
            OnPropertyChanged(() => Equipment);
            OnPropertyChanged(() => Furniture);
            OnPropertyChanged(() => Monsters);
            OnPropertyChanged(() => Territories);
            OnPropertyChanged(() => Demihuman);
            OnPropertyChanged(() => Data);
            OnPropertyChanged(() => Image);

            OnPropertyChanged(() => GamePath);
            OnPropertyChanged(() => GameVersion);
            OnPropertyChanged(() => DefinitionVersion);
            OnPropertyChanged(() => ClientType);
        }
        #endregion

        #region Commands
        private ICommand _LanguageCommand;
        private ICommand _GameLocationCommand;
        private ICommand _NewWindowCommand;
        private ICommand _ShowOffsetsCommand;
        private ICommand _SortByOffsetsCommand;
        private ICommand _IconResCommand;
        private ICommand _IconHQCommand;

        public ICommand LanguageCommand { get { return _LanguageCommand ?? (_LanguageCommand = new Commands.DelegateCommand<SaintCoinach.Ex.Language>(OnLanguage)); } }
        public ICommand GameLocationCommand { get { return _GameLocationCommand ?? (_GameLocationCommand = new Commands.DelegateCommand(OnGameLocation)); } }
        public ICommand NewWindowCommand { get { return _NewWindowCommand ?? (_NewWindowCommand = new Commands.DelegateCommand(OnNewWindowCommand)); } }
        public ICommand ShowOffsetsCommand { get { return _ShowOffsetsCommand ?? (_ShowOffsetsCommand = new Commands.DelegateCommand(OnShowOffsetsCommand)); } }
        public ICommand SortByOffsetsCommand { get { return _SortByOffsetsCommand ?? (_SortByOffsetsCommand= new Commands.DelegateCommand(OnSortByOffsetsCommand)); } }
        public ICommand IconResCommand { get { return _IconResCommand ?? (_IconResCommand = new Commands.DelegateCommand(OnIconRes)); } }
        public ICommand IconHQCommand { get { return _IconHQCommand ?? (_IconHQCommand = new Commands.DelegateCommand(OnIconHQ)); } }

        private void OnLanguage(SaintCoinach.Ex.Language newLanguage) {
            Realm.GameData.ActiveLanguage = newLanguage;

            OnPropertyChanged(() => IsEnglish);
            OnPropertyChanged(() => IsJapanese);
            OnPropertyChanged(() => IsGerman);
            OnPropertyChanged(() => IsFrench);
            OnPropertyChanged(() => IsChineseSimplified);
            OnPropertyChanged(() => IsKorean);
            OnPropertyChanged(() => IsChineseTraditional);

            Equipment.Refresh();
            Territories.Refresh();
            Image.Refresh();
        }

        private void OnGameLocation() {
            Properties.Settings.Default.GamePath = null;
            Properties.Settings.Default.Save();

            RequestGamePath();
            SetStatusLoading("Initiating...", -1);
            InitializeRealm();

            OnPropertyChanged(() => IsEnglish);
            OnPropertyChanged(() => IsJapanese);
            OnPropertyChanged(() => IsGerman);
            OnPropertyChanged(() => IsFrench);
            OnPropertyChanged(() => IsChineseSimplified);
            OnPropertyChanged(() => IsKorean);
            OnPropertyChanged(() => IsChineseTraditional);
            SetStatusReady();

        }

        private void OnIconRes() {
            Settings.Default.ShowIconHr1 = !Settings.Default.ShowIconHr1;
            Properties.Settings.Default.Save();
            Image.Refresh();

            OnPropertyChanged(() => IsIconHr1);
        }

        private void OnIconHQ() {
            Settings.Default.ShowIconHQ = !Settings.Default.ShowIconHQ;
            Properties.Settings.Default.Save();
            Image.Refresh();

            OnPropertyChanged(() => IsIconHQ);
        }

        private void OnClearGameLocation() {

        }

        private void OnNewWindowCommand() {
            Mouse.OverrideCursor = Cursors.Wait;
            try {
                var viewModel = new MainViewModel(Realm);
                var window = new MainWindow() { DataContext = viewModel };
                window.Show();
            }
            finally {
                Mouse.OverrideCursor = null;
            }
        }

        private void OnShowOffsetsCommand() {
            Settings.Default.ShowOffsets = !Settings.Default.ShowOffsets;

            OnPropertyChanged(() => ShowOffsets);
        }

        private void OnSortByOffsetsCommand() {
            Settings.Default.SortByOffsets = !Settings.Default.SortByOffsets;

            OnPropertyChanged(() => SortByOffsets);
        }
        #endregion

        #region GamePath
        private bool RequestGamePath() {
            string path = Godbert.Properties.Settings.Default.GamePath;
            if (!IsValidGamePath(path)) {
                string programDir;
                if (Environment.Is64BitProcess)
                    programDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                else
                    programDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                path = System.IO.Path.Combine(programDir, "SquareEnix", "FINAL FANTASY XIV - A Realm Reborn");

                if (IsValidGamePath(path)) {
                    var msgResult = System.Windows.MessageBox.Show(string.Format("Found game installation at \"{0}\". Is this correct?", path), "Confirm game installation", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
                    if (msgResult == MessageBoxResult.Yes) {
                        Godbert.Properties.Settings.Default.GamePath = path;
                        Godbert.Properties.Settings.Default.Save();

                        return true;
                    }

                    path = null;
                }
            }

            VistaFolderBrowserDialog dlg = null;

            while (!IsValidGamePath(path)) {
                var result = (dlg ?? (dlg = new VistaFolderBrowserDialog {
                    Description = "Please select the directory of your FFXIV:ARR game installation (should contain 'boot' and 'game' directories).",
                    ShowNewFolderButton = false,
                })).ShowDialog();

                if (!result.GetValueOrDefault(false)) {
                    var msgResult = System.Windows.MessageBox.Show("Cannot continue without a valid game installation, quit the program?", "That's no good", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);
                    if (msgResult == MessageBoxResult.Yes)
                        return false;
                }

                path = dlg.SelectedPath;
            }

            Godbert.Properties.Settings.Default.GamePath = path;
            Godbert.Properties.Settings.Default.Save();

            return true;
        }
        public static bool IsValidGamePath(string path) {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!Directory.Exists(path))
                return false;

            return File.Exists(Path.Combine(path, "game", "ffxivgame.ver"));
        }
        #endregion

        #region DynamicUIRelated
        public void LogToView(string message) {
            _logText += message + Environment.NewLine;
            OnPropertyChanged(nameof(LogText));
        }

        public void SetStatusLoading(string task, int progress = -1) {
            _processStatus = task;
            if (progress == -1) {
                _processProgressIndeterminate = true;
            }
            else {
                _processProgressIndeterminate = false;
                _processProgress = progress;
            }
            _progressBarVisibility = Visibility.Visible;

            OnPropertyChanged(nameof(ProcessStatus));
            OnPropertyChanged(nameof(ProcessProgress));
            OnPropertyChanged(nameof(ProcessProgressIndeterminate));
            OnPropertyChanged(nameof(ProcessProgressBarVisibility));
        }

        public void SetStatusReady() {
            _processStatus = "Ready";
            _progressBarVisibility = Visibility.Hidden;

            OnPropertyChanged(nameof(ProcessStatus));
            OnPropertyChanged(nameof(ProcessProgressBarVisibility));
        }

        public void SaveLogToFile() {
            // TODO: VistaFileDialog
        }
        #endregion

        #region Logging
        public class TextBoxWriter : TextWriter {
            private readonly MainViewModel _parent;

            public TextBoxWriter(MainViewModel parent) {
                _parent = parent;
            }

            public override void Write(char value) {
                _parent._logText += value;
            }

            public override void Write(string value) {
                _parent.LogToView(value);
            }

            // This is important for Console.WriteLine to function correctly with new lines
            public override Encoding Encoding => Encoding.ASCII;
        }
        public class TextBoxTraceListener : TraceListener {
            private readonly MainViewModel _parent;

            public TextBoxTraceListener(MainViewModel parent) {
                _parent = parent;
            }

            public override void Write(string message) {
                _parent.LogToView(message);
            }

            public override void WriteLine(string message) {
                _parent.LogToView(message);
            }
        }

        #endregion
    }
}
