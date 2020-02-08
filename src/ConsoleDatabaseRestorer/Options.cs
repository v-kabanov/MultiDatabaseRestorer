using System.Configuration;
using System.Linq;
using CommandLine;
using CommandLine.Text;

namespace ConsoleDatabaseRestorer
{
    class Options
    {
        private string _backupFolder;
        private string[] _splitDatabases;
        private string _targetServer;

        [Option('f', "BackupFolder", HelpText = "Full path to the root backup folder", Required = false)]
        public string BackupFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(_backupFolder))
                {
                    return _backupFolder;
                }
                else
                {
                    return ConfigurationManager.AppSettings.Get("BackupFolder");
                }
            }
            set => _backupFolder = value;
        }

        [Option("DoNotRequireAllSplitDatabaseRestoration", Default = false, HelpText = "Valid backups to restore for every existing split database will not be required for restoration to start")]
        public bool DoNotRequireAllSplitDatabaseRestoration { get; set; }

        [Option("SplitDatabases", Default = new string[0], HelpText = "List of base database names split into series with numeric suffix", Required = false)]
        public string[] SplitDatabases
        {
            get
            {
                if (_splitDatabases != null)
                {
                    return _splitDatabases;
                }
                else
                {
                    return GetSplitDatabaseNamesFromAppConfig();
                }
            }
            set => _splitDatabases = value;
        }

        [Option('s', "TargetServer", HelpText = "Name of target SQL Server instance", Required = false)]
        public string TargetServer
        {
            get
            {
                if (!string.IsNullOrEmpty(_targetServer))
                {
                    return _targetServer;
                }
                else
                {
                    return ConfigurationManager.AppSettings.Get("TargetServer");
                }
            }
            set => _targetServer = value;
        }

        [Option(nameof(RebindUsersWithSqlLogins), Default = false, HelpText = "Rebind orphaned users with SQL logins by matching name; useful when restoring on another server.")]
        public bool RebindUsersWithSqlLogins { get; set; }

        private string[] GetSplitDatabaseNamesFromAppConfig()
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

        /// <summary>
        ///     This is deprecated and unused
        /// </summary>
        private bool RequireAllSplitDatabaseRestorationFromAppConfig
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
    }
}