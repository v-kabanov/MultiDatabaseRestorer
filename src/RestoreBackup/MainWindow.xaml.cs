// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2014-11-14
// Comment		
// **********************************************************************************************/

using System;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using RestoreBackupLib;
using log4net;

namespace RestoreBackup
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly BackgroundWorker _worker = new BackgroundWorker();

        private FolderBrowserDialog _browseBackupDirDialog = new FolderBrowserDialog()
            {
                Description = "Select root backup folder",
                ShowNewFolderButton = false
            };

        private string BackupFolderPath
        {
            get => backupFolderTextBox.Text;
            set => backupFolderTextBox.Text = value;
        }

        public DateTime? PointInTime
        {
            get
            {
                if (rbPointInTime.IsChecked.HasValue && rbPointInTime.IsChecked.Value)
                {
                    return timePicker.Value;
                }
                else
                {
                    return null;
                }
            }
        }

        public bool IsWorkInProgress => _worker != null && _worker.IsBusy;

        public MainWindow()
        {
            InitializeComponent();

            timePicker.Value = DateTime.Now.Date;
            BackupFolderPath = System.Configuration.ConfigurationManager.AppSettings["DefaultBackupDirectory"];

            _worker.DoWork += WorkerOnDoWork;
            _worker.RunWorkerCompleted += WorkerOnRunWorkerCompleted;
            _worker.ProgressChanged += WorkerOnProgressChanged;
            _worker.WorkerReportsProgress = true;
            _worker.WorkerSupportsCancellation = true;

            progressBar.Visibility = Visibility.Hidden;

            var traceListener = new MyTraceListener(this.tbTrace);
            Trace.Listeners.Add(traceListener);
        }

        private void BtnBrowseBackupDirectory_OnClick(object sender, RoutedEventArgs e)
        {
            var result = _browseBackupDirDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                BackupFolderPath = _browseBackupDirDialog.SelectedPath;
            }
        }

        private void PointInTime_OnChecked(object sender, RoutedEventArgs e)
        {
            timePicker.IsEnabled = true;
        }

        private void PointInTime_OnUnchecked(object sender, RoutedEventArgs e)
        {
            timePicker.IsEnabled = false;
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            if (_worker.IsBusy)
            {
                if (System.Windows.MessageBox.Show("Cancel restoration?", "Please confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    if (!_worker.CancellationPending)
                    {
                        _worker.CancelAsync();
                        System.Windows.MessageBox.Show("Cancellation pending, please wait for confirmation");
                        return;
                    }
                }
                else
                {
                    return;
                }
            }
            _worker.Dispose();
            Close();
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            /*
            var localSystemSid = new SecurityIdentifier(System.Security.Principal.WellKnownSidType.NetworkServiceSid, null); //LocalSystemSid
            var refef = localSystemSid.Translate(typeof(System.Security.Principal.NTAccount));
            var accountNameComponents = refef.Value.Split('\\');

            var restorer = new MultiRestorer(BackupFolderPath, PointInTime);

            using (SimpleImpersonation.Impersonation.LogonUser(accountNameComponents[0],  accountNameComponents[1], string.Empty, LogonType.Service))
            {
                restorer.Prepare();
            }
            */

            tbTrace.Clear();

            var server = ConfigurationManager.AppSettings.Get("TargetServer");
            if (string.IsNullOrEmpty(server))
            {
                server = "localhost";
            }

            var namingConvention = BackupDirectoryNamingConvention.CreateDefaultMonthDayDayConvention();

            var restorer = new MultiRestorer(BackupFolderPath, PointInTime, _worker, server, namingConvention);
            restorer.RequireAllSplitDatabaseRestore = RequireAllSplitDatabaseRestoration;
            restorer.SplitDatabaseBaseNames = GetSplitDatabaseNames();

            btnRestore.IsEnabled = false;
            progressBar.Value = 0;
            _worker.RunWorkerAsync(restorer);

            progressBar.Visibility = Visibility.Visible;
        }

        private string[] GetSplitDatabaseNames()
        {
            var setting = ConfigurationManager.AppSettings.Get("SplitDatabases");

            string[] result;
            if (!string.IsNullOrEmpty(setting))
            {
                result = setting.Split(',').Where(s => !string.IsNullOrEmpty(s)).ToArray();
            }
            else
            {
                result = new string[] { };
            }

            return result;
        }

        private bool RequireAllSplitDatabaseRestoration
        {
            get
            {
                var result = true;
                var val = ConfigurationManager.AppSettings.Get("RequireAllSplitDatabaseRestoration");
                
                if (!string.IsNullOrWhiteSpace(val))
                {
                    bool.TryParse(val, out result);
                }

                return result;
            }
        }

        private void WorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs runWorkerCompletedEventArgs)
        {
            btnRestore.IsEnabled = true;
            MessageBoxImage image;
            string message;
            if (runWorkerCompletedEventArgs.Error != null)
            {
                message = runWorkerCompletedEventArgs.Error.Message;
                image = MessageBoxImage.Error;
            }
            else
            {
                if (runWorkerCompletedEventArgs.Cancelled)
                {
                    message = "Restoration cancelled";
                    image = MessageBoxImage.Warning;
                }
                else
                {
                    message = "Restoration completed successfully";
                    image = MessageBoxImage.None;
                }
            }
            //runWorkerCompletedEventArgs.Error
            System.Windows.MessageBox.Show(message, "Finish", MessageBoxButton.OK, image) ;

            progressBar.Visibility = Visibility.Hidden;
        }

        private void WorkerOnDoWork(object sender, DoWorkEventArgs doWorkEventArgs)
        {
            Log.Debug("Entering WorkerOnDoWork");
            var restorer = (MultiRestorer)doWorkEventArgs.Argument;
            var worker = (BackgroundWorker)sender;

            Log.Debug("Calling restorer.Prepare");
            restorer.Prepare();
            doWorkEventArgs.Result = restorer.Restore();
        }

        private void WorkerOnProgressChanged(object sender, ProgressChangedEventArgs progressChangedEventArgs)
        {
            progressBar.Value = progressChangedEventArgs.ProgressPercentage;
        }
    }
}
