// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-07
// Comment		
// **********************************************************************************************/

using System;
using System.IO;

namespace RestoreBackupLib
{
    public class BackupFileInfo : IBackupFilesystemObjectInfo
    {
        /// <summary>
        ///     Constructor of immutable instance.
        /// </summary>
        /// <param name="backupType">
        ///     Type of backups contained in this file.
        /// </param>
        /// <param name="startTime">
        ///     Explicitly defined start of the period in which backups were written into the file. If null, the start time will be taken from creation time.
        /// </param>
        /// <param name="endTime">
        ///     Explicitly defined end of the period in which backups were written into the file.
        /// </param>
        /// <param name="fileInfo">
        /// </param>
        /// <remarks>
        ///     Note that <see cref="StartTime"/> will always have a value while <see cref="EndTime"/> may be null. This is because start time is more important and
        ///     must be figured out in any case while EndTime is not essential and only beneficial for optimization.
        ///     If simultaneous log and database backups are allowed, without end time we only need to start from first log prior to last database backup.
        ///     However, if there is no simultaneous backup, things are much easier and we can work without end time.
        ///     What happens if diff backup is marked with the hour when it is made and single daily log backup is marked with start of day timestamp?
        ///     Then we need end time or have to start from first log backup file prior to the database backup.
        /// </remarks>
        public BackupFileInfo(SupportedBackupType backupType, DateTime? startTime, DateTime? endTime, FileInfo fileInfo)
        {
            Check.DoRequireArgumentNotNull(fileInfo, "fileInfo");
            Check.DoCheckArgument(backupType != SupportedBackupType.None, "Invalid file type");

            BackupType = backupType;

            FileInfo = fileInfo;

            PeriodStartDeclaredExplicitly = startTime.HasValue;
            StartTime = startTime ?? fileInfo.CreationTime;

            PeriodEndDeclaredExplicitly = endTime.HasValue;
            EndTime = endTime ?? fileInfo.LastWriteTime;
        }

        /// <summary>
        ///     The start of the period in which backups were written into the file.
        /// </summary>
        public DateTime StartTime { get; private set; }

        /// <summary>
        ///     End of the period during which backups could be written to the file if know via e.g. naming convention.
        ///     End time may be set later to e.g. start time of the next file.
        /// </summary>
        public DateTime EndTime { get; set; }

        public FileInfo FileInfo { get; private set; }

        /// <summary>
        ///     Get type of backups contained in this file.
        /// </summary>
        /// <remarks>
        ///     The reason for the constraint is that experience shows that excessive IO results when you put many backups in the same file.
        ///     SQL Server appears to read the whole file to list backups in it. Log backups are exoected to be numerous, but compact.
        ///     So they should be kept in a separate file. Full backups are large and they should be put in separate file each, because
        ///     to get info about the following backups in the same file the whole full backup needs to be read, presumably.
        /// </remarks>
        public SupportedBackupType BackupType { get; private set; }

        public bool PeriodStartDeclaredExplicitly { get; private set; }

        public bool PeriodEndDeclaredExplicitly { get; private set; }

        public FileSystemInfo FileSystemInfo => FileInfo;

        public bool IsFull => BackupType == SupportedBackupType.Full;

        public bool IsDiff => BackupType == SupportedBackupType.Diff;

        public bool IsDatabase => IsFull || IsDiff;

        /// <summary>
        ///     Whether this represents log backup file
        /// </summary>
        public bool IsLog => BackupType == SupportedBackupType.Log;
    }
}