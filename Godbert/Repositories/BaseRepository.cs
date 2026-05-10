using Godbert.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Godbert.Repositories {
    public abstract class BaseRepository<T> {

        public MainViewModel Parent { get; private set; }

        protected bool _IsReady = false;
        public bool IsReady { get { return _IsReady; } }

        protected BaseRepository(MainViewModel parent) {
            Parent = parent;
        }

        public void Initialize() {
            BackgroundWorker worker = new() {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            worker.DoWork += StartScan;

            worker.ProgressChanged += (sender, e) => {
                string status = e.UserState as string;
                Parent.SetStatusLoading(status, e.ProgressPercentage);
            };

            worker.RunWorkerCompleted += (sender, e) => {
                Parent.SetStatusReady();
                OnScanComplete(sender as BackgroundWorker, e);
            };

            worker.RunWorkerAsync();
        }
        public abstract void StartScan(object sender, DoWorkEventArgs e);
        public abstract void OnScanComplete(BackgroundWorker worker, RunWorkerCompletedEventArgs e);
        public abstract IEnumerable<T> GetAvailableEntries();
        public abstract IEnumerable<T> GetFilteredEntries(string query);
    }
}
