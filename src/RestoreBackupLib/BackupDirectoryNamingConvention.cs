// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-15
// Comment		
// **********************************************************************************************/

using System.IO;
using System.Reflection;
using log4net;

namespace RestoreBackupLib
{
    public interface IBackupDirectoryNamingConvention
    {
        IBackupFileNamingConvention BackupFileNamingConvention { get; }

        BackupFileFolderInfo GetFolderInfo(DirectoryInfo directoryInfo);
    }

    public class BackupDirectoryNamingConvention : IBackupDirectoryNamingConvention
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public BackupDirectoryNamingConvention(
            ITimePeriodFromFilesystemNameExtractor timePeriodExtractor
            , IBackupFileNamingConvention backupFileNamingConvention)
        {
            TimePeriodExtractor = timePeriodExtractor;
            BackupFileNamingConvention = backupFileNamingConvention;
        }

        public ITimePeriodFromFilesystemNameExtractor TimePeriodExtractor { get; private set; }
        public IBackupFileNamingConvention BackupFileNamingConvention { get; private set; }

        public BackupFileFolderInfo GetFolderInfo(DirectoryInfo directoryInfo)
        {
            BackupFileFolderInfo result = null;
            var periodStart = TimePeriodExtractor.GetTimestamp(directoryInfo.Name);

            if (periodStart.HasValue)
            {
                var periodEnd = TimePeriodExtractor.GetPeriodEnd(directoryInfo.Name);

                result = new BackupFileFolderInfo(BackupFileNamingConvention, directoryInfo, periodStart.Value, periodEnd);
            }

            return result;
        }

        /// <summary>
        ///     Factory method creating convention for calendar month folders and given file naming convention.
        /// </summary>
        /// <param name="fileNamingConvention">
        ///     File naming convention to use.
        /// </param>
        /// <returns>
        /// </returns>
        public static BackupDirectoryNamingConvention CreateMonthlyConvention(IBackupFileNamingConvention fileNamingConvention)
        {
            Check.DoRequireArgumentNotNull(fileNamingConvention, "fileNamingConvention");

            var monthDirectoryNameParser = new BackupFilesystemObjectNameParser(
                "(.+)", RestoreBackupLib.BackupFileNamingConvention.DefaultMonthTimestampFormat, null);

            var monthPeriodExtractor = TimePeriodFromFilesystemNameExtractor.CreateForCalendarMonths(monthDirectoryNameParser);

            var result = new BackupDirectoryNamingConvention(monthPeriodExtractor, fileNamingConvention);

            return result;
        }

        /// <summary>
        ///     Factory method creating convention for calendar month folders and default 'month-day-day' file naming convention.
        /// </summary>
        /// <returns></returns>
        public static BackupDirectoryNamingConvention CreateDefaultMonthDayDayConvention()
        {
            var defaultMonthDayDayConvention =
                RestoreBackupLib.BackupFileNamingConvention.CreateDefaultMonthDayDayConvention();

            return CreateMonthlyConvention(defaultMonthDayDayConvention);
        }
    }
}