using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CommandLine;
using CommandLine.Text;
using RestoreBackupLib;
using log4net;

namespace ConsoleDatabaseRestorer
{
    class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        static void Main(string[] args)
        {
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

            var namingConvention = BackupDirectoryNamingConvention.CreateDefaultMonthDayDayConvention();
            var success = false;

            try
            {
                var restorer = new MultiRestorer(options.BackupFolder, null, null, options.TargetServer, namingConvention);
                restorer.RequireAllSplitDatabaseRestore = !options.DoNotRequireAllSplitDatabaseRestoration;
                restorer.SplitDatabaseBaseNames = options.SplitDatabases;

                restorer.Prepare();
                success = restorer.Restore(options.RebindUsersWithSqlLogins);
            }
            catch (Exception e)
            {
                Log.ErrorFormat("Exception: {0}", e.Message);
                Log.Debug(e);
            }

            Log.InfoFormat("Finish, success = {0}", success);
        }

        static void HandleArgumentsErrors(IEnumerable<Error> errors)
        {

        }
    }
}
