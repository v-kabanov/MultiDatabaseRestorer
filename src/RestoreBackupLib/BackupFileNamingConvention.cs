// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-14
// Comment		
// **********************************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;

namespace RestoreBackupLib
{
    /// <summary>
    ///     Enumerates types of backups which can be contained in an acceptable backup file.
    ///     Defines constraint: backup files can contain backup of one type only.
    /// </summary>
    /// <remarks>
    ///     The reason for the constraint is that experience shows that excessive IO results when you put many backups in the same file.
    ///     SQL Server appears to read the whole file to list backups in it. Log backups are expected to be numerous, but compact.
    ///     So they should be kept in a separate file. Full backups are large and they should be put in separate file each, because
    ///     to get info about the following backups in the same file the whole full backup needs to be read, presumably.
    /// </remarks>
    public enum SupportedBackupType
    {
        None,
        Full,
        Diff,
        Log
    }

    public interface IBackupTypeInfo
    {
        string FileNameSuffix { get; }
        SupportedBackupType BackupType { get; }
        ITimePeriodFromFilesystemNameExtractor TimePeriodExtractor { get; }
    }

    public interface IBackupFileNamingConvention
    {
        /// <summary>
        ///     Get information about backup file based on its name.
        /// </summary>
        /// <param name="fileInfo">
        ///     Backup file info
        /// </param>
        /// <returns>
        ///     null if <paramref name="fileInfo"/>'s name is not a valid backup file name
        /// </returns>
        BackupFileInfo GetBackupFileInfo(FileInfo fileInfo);

        /// <summary>
        ///     Infer type of backups stored in the backup file. Files must contain backups of a particular type only.
        /// </summary>
        /// <param name="fileName">
        ///     The backup file name.
        /// </param>
        /// <returns>
        ///     <see cref="RestoreBackupLib.BackupType.None"/> if not a valid backup file name.
        /// </returns>
        SupportedBackupType GetBackupType(string fileName);
    }

    public class BackupFileNamingConvention : IBackupFileNamingConvention
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private class BackupTypeInfo : IBackupTypeInfo
        {
            public string FileNameSuffix { get; set; }
            public SupportedBackupType BackupType { get; set; }
            public ITimePeriodFromFilesystemNameExtractor TimePeriodExtractor { get; set; }
        }

        public const string DefaultDayTimestampFormat = "yyyy-MM-MMM-dd";
        public const string DefaultMonthTimestampFormat = "yyyy-MM-MMM";

        // suffixes of backup file names
        public const string FullBackupFileNameSuffix = "-full.bak";
        public const string DiffBackupFileNameSuffix = "-diff.bak";
        public const string LogBackupFileNameSuffix = "-log.trn";

        // regex must capture timestamp in first subexpression
        public const string DefaultDayTimestampFromFileNameExtractorRegex = @"^(\d{4}-\d{2}-\w{3}-\d{2})-\w+-(?:full|diff|log)\.(?:trn|bak)";
        public const string DefaultMonthTimestampFromFileNameExtractorRegex = @"^(\d{4}-\d{2}-\w{3})-\w+-(?:full|diff|log)\.(?:trn|bak)";

        private readonly Dictionary<SupportedBackupType, BackupTypeInfo> _backupTypes = new Dictionary<SupportedBackupType, BackupTypeInfo>();

        /// <summary>
        ///     Add new backup type mapping.
        /// </summary>
        /// <param name="nameSuffix">
        ///     Suffix of backup files, including extension, case insensitive
        /// </param>
        /// <param name="backupType">
        ///     Backup type
        /// </param>
        /// <param name="timePeriodExtractor">
        ///     Object implementing extraction of relevant information from file name, such as time period.
        /// </param>
        public void AddType(string nameSuffix, SupportedBackupType backupType, ITimePeriodFromFilesystemNameExtractor timePeriodExtractor)
        {
            Check.DoRequireArgumentNotNull(nameSuffix, "nameSuffix");
            Check.DoRequireArgumentNotNull(timePeriodExtractor, "timePeriodExtractor");
            Check.DoCheckArgument(null == GetTypeInfo(backupType), string.Format("Parser for {0} already mapped", backupType));
            Check.DoCheckArgument(backupType != SupportedBackupType.None, "Invalid backup type (None)");

            var intersectedType = _backupTypes.Values.Where(
                i => i.FileNameSuffix.EndsWith(nameSuffix, StringComparison.InvariantCultureIgnoreCase)
                     || nameSuffix.EndsWith(i.FileNameSuffix, StringComparison.InvariantCultureIgnoreCase))
                                              .FirstOrDefault();

            Check.DoCheckArgument(
                intersectedType == null
                , () => string.Format("Suffix {0} for {1} conflicts with suffix {2} for type {3}"
                                      , nameSuffix, backupType, intersectedType.FileNameSuffix, intersectedType.BackupType));

            var newTypeInfo = new BackupTypeInfo() {FileNameSuffix = nameSuffix, BackupType = backupType, TimePeriodExtractor = timePeriodExtractor};
            _backupTypes.Add(backupType, newTypeInfo);
        }

        public BackupFileInfo GetBackupFileInfo(FileInfo fileInfo)
        {
            BackupFileInfo result = null;

            var typeInfo = GetTypeInfo(fileInfo.Name);

            if (typeInfo != null)
            {
                var startTime = typeInfo.TimePeriodExtractor.GetTimestamp(fileInfo.Name);
                DateTime? endTime = null;

                if (!startTime.HasValue)
                {
                    Log.ErrorFormat("File start time cannot be extracted from {0}, using creation time", fileInfo.Name);
                }
                else
                {
                    endTime = typeInfo.TimePeriodExtractor.GetPeriodEnd(fileInfo.Name);
                }

                result = new BackupFileInfo(typeInfo.BackupType, startTime, endTime, fileInfo);
            }

            return result;
        }

        /// <summary>
        ///     Infer type of backups stored in the backup file. Files must contain backups of a particular type only.
        /// </summary>
        /// <param name="fileName">
        ///     The backup file name.
        /// </param>
        /// <returns>
        ///     <see cref="RestoreBackupLib.BackupType.None"/> if not a valid backup file name.
        /// </returns>
        public SupportedBackupType GetBackupType(string fileName)
        {
            var result = SupportedBackupType.None;

            var info = GetTypeInfo(fileName);

            if (info != null)
            {
                result = info.BackupType;
            }

            return result;
        }

        private BackupTypeInfo GetTypeInfo(SupportedBackupType backupType)
        {
            _backupTypes.TryGetValue(backupType, out var result);

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns>
        ///     null if not mapped
        /// </returns>
        private BackupTypeInfo GetTypeInfo(string fileName)
        {
            Check.DoRequireArgumentNotNull(fileName, "fileSystemName");
            return _backupTypes.Values.Where(t => fileName.EndsWith(t.FileNameSuffix, StringComparison.InvariantCultureIgnoreCase))
                               .FirstOrDefault();
        }

        /// <summary>
        ///     Convenience factory method creating the recommended Month-Day-day scheme convention.
        /// </summary>
        /// <remarks>
        ///     - backups in calendar monthly folders
        ///     <br />
        ///     - every month starts with single full backup in the month, file name 'yyyy-MM-MMM-{db name}-full.bak'
        ///     <br />
        ///     - every day starts with single diff backup, file name 'yyyy-MM-MMM-dd-{db name}-diff.bak'
        ///     <br />
        ///     - during day all log backups are written to 'yyyy-MM-MMM-{db name}-log.trn'
        /// </remarks>
        /// <returns></returns>
        public static BackupFileNamingConvention CreateDefaultMonthDayDayConvention()
        {
            var result = new BackupFileNamingConvention();

            var fullBackupFileNameParser = new BackupFilesystemObjectNameParser(DefaultMonthTimestampFromFileNameExtractorRegex, DefaultMonthTimestampFormat, null);
            var dayBackupFileNameParser = new BackupFilesystemObjectNameParser(DefaultDayTimestampFromFileNameExtractorRegex, DefaultDayTimestampFormat, null);

            var monthPeriodExtractor = TimePeriodFromFilesystemNameExtractor.CreateForCalendarMonths(fullBackupFileNameParser);
            var dayPeriodExtractor = TimePeriodFromFilesystemNameExtractor.CreateForCalendarDays(dayBackupFileNameParser);

            result.AddType(FullBackupFileNameSuffix, SupportedBackupType.Full, monthPeriodExtractor);
            result.AddType(DiffBackupFileNameSuffix, SupportedBackupType.Diff, dayPeriodExtractor);
            result.AddType(LogBackupFileNameSuffix, SupportedBackupType.Log, dayPeriodExtractor);

            return result;
        }
    }
}