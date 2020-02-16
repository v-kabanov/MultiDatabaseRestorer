using NUnit.Framework;
using RestoreBackupLib;
using System.Linq;

namespace RestoreBackupLibTest
{
    [TestFixture]
    public class CatalogTestFixture
    {
        [Test]
        public void TestLatest()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(TestSession.TestDbBackupDir.FullName);

            IBackupFileCatalog icatalog = catalog;

            var fullBackups = icatalog.GetReverseBackupSequence(null, SupportedBackupType.Full);
            var diffBackups = icatalog.GetReverseBackupSequence(null, SupportedBackupType.Diff);

            Assert.IsNotNull(fullBackups);
            Assert.IsNotNull(diffBackups);

            var lastFull = fullBackups.FirstOrDefault();
            var lastDiff = diffBackups.FirstOrDefault();

            Assert.IsNotNull(lastFull);
            Assert.IsNotNull(lastDiff);

            Assert.AreEqual(SupportedBackupType.Full, lastFull.BackupType);
            Assert.AreEqual(SupportedBackupType.Diff, lastDiff.BackupType);

            var lastBackupMonthStart = TestSession.StartDate.AddMonths(TestSession.TestBckupPeriodMonths - 1);
            var lastBackupDayStart = lastBackupMonthStart.AddMonths(1).AddDays(-1);

            Assert.AreEqual(lastBackupMonthStart, lastFull.StartTime);
            Assert.AreEqual(lastBackupDayStart, lastDiff.StartTime);

            var logBackups = icatalog.GetLogBackupsSequence(lastBackupDayStart);

            Assert.IsNotNull(logBackups);
            var logBackupsList = logBackups.ToList();

            Assert.AreEqual(1, logBackupsList.Count);

            var lastLog = logBackups.First();
            Assert.IsNotNull(lastLog);
            Assert.AreEqual(lastBackupDayStart, lastLog.StartTime);
            Assert.AreEqual(lastBackupDayStart.AddDays(1), lastLog.EndTime);
        }

        [Test]
        public void TestNoDiffAndTwoLogs()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(TestSession.TestDbBackupDir.FullName);

            IBackupFileCatalog icatalog = catalog;

            // 1 second before the start of the 2nd day of the second month
            var targetTime = TestSession.StartDate.AddMonths(1).AddHours(23).AddMinutes(59).AddSeconds(59);
            var logBackupCountApplicable = (int)(TestSession.StartDate.AddMonths(TestSession.TestBckupPeriodMonths) - TestSession.StartDate.AddMonths(1)).TotalDays;

            var fullBackups = icatalog.GetReverseBackupSequence(targetTime, SupportedBackupType.Full);
            var diffBackups = icatalog.GetReverseBackupSequence(targetTime, SupportedBackupType.Diff);

            Assert.IsNotNull(fullBackups);
            Assert.IsNotNull(diffBackups);

            var lastFull = fullBackups.FirstOrDefault();
            var lastDiff = diffBackups.FirstOrDefault();

            Assert.IsNotNull(lastFull);
            Assert.IsNotNull(lastDiff);

            Assert.AreEqual(SupportedBackupType.Full, lastFull.BackupType);
            Assert.AreEqual(SupportedBackupType.Diff, lastDiff.BackupType);

            var lastBackupMonthStart = TestSession.StartDate.AddMonths(1);
            var prevBackupDayStart = lastBackupMonthStart.AddDays(-1);

            Assert.AreEqual(lastBackupMonthStart, lastFull.StartTime);
            Assert.AreEqual(prevBackupDayStart, lastDiff.StartTime);

            var logBackups = icatalog.GetLogBackupsSequence(lastBackupMonthStart);

            Assert.IsNotNull(logBackups);
            var logBackupsList = logBackups.ToList();

            Assert.AreEqual(logBackupCountApplicable, logBackupsList.Count);

            var firstLog = logBackups.First();
            Assert.IsNotNull(firstLog);
            Assert.AreEqual(lastBackupMonthStart, firstLog.StartTime);
            Assert.AreEqual(lastBackupMonthStart.AddDays(1), firstLog.EndTime);

            var secondLog = logBackups.Skip(1).First();
            Assert.IsNotNull(secondLog);
            Assert.AreEqual(lastBackupMonthStart.AddDays(1), secondLog.StartTime);
            Assert.AreEqual(lastBackupMonthStart.AddDays(2), secondLog.EndTime);
        }

        [Test]
        public void TestTargetStartOfMonth()
        {
            var catalog = BackupFileCatalog.CreateDefaultMonthDayDayCatalog(TestSession.TestDbBackupDir.FullName);

            IBackupFileCatalog icatalog = catalog;

            // 1 second before the start of the 2nd day of the second month
            var targetTime = TestSession.StartDate.AddMonths(2);
            // log backup of the day prior to the target time will be necessary
            var logBackupCountApplicable = (int)(TestSession.StartDate.AddMonths(TestSession.TestBckupPeriodMonths) - TestSession.StartDate.AddMonths(2).AddDays(-1)).TotalDays;

            var fullBackups = icatalog.GetReverseBackupSequence(targetTime, SupportedBackupType.Full);
            var diffBackups = icatalog.GetReverseBackupSequence(targetTime, SupportedBackupType.Diff);

            Assert.IsNotNull(fullBackups);
            Assert.IsNotNull(diffBackups);

            var lastFull = fullBackups.FirstOrDefault();
            var lastDiff = diffBackups.FirstOrDefault();

            Assert.IsNotNull(lastFull);
            Assert.IsNotNull(lastDiff);

            Assert.AreEqual(SupportedBackupType.Full, lastFull.BackupType);
            Assert.AreEqual(SupportedBackupType.Diff, lastDiff.BackupType);

            var lastBackupMonthStart = TestSession.StartDate.AddMonths(1);
            var lastBackupDayStart = lastBackupMonthStart.AddMonths(1).AddDays(-1);

            Assert.AreEqual(lastBackupMonthStart, lastFull.StartTime);
            Assert.AreEqual(lastBackupDayStart, lastDiff.StartTime);

            var logBackups = icatalog.GetLogBackupsSequence(lastBackupDayStart);

            Assert.IsNotNull(logBackups);
            var logBackupsList = logBackups.ToList();

            Assert.AreEqual(logBackupCountApplicable, logBackupsList.Count);

            var firstLog = logBackups.First();
            Assert.IsNotNull(firstLog);
            Assert.AreEqual(lastBackupDayStart, firstLog.StartTime);
            Assert.AreEqual(lastBackupDayStart.AddDays(1), firstLog.EndTime);

            var secondLog = logBackups.Skip(1).First();
            Assert.IsNotNull(secondLog);
            Assert.AreEqual(lastBackupDayStart.AddDays(1), secondLog.StartTime);
            Assert.AreEqual(lastBackupDayStart.AddDays(2), secondLog.EndTime);
        }
    }
}

