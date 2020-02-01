// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-07
// Comment		
// **********************************************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;

namespace RestoreBackupLib
{
    public class BackupFileCatalog : IBackupFileCatalog
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public const string MonthFolderNameRegex = @"\d{4}-\d{2}-[A-Za-z]{3}";
        public const string MonthFolderNameFormat = "yyyy-MM-MMM";

        // sorted in ascending order
        private readonly List<BackupFileFolderInfo> _backupFileFolders;

        public BackupFileCatalog(
            IBackupDirectoryNamingConvention namingConvention
            , string databaseBackupFolder)
        {
            Check.DoRequireArgumentNotNull(namingConvention, "namingConvention");
            Check.DoRequireArgumentNotNull(databaseBackupFolder, "databaseBackupFolder");

            NamingConvention = namingConvention;
            DatabaseBackupDirectory = new DirectoryInfo(databaseBackupFolder);
            Check.DoCheckArgument(DatabaseBackupDirectory.Exists, () => string.Format("Backup folder {0} does not exist", databaseBackupFolder));

            _backupFileFolders = GetBackupFolders();
        }

        public IBackupDirectoryNamingConvention NamingConvention { get; private set; }

        public DirectoryInfo DatabaseBackupDirectory { get; private set; }

        /// <summary>
        ///     Get sequence of folders whose start time (the start of the period during which backups were written to it) is strictly less than the
        ///     specified point in time <paramref name="pointInTime"/>, sorted in descending order.
        /// </summary>
        /// <param name="pointInTime">
        ///     Optional, null for unrestricted
        /// </param>
        /// <returns></returns>
        public IEnumerable<BackupFileFolderInfo> GetPriorFolderSequence(DateTime? pointInTime)
        {
            return _backupFileFolders
                .OrderByDescending(f => f.StartTime)
                .Where(f => !pointInTime.HasValue || f.StartTime < pointInTime.Value);
        }

        /// <summary>
        ///     Get sequence of folders whose end time (the end of the period during which backups were written to it) is strictly greater than the
        ///     specified point in time <paramref name="pointInTime"/>, sorted in ascending order.
        /// </summary>
        /// <param name="pointInTime">
        ///     Optional, null for unrestricted
        /// </param>
        /// <returns></returns>
        public IEnumerable<BackupFileFolderInfo> GetForwardFolderSequence(DateTime? pointInTime)
        {
            return _backupFileFolders
                .Where(f => !pointInTime.HasValue || f.EndTime > pointInTime.Value);
        }

        /// <summary>
        ///     Find last folder whose start time (the start of the period during which backups were written to it) is strictly less than the
        ///     specified point in time <paramref name="pointInTime"/>
        /// </summary>
        /// <param name="pointInTime">
        ///     null for lastest.
        /// </param>
        /// <returns>
        ///     null if not found
        /// </returns>
        public BackupFileFolderInfo GetLastFolder(DateTime? pointInTime)
        {
            return _backupFileFolders
                .OrderByDescending(f => f.StartTime)
                .Where(f => !pointInTime.HasValue || f.StartTime < pointInTime.Value)
                .FirstOrDefault();
        }

        public BackupFileFolderInfo GetContainingFolder(DateTime backupTime)
        {
            return _backupFileFolders
                .Where(f => f.StartTime >= backupTime && f.EndTime > backupTime)
                .SingleOrDefault();
        }

        public BackupFileFolderInfo GetContainingFolder(BackupFileInfo backupFile)
        {
            Check.DoRequireArgumentNotNull(backupFile, "backupFile");

            return _backupFileFolders
                .Where(f => f.DirectoryInfo.Name.Equals(backupFile.FileInfo.Directory.Name, StringComparison.InvariantCultureIgnoreCase))
                .SingleOrDefault();
        }

        /// <summary>
        ///     Find next folder whose start time (the start of the period during which backups were written to it) is strictly greater than the
        ///     specified point in time <paramref name="pointInTime"/>
        /// </summary>
        /// <param name="pointInTime">
        ///     null for earliest.
        /// </param>
        /// <returns>
        ///     null if not found
        /// </returns>
        public BackupFileFolderInfo GetNextFolder(DateTime? pointInTime)
        {
            return GetForwardFolderSequence(pointInTime).Skip(1).FirstOrDefault();
        }

        /// <summary>
        ///     Get reverse sequence of backup files which started before <paramref name="pointInTime"/>.
        /// </summary>
        /// <param name="pointInTime">
        ///     Point in time before which the database backup to be found must have started; null for latest.
        /// </param>
        /// <param name="backupType">
        ///     Type of backup files to find.
        /// </param>
        /// <returns>
        ///     Sequence ordered by file start time <see cref="BackupFileInfo.StartTime"/> descending.
        /// </returns>
        /// <remarks>
        ///     Note that database backup (diff or full) restores database to a point at which reading from data files while performing backup finished,
        ///     which is in general some time after the backup start time. Thus in order to restore precisely to a point in time within the time period when
        ///     database backup Bn was being performed, one must go back and restore previous database backup followed by log backups up to and including
        ///     the one taken after the Bn backup. This is the reason to expose this method rather than provide just a method to retrieve the sequence
        ///     of files to use for restoration. One must analyze the contents of the database backup (diff or full) in order to understand whether
        ///     one can restore to a point in time within that file's time period. If not suitable, one must go back and check previous backup.
        ///     Passing <see cref="BackupFileInfo.StartTime"/> the caller shall never get the same file back; thus it is possible to iterate backwards.
        /// </remarks>
        public IEnumerable<BackupFileInfo> GetReverseBackupSequence(DateTime? pointInTime, SupportedBackupType backupType)
        {
            Check.DoCheckArgument(backupType != SupportedBackupType.None);

            foreach (var folder in GetPriorFolderSequence(pointInTime))
            {
                var files = folder.AllFiles
                    .Where(f => f.BackupType == backupType)
                    .Where(f => !pointInTime.HasValue || f.StartTime < pointInTime.Value)
                    .OrderByDescending(f => f.StartTime);

                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        ///     Get open ended sequence in chronological order of log backup files which may contain log backups made after <paramref name="databaseBackupTime"/>
        /// </summary>
        /// <remarks>
        ///     In most cases the sequence can be limited by the first log backup file which started after <paramref name="databaseBackupStartTime"/>
        ///     Then start time, not end time would take part in determining whether target is passed because
        ///     end time does not mean last time backup was actually written into the file, only that it could have been written.
        ///     In contrast, start time guarantees that at least one backup started at or after the start time
        ///     actually went into the file (that is unless empty backup file is created which is not supposed to happen).
        ///     But to work with empty backup files it's better to stop after finding actual backup in file which started after full or diff backup was taken.
        ///     Therefore leaving this method to return open-ended sequence and planner will parse file headers and stop
        ///     iterating ASAP, thus keeping backup folders loaded lazily.
        /// </remarks>
        public IEnumerable<BackupFileInfo> GetLogBackupsSequence(DateTime databaseBackupStartTime)
        {
            var backupFileFolders = GetForwardFolderSequence(databaseBackupStartTime);

            foreach (var folder in backupFileFolders)
            {
                var files = folder.LogFiles.Where(f => f.EndTime > databaseBackupStartTime);
                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }

        private DateTime? GetMonthDirectoryStartDate(string directoryName)
        {
            if (DateTime.TryParseExact(directoryName, MonthFolderNameFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startOfMonth))
            {
                return startOfMonth;
            }
            return null;
        }

        /// <summary>
        ///     Returns list sorted in chronological order
        /// </summary>
        private List<BackupFileFolderInfo> GetBackupFolders()
        {
            var subfolders = DatabaseBackupDirectory.GetDirectories();

            var result = new List<BackupFileFolderInfo>(subfolders.Length);

            foreach (var directoryInfo in subfolders)
            {
                var folderInfo = NamingConvention.GetFolderInfo(directoryInfo);
                if (folderInfo != null)
                {
                    result.Add(folderInfo);
                }
            }

            result = result.OrderBy(f => f.StartTime).ToList();

            InferEndTimeWhereNotDefinedExplicitly(result);

            return result;
        }
 
        /// <summary>
        ///     Set EndTime to start of the next folder where it has not yet been set explicitly.
        /// </summary>
        /// <param name="objectSequence">
        ///     Must be sorted chronologically
        /// </param>
        public static void InferEndTimeWhereNotDefinedExplicitly<T>(List<T> objectSequence)
            where T : IBackupFilesystemObjectInfo
        {
            for (var i = 0; i < objectSequence.Count; ++i)
            {
                var currentObject = objectSequence[i];
                if (!currentObject.PeriodEndDeclaredExplicitly)
                {
                    if (i + 1 < objectSequence.Count)
                    {
                        var nextObject = objectSequence[i + 1];
                        currentObject.EndTime = nextObject.StartTime;
                    }
                    else
                    {
                        // last folder: using last write time
                        currentObject.EndTime = currentObject.FileSystemInfo.LastWriteTime;
                    }
                }

                Check.DoAssertLambda(currentObject.StartTime < currentObject.EndTime
                    , () => string.Format("End time of {0} equals its start time {1}", currentObject.FileSystemInfo.Name, currentObject.StartTime));
            }
        }
 
        /// <summary>
        ///     Factory method creating catalog for calendar month folders and given file naming convention.
        /// </summary>
        /// <param name="fileNamingConvention">
        ///     File naming convention to use.
        /// </param>
        public static BackupFileCatalog CreateMonthlyCatalog(string databaseBackupFolder, IBackupFileNamingConvention fileNamingConvention)
        {
            Check.DoRequireArgumentNotNull(databaseBackupFolder, "databaseBackupFolder");
            Check.DoRequireArgumentNotNull(fileNamingConvention, "fileNamingConvention");


            var namingConvention = BackupDirectoryNamingConvention.CreateMonthlyConvention(fileNamingConvention);

            var result = new BackupFileCatalog(namingConvention, databaseBackupFolder);

            return result;
        }

        /// <summary>
        ///     Factory method creating catalog for calendar month folders and default 'month-day-day' file naming convention.
        /// </summary>
        public static BackupFileCatalog CreateDefaultMonthDayDayCatalog(string databaseBackupFolder)
        {
            Check.DoRequireArgumentNotNull(databaseBackupFolder, "databaseBackupFolder");

            var namingConvention = BackupDirectoryNamingConvention.CreateDefaultMonthDayDayConvention();

            var result = new BackupFileCatalog(namingConvention, databaseBackupFolder);

            return result;
        }
    }
}