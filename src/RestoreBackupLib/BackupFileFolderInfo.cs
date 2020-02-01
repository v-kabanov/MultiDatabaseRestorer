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
    public interface IBackupFilesystemObjectInfo
    {
        /// <summary>
        ///     Start of the period during which were written (measured by start time) into the filesystem object, inclusive.
        /// </summary>
        DateTime StartTime { get; }

        /// <summary>
        ///     End of the period during which were written (measured by start time) into the filesystem object, exclusive.
        /// </summary>
        DateTime EndTime { get; set; }

        /// <summary>
        ///     Whether <see cref="EndTime"/> was declared in the object name.
        /// </summary>
        /// <remarks>
        ///     End time can also be inferred from object metadata (eg last write time) or environment (eg start of the chronologically next object)
        /// </remarks>
        bool PeriodEndDeclaredExplicitly { get; }

        FileSystemInfo FileSystemInfo { get; }
    }

    /// <summary>
    ///     Folder containing backup files.
    /// </summary>
    public class BackupFileFolderInfo : IBackupFilesystemObjectInfo
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<BackupFileInfo> _allFiles;

        public BackupFileFolderInfo(IBackupFileNamingConvention fileNamingConvention, DirectoryInfo directoryInfo, DateTime startTime, DateTime? endTime)
        {
            Check.DoRequireArgumentNotNull(fileNamingConvention, "fileNamingConvention");
            Check.DoRequireArgumentNotNull(directoryInfo, "directoryInfo");
            Check.DoCheckArgument(endTime > startTime, "Time period");

            DirectoryInfo = directoryInfo;
            StartTime = startTime;

            PeriodEndDeclaredExplicitly = endTime.HasValue;
            EndTime = endTime.HasValue ? endTime.Value : directoryInfo.LastWriteTime;

            BackupFileNamingConvention = fileNamingConvention;
        }

        public IBackupFileNamingConvention BackupFileNamingConvention { get; set; }

        public DirectoryInfo DirectoryInfo { get; private set; }
        public DateTime StartTime { get; private set; }

        /// <summary>
        ///     End time may get known only after parsing all sibling folders, inferred from the next folder's timestamp/start time.
        /// </summary>
        public DateTime EndTime { get; set; }

        public bool PeriodEndDeclaredExplicitly { get; private set; }

        public FileSystemInfo FileSystemInfo => this.DirectoryInfo;

        /// <summary>
        ///     Lazy initialization, sorted in ascending order
        /// </summary>
        public IEnumerable<BackupFileInfo> AllFiles
        {
            get
            {
                EnsureFileListLoaded();
                return _allFiles;
            }
        }

        /// <summary>
        ///     Lazy initialization, sorted in ascending order
        /// </summary>
        public IEnumerable<BackupFileInfo> FullFiles
        {
            get
            {
                EnsureFileListLoaded();
                return _allFiles.Where(f => f.IsFull);
            }
        }
        
        /// <summary>
        ///     Lazy initialization, sorted in ascending order
        /// </summary>
        public IEnumerable<BackupFileInfo> DifFiles
        {
            get
            {
                EnsureFileListLoaded();
                return _allFiles.Where(f => f.IsDiff);
            }
        }

        /// <summary>
        ///     Lazy initialization, sorted in ascending order.
        /// </summary>
        public IEnumerable<BackupFileInfo> LogFiles
        {
            get
            {
                EnsureFileListLoaded();
                return _allFiles.Where(f => f.IsLog);
            }
        }

        /// <summary>
        ///     Get start of the time period in which the backup file was made from its name.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns>
        ///     null if time cannot be extracted
        /// </returns>
        private DateTime? GetFileStartTime(string fileName, string timeFormat)
        {
            if (DateTime.TryParseExact(fileName, timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }
            return null;
        }

        private void LoadFileList()
        {
            _allFiles = GetBackupFiles(DirectoryInfo.GetFiles().ToList());
        }

        private void EnsureFileListLoaded()
        {
            if (_allFiles == null)
            {
                _allFiles = GetBackupFiles(DirectoryInfo.GetFiles().ToList());
            }
        }

        private List<BackupFileInfo> GetBackupFiles(List<FileInfo> files)
        {
            Check.DoRequireArgumentNotNull(files, "files");

            var result = new List<BackupFileInfo>(files.Count);

            var filteredFiles = files.Where(f => BackupFileNamingConvention.GetBackupType(f.Name) != SupportedBackupType.None);

            foreach (var info in filteredFiles)
            {
                var backupFileInfo = BackupFileNamingConvention.GetBackupFileInfo(info);

                result.Add(backupFileInfo);
            }

            result = result.OrderBy(f => f.StartTime).ToList();

            InferEndTimeWhereNotDefinedExplicitly(result);

            return result;
        }

        /// <summary>
        ///     Set EndTime to start of the next file of the same type where it has not yet been set explicitly.
        /// </summary>
        /// <param name="objectSequence">
        ///     Must be sorted chronologically
        /// </param>
        public static void InferEndTimeWhereNotDefinedExplicitly(List<BackupFileInfo> objectSequence)
        {
            var supportedBackupTypes = Enumerable.Cast<SupportedBackupType>(
                Enum.GetValues(typeof (SupportedBackupType)))
                .Where(t => t != SupportedBackupType.None);

            foreach (var tp in supportedBackupTypes)
            {
                var filesOfTheSameType = objectSequence.Where(o => o.BackupType == tp).ToList();
                BackupFileCatalog.InferEndTimeWhereNotDefinedExplicitly(filesOfTheSameType);
            }
        }
    }
}