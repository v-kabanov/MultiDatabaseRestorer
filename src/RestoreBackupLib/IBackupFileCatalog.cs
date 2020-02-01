// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-10
// Comment		
// **********************************************************************************************/

using System;
using System.Collections.Generic;

namespace RestoreBackupLib
{
    /// <summary>
    ///     Declares functionality to be provided by component managing historical backup file archive.
    /// </summary>
    /// <remarks>
    ///     For practical purposes backups must be split into files based on time they were performed, otherwise restoring from them would take too long
    ///     as well as backing up those backups. Thus there's a need in a catalog which would provide access to backup files, helping find those which
    ///     may contain data of interest. The catalog is based on 1 simple assumption: every file is stamped with time of the start of the period during which
    ///     backups of certain type are written to it. Intersections are not allowed - next file will "start" exactly where previous ended.
    ///     For example, log backups can be written into daily files, each file having start of the day in its name.
    /// </remarks>
    public interface IBackupFileCatalog
    {
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
        IEnumerable<BackupFileInfo> GetReverseBackupSequence(DateTime? pointInTime, SupportedBackupType backupType);

        /// <summary>
        ///     Get open ended sequence in chronological order of log backup files which may contain log backups made after <paramref name="databaseBackupStartTime"/>
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
        IEnumerable<BackupFileInfo> GetLogBackupsSequence(DateTime databaseBackupStartTime);
    }
}