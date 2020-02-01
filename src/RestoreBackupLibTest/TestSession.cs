// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-13
// Comment		
// **********************************************************************************************/

using System;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using log4net;
using log4net.Config;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace RestoreBackupLibTest
{
    [SetUpFixture]
    public class TestSession
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static DateTime StartDate { get; } = new DateTime(2015, 2, 1);

        public static int TestBckupPeriodMonths { get; } = 3;

        public static DirectoryInfo RootTestBackupDir { get; private set; }

        public static DirectoryInfo TestDbBackupDir { get; private set; }

        public static Server Server { get; private set; }

        [OneTimeSetUp]
        public static void StartSession()
        {
            var logConfig = new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "log4net.config"));
            XmlConfigurator.ConfigureAndWatch(logConfig);

            Log.Info("Starting test session");

            var bld = new SqlConnectionStringBuilder()
            {
                DataSource = ".",
                InitialCatalog = "master",
                IntegratedSecurity = true,
                ApplicationName = "bfsBackupRestoreTest"
            };

            var conn = new ServerConnection(new SqlConnection(bld.ToString()));
            Server = new Server(conn);
            Server.ConnectionContext.StatementTimeout = 0;

            RootTestBackupDir = Directory.CreateDirectory("TestLogSequence");
            TestDbBackupDir = RootTestBackupDir.CreateSubdirectory("test");

            for (var m = 0; m < TestBckupPeriodMonths; ++m)
            {
                var monthStart = StartDate.AddMonths(m);

                var monthString = monthStart.ToString("yyyy-MM-MMM");
                var monthDir = TestDbBackupDir.CreateSubdirectory(monthString);

                File.Create(Path.Combine(monthDir.FullName, string.Format("{0}-test-full.bak", monthString))).Close();
                File.Create(Path.Combine(monthDir.FullName, string.Format("{0}-01-test-log.trn", monthString))).Close();

                for (var dayStart = monthStart.AddDays(1); dayStart.Month == monthStart.Month; dayStart = dayStart.AddDays(1))
                {
                    var name = dayStart.ToString("yyyy-MM-MMM-dd") + "-test-diff.bak";
                    File.Create(Path.Combine(monthDir.FullName, name)).Close();

                    name = dayStart.ToString("yyyy-MM-MMM-dd") + "-test-log.trn";
                    File.Create(Path.Combine(monthDir.FullName, name)).Close();
                }
            }
        }

        //[TearDown]
        public static void TearDown()
        {
            RootTestBackupDir.Delete(true);
        }
    }
}
