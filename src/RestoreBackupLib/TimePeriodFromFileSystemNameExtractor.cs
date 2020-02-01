// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-15
// Comment		
// **********************************************************************************************/

using System;

namespace RestoreBackupLib
{
    /// <summary>
    ///     Object that in addition to tasks performed by <see cref="IBackupFilesystemNameParser"/> can also
    ///     infer end of covered period from filesystem object's name.
    /// </summary>
    public interface ITimePeriodFromFilesystemNameExtractor : IBackupFilesystemNameParser
    {
        DateTime? GetPeriodEnd(string fileSystemObjectName);
    }

    /// <summary>
    ///     Delegating generic implementation.
    /// </summary>
    public class TimePeriodFromFilesystemNameExtractor : ITimePeriodFromFilesystemNameExtractor
    {
        public IBackupFilesystemNameParser BackupFilesystemNameParser { get; }

        public Func<DateTime, DateTime> EndOfPeriodCalculator { get; }

        /// <summary>
        ///     Constructor.
        /// </summary>
        /// <param name="backupFilesystemNameParser">
        ///     Parser to delegate <see cref="IBackupFilesystemNameParser"/>'s method calls to.
        /// </param>
        /// <param name="endOfPeriodCalculator">
        ///     Optional; if null, <see cref="GetPeriodEnd"/> will always return null
        /// </param>
        public TimePeriodFromFilesystemNameExtractor(IBackupFilesystemNameParser backupFilesystemNameParser, Func<DateTime, DateTime> endOfPeriodCalculator)
        {
            Check.DoRequireArgumentNotNull(backupFilesystemNameParser, "BackupFilesystemNameParser");

            BackupFilesystemNameParser = backupFilesystemNameParser;
            EndOfPeriodCalculator = endOfPeriodCalculator;
        }

        /// <summary>
        ///     May return null
        /// </summary>
        /// <param name="fileSystemName">
        ///     Filesystem object name, no path
        /// </param>
        /// <returns>
        ///     null if not supported or failed to extract
        /// </returns>
        public string GetDatabaseName(string fileSystemName)
        {
            return BackupFilesystemNameParser.GetDatabaseName(fileSystemName);
        }

        /// <summary>
        ///     May return null
        /// </summary>
        /// <param name="fileSystemName">
        ///     Filesystem object name, no path
        /// </param>
        /// <returns>
        ///     null if not supported or failed to extract
        /// </returns>
        public DateTime? GetTimestamp(string fileSystemName)
        {
            return BackupFilesystemNameParser.GetTimestamp(fileSystemName);
        }

        public DateTime? GetPeriodEnd(string fileSystemObjectName)
        {
            if (null != EndOfPeriodCalculator)
            {
                var timestamp = BackupFilesystemNameParser.GetTimestamp(fileSystemObjectName);
                if (timestamp.HasValue)
                    return EndOfPeriodCalculator.Invoke(timestamp.Value);
            }

            return null;
        }

        public static TimePeriodFromFilesystemNameExtractor CreateForCalendarDays(IBackupFilesystemNameParser nameParser)
        {
            return new TimePeriodFromFilesystemNameExtractor(nameParser, (t) => t.AddDays(1));
        }

        public static TimePeriodFromFilesystemNameExtractor CreateForCalendarMonths(IBackupFilesystemNameParser nameParser)
        {
            return new TimePeriodFromFilesystemNameExtractor(nameParser, (t) => t.AddMonths(1));
        }
    }
}