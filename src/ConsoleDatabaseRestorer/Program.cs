using System;
using System.IO;
using System.Reflection;
using CommandLine;
using RestoreBackupLib;
using log4net;

namespace ConsoleDatabaseRestorer
{
    class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        static void Main(string[] args)
        {
            var logConfigPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().ManifestModule.FullyQualifiedName), "log4net.config");
            var logConfigFileInfo = new FileInfo(logConfigPath);

            if (logConfigFileInfo.Exists)
                log4net.Config.XmlConfigurator.ConfigureAndWatch(logConfigFileInfo);
            else
                log4net.Config.XmlConfigurator.Configure();

            CommandLine.Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Execute);
        }

        private static void Execute(Options options)
        {
            Log.InfoFormat("Split databases: {0}", string.Join(",", options.SplitDatabases));
            Log.InfoFormat("Backup folder: {0}", options.BackupFolder);
            Log.InfoFormat("Target server: {0}", options.TargetServer);
            Log.InfoFormat("Don't require all split db restoration: {0}", options.DoNotRequireAllSplitDatabaseRestoration);
            Log.InfoFormat("RebindUsersWithSqlLogins: {0}", options.RebindUsersWithSqlLogins);
            Log.InfoFormat("MaxExpectedLastBackupAgeHours: {0}", options.MaxExpectedLastBackupAgeHours);

            var namingConvention = BackupDirectoryNamingConvention.CreateDefaultMonthDayDayConvention();
            RestorationSummary result;

            try
            {
                var restorer = new MultiRestorer(options.BackupFolder, null, null, options.TargetServer, namingConvention);
                restorer.RequireAllSplitDatabaseRestore = !options.DoNotRequireAllSplitDatabaseRestoration;
                restorer.SplitDatabaseBaseNames = options.SplitDatabases;

                restorer.Prepare();
                result = restorer.Restore(options.RebindUsersWithSqlLogins);

                if (options.MaxExpectedLastBackupAgeHours > 0)
                    foreach (var dbSummary in result.DatabaseSummaries)
                    {
                        if (dbSummary.Status == Status.Success
                            && dbSummary.LastRestoredBackup != null
                            && (DateTime.UtcNow - dbSummary.LastRestoredBackup.BackupStartTime.ToUniversalTime()).TotalHours > options.MaxExpectedLastBackupAgeHours)
                        {
                            dbSummary.Status = Status.Warning;
                            dbSummary.Message = $"Last backup time is older than the expected maximum of {options.MaxExpectedLastBackupAgeHours} hours";
                        }
                    }

            }
            catch (Exception e)
            {
                Log.ErrorFormat("Exception: {0}", e.Message);
                Log.Debug(e);
                result = (e as RestorationException)?.RestorationSummary;

                if (result == null)
                    result = new RestorationSummary() {Status = Status.Error, StatusMessage = e.GetBaseException().Message};
            }

            result.LogSummary();

            Log.InfoFormat("Finish, status = {0}", result.MaxSeverity);
        }
    }
}
