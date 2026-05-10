using Godbert.ViewModels;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Godbert {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        protected override void OnStartup(StartupEventArgs e) {
            Application.Current.DispatcherUnhandledException += new DispatcherUnhandledExceptionEventHandler(AppDispatcherUnhandledException);

            SaintCoinach.Graphics.Viewer.Interop.HavokInterop.InitializeMTA();

            base.OnStartup(e);

            this.Exit += App_Exit;
        }

        private void App_Exit(object sender, ExitEventArgs e) {
            Settings.Default.Save();
        }

        

        #region ExceptionHandler
        void AppDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
            // Log the exception, display a user-friendly message, etc.
            //MessageBox.Show($"An application error occurred:\n\n{e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Window mainWindowRaw = Application.Current.MainWindow;

            if (mainWindowRaw != null) { 
                MainWindow mainWindow = mainWindowRaw as MainWindow;
                MainViewModel vm = mainWindow.DataContext as MainViewModel;
                vm.LogToView(e.Exception.Message);
                vm.LogToView(e.Exception.StackTrace);
            }

            // Mark the exception as handled to prevent the application from terminating
            e.Handled = true;

            // In debug mode, you might want to leave e.Handled = false so Visual Studio can break at the exception source
            #if DEBUG
            e.Handled = false;
            #endif
        }
        #endregion
    }
}
