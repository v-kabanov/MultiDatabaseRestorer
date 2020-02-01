using NUnit.Framework;
using RestoreBackupLib;
using System.Linq;
using System.Reflection;
using log4net;
using Microsoft.SqlServer.Management.Smo;

namespace RestoreBackupLibTest
{
    [TestFixture]
    public class RestorePlannerTestFixture
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        [Test]
        public void ScratchPad()
        {
            foreach (var l in TestSession.Server.Logins.Cast<Login>())
                TestContext.Progress.WriteLine($"{l.Name} - {l.LoginType}");

            var login = TestSession.Server.Logins["mesdeveloper"];
            Assert.IsNotNull(login);

            var db = TestSession.Server.Databases["MES"];
            ReBindDatabaseUsersWithSqlLogins(db);
        }
        public void ReBindDatabaseUsersWithSqlLogins(Database db)
        {
            // read actual names
            var existingLogins = string.Join(", ", db.Parent.Logins.Cast<Login>().Select(x => x.Name));
            Log.InfoFormat("Existing logins: {0}", existingLogins);

            for (var i = 0; i < db.Users.Count; ++i)
            {
                var user = db.Users[i];
                if (user.IsSystemObject || user.LoginType != LoginType.SqlLogin)
                    continue;

                if (!string.IsNullOrEmpty(user.Login))
                {
                    Log.InfoFormat("User {0} is already mapped to login {1}", user.Name, user.Login);
                    continue;
                }

                var login = db.Parent.Logins[user.Name];
                if (login == null)
                {
                    Log.InfoFormat("No login for orphaned user {0}", user.Name);
                    continue;
                }

                db.ExecuteNonQuery($"alter user [{user.Name}] with login = [{login.Name}]");
            }
        }

        [Test]
        public void TestLatest()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(TestSession.TestDbBackupDir.FullName);

            var databaseName = "test";
            var sqlServerProxy = new TestSqlServerProxy(catalog.NamingConvention.BackupFileNamingConvention, databaseName);
            var planner = new RestorePlanner(catalog, sqlServerProxy, databaseName);

            var itemsToRestore = planner.CreatePlan(null);
            Assert.IsNotNull(itemsToRestore);

            // full month of backups, every day starting with full or diff backup, then full day of log backups
            Assert.AreEqual(TestSqlServerProxy.LogBackupsPerDay + 2, itemsToRestore.Count);
            Assert.AreEqual(BackupType.Full, itemsToRestore[0].BackupType);
            Assert.AreEqual(BackupType.DifferentialDatabase, itemsToRestore[1].BackupType);
            Assert.AreEqual(BackupType.Log, itemsToRestore[2].BackupType);

            var expectedLastLogBackupStartTime = TestSession.StartDate.AddMonths(TestSession.TestBckupPeriodMonths).AddDays(-1).Add(TestSqlServerProxy.LastBackupTime);

            Assert.AreEqual(expectedLastLogBackupStartTime, itemsToRestore.Last().BackupStartTime);
        }

        [Test]
        public void TestNoDiff()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(TestSession.TestDbBackupDir.FullName);

            var databaseName = "test";
            var sqlServerProxy = new TestSqlServerProxy(catalog.NamingConvention.BackupFileNamingConvention, databaseName);
            var planner = new RestorePlanner(catalog, sqlServerProxy, databaseName);

            var logBackupsToSpan = 3;
            var minutesAfterFirstBackup = TestSqlServerProxy.BackupIntervalMinutes * logBackupsToSpan;
            var targetTime = TestSession.StartDate.Add(TestSqlServerProxy.FirstBackupTime).AddMinutes(minutesAfterFirstBackup);

            var itemsToRestore = planner.CreatePlan(targetTime);
            Assert.IsNotNull(itemsToRestore);

            // 1 full followed by planned number of log backups plus 1, because must take first log following target time
            Assert.AreEqual(logBackupsToSpan + 2, itemsToRestore.Count);
            Assert.AreEqual(BackupType.Full, itemsToRestore[0].BackupType);
            Assert.AreEqual(BackupType.Log, itemsToRestore[1].BackupType);

            var expectedLastLogBackupStartTime = TestSession.StartDate.Add(TestSqlServerProxy.FirstBackupTime)
                .AddMinutes((logBackupsToSpan + 1) * TestSqlServerProxy.BackupIntervalMinutes);

            Assert.AreEqual(expectedLastLogBackupStartTime, itemsToRestore.Last().BackupStartTime);
        }

        [Test]
        public void TestWithDiff()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(TestSession.TestDbBackupDir.FullName);

            var databaseName = "test";
            var sqlServerProxy = new TestSqlServerProxy(catalog.NamingConvention.BackupFileNamingConvention, databaseName);
            var planner = new RestorePlanner(catalog, sqlServerProxy, databaseName);

            var logBackupsToSpan = 3;
            var minutesAfterFirstBackup = TestSqlServerProxy.BackupIntervalMinutes * logBackupsToSpan;
            var targetTime = TestSession.StartDate.AddDays(1).Add(TestSqlServerProxy.FirstBackupTime).AddMinutes(minutesAfterFirstBackup);

            var itemsToRestore = planner.CreatePlan(targetTime);
            Assert.IsNotNull(itemsToRestore);

            // 1 full, then diff followed by planned number of log backups plus 1, because must take first log following target time
            Assert.AreEqual(logBackupsToSpan + 3, itemsToRestore.Count);
            Assert.AreEqual(BackupType.Full, itemsToRestore[0].BackupType);
            Assert.AreEqual(BackupType.DifferentialDatabase, itemsToRestore[1].BackupType);
            Assert.AreEqual(BackupType.Log, itemsToRestore[2].BackupType);

            var expectedLastLogBackupStartTime = TestSession.StartDate.AddDays(1).Add(TestSqlServerProxy.FirstBackupTime)
                .AddMinutes((logBackupsToSpan + 1) * TestSqlServerProxy.BackupIntervalMinutes);

            Assert.AreEqual(expectedLastLogBackupStartTime, itemsToRestore.Last().BackupStartTime);
        }

        [Test]
        public void TestWithinFull()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(TestSession.TestDbBackupDir.FullName);

            var databaseName = "test";
            var sqlServerProxy = new TestSqlServerProxy(catalog.NamingConvention.BackupFileNamingConvention, databaseName);
            var planner = new RestorePlanner(catalog, sqlServerProxy, databaseName);

            var targetTime = TestSession.StartDate.AddMonths(1).Add(TestSqlServerProxy.FirstBackupTime).AddSeconds(1);

            var itemsToRestore = planner.CreatePlan(targetTime);
            Assert.IsNotNull(itemsToRestore);

            // 1 full, then diff followed by all log backups in the day plus 1, because must take first log following target time
            Assert.AreEqual(TestSqlServerProxy.LogBackupsPerDay + 1 + 2, itemsToRestore.Count);
            Assert.AreEqual(BackupType.Full, itemsToRestore[0].BackupType);
            Assert.AreEqual(BackupType.DifferentialDatabase, itemsToRestore[1].BackupType);
            Assert.AreEqual(BackupType.Log, itemsToRestore[2].BackupType);

            var expectedLastLogBackupStartTime = TestSession.StartDate.AddMonths(1).Add(TestSqlServerProxy.FirstBackupTime)
                .AddMinutes(TestSqlServerProxy.BackupIntervalMinutes);

            Assert.AreEqual(expectedLastLogBackupStartTime, itemsToRestore.Last().BackupStartTime);
        }

        [Test]
        public void TestTargetNonWorkHours()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(TestSession.TestDbBackupDir.FullName);

            var databaseName = "test";
            var sqlServerProxy = new TestSqlServerProxy(catalog.NamingConvention.BackupFileNamingConvention, databaseName);
            var planner = new RestorePlanner(catalog, sqlServerProxy, databaseName);

            var targetTime = TestSession.StartDate.AddMonths(1).AddDays(2).Add(TestSqlServerProxy.FirstBackupTime).AddHours(-1);

            var itemsToRestore = planner.CreatePlan(targetTime);
            Assert.IsNotNull(itemsToRestore);

            // 1 full, then diff followed by all log backups in the day plus 1, because must take first log following target time
            Assert.AreEqual(TestSqlServerProxy.LogBackupsPerDay + 2 + 1, itemsToRestore.Count);
            Assert.AreEqual(BackupType.Full, itemsToRestore[0].BackupType);
            Assert.AreEqual(BackupType.DifferentialDatabase, itemsToRestore[1].BackupType);
            Assert.AreEqual(BackupType.Log, itemsToRestore[2].BackupType);

            var expectedLastLogBackupStartTime = TestSession.StartDate.AddMonths(1).AddDays(2).Add(TestSqlServerProxy.FirstBackupTime)
                .AddMinutes(TestSqlServerProxy.BackupIntervalMinutes);

            Assert.AreEqual(expectedLastLogBackupStartTime, itemsToRestore.Last().BackupStartTime);
        }

        //[Test]
        public void RealPlannerScratchPad()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(@"F:\temp\backup\DOCUMENTS");
            var databaseName = "DOCUMENTS";
            var sqlServerProxy = new SqlServer2014Proxy(TestSession.Server);

            var planner = new RestorePlanner(catalog, sqlServerProxy, databaseName);
            var backupItems = planner.CreatePlan(null);

            Assert.That(backupItems, Is.Not.Empty);
        }
    }
}
