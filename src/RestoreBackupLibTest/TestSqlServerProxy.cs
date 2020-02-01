// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-05-22
// Comment		
// **********************************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using RestoreBackupLib;

namespace RestoreBackupLibTest
{
    public class TestSqlServerProxy : ISqlServerProxy
    {
        public static readonly TimeSpan FirstBackupTime     = new TimeSpan(8, 0, 0);
        public static readonly TimeSpan FirstLogBackupTime  = new TimeSpan(8, 5, 0);
        public static readonly TimeSpan LastBackupTime      = new TimeSpan(20, 0, 0);
        public const int BackupIntervalMinutes = 5;
        // time from start of backup to point when data has been read from data files; that is the point to which db will be restored
        public const int FullBackupDurationSeconds = 25;
        public const int DiffBackupDurationSeconds = 10;
        public const int LogBackupDurationSeconds = 2;

        public static readonly int LogBackupsPerDay = (int)(LastBackupTime - FirstBackupTime).TotalMinutes / BackupIntervalMinutes;

        public TestSqlServerProxy(IBackupFileNamingConvention fileNamingConvention, string databaseName)
        {
            Check.DoRequireArgumentNotNull(fileNamingConvention, "fileNamingConvention");
            Check.DoRequireArgumentNotNull(databaseName, "databaseName");

            FileNamingConvention = fileNamingConvention;
            DatabaseName = databaseName;
        }

        public IBackupFileNamingConvention FileNamingConvention { get; }

        public string DatabaseName { get; }

        /// <remarks>
        ///     Full backup action sequence, from https://technet.microsoft.com/en-us/magazine/2009.07.sqlbackup.aspx :
        ///         1. Force a database checkpoint and make a note of the log sequence number at this point. This flushes all updated-in-memory pages to disk
        ///             before anything is read by the backup to help minimize the amount of work the recovery part of restore has to do.
        ///         2. Start reading from the data files in the database.
        ///         3. Stop reading from the data files and make a note of the log sequence number of the start of the oldest active transaction at that point
        ///             (see my article "Understanding Logging and Recovery in SQL Server" for an explanation of these terms).
        ///         4. Read as much transaction log as is necessary.
        ///     ...
        ///         Backing up enough of the transaction log is required so that recovery can successfully run during the restore and so that all pages in
        ///         the database are at the same point in time—the time at which the data reading portion of the backup operation completed (Point 7).
        /// </remarks>
        public List<BackupItem> GetBackupItems(FileInfo file)
        {
            var fileInfo = FileNamingConvention.GetBackupFileInfo(file);

            List<BackupItem> result;

            if (fileInfo.IsLog)
            {
                // first backup in a day is diff or full

                result = new List<BackupItem>(LogBackupsPerDay);

                for (var pos = 1; pos <= LogBackupsPerDay; ++pos)
                {
                    var backupTime = fileInfo.StartTime.Date.Add(FirstBackupTime).AddMinutes(pos * BackupIntervalMinutes);

                    var item = new BackupItem()
                        {
                            BackupEndTime = backupTime.AddSeconds(LogBackupDurationSeconds),
                            BackupStartTime = backupTime,
                            BackupType = BackupType.Log,
                            DatabaseName = DatabaseName,
                            DatabaseBackupLsn = GetDatabaseBackupLsn(backupTime),
                            Position = pos,
                            FirstLsn = GetLogBackupFirstLsn(backupTime),
                            LastLsn = GetLogBackupLastLsn(backupTime),
                            RecoveryModel = Microsoft.SqlServer.Management.Smo.RecoveryModel.Full,
                            FileInfo = file
                        };

                    item.CheckpointLsn = item.FirstLsn;
                    result.Add(item);
                }
            }
            else
            {
                var backupTime = fileInfo.StartTime.Date.Add(FirstBackupTime);
                var backupEndTime = backupTime.AddSeconds(fileInfo.IsFull ? FullBackupDurationSeconds : DiffBackupDurationSeconds);

                var item = new BackupItem()
                    {
                        BackupEndTime = backupEndTime,
                        BackupStartTime = backupTime,
                        BackupType = fileInfo.IsFull ? BackupType.Full : BackupType.DifferentialDatabase,
                        DatabaseName = DatabaseName,
                        DatabaseBackupLsn = GetDatabaseBackupLsn(backupTime),
                        Position = 1,
                        FirstLsn = GetLastLsn(backupTime),
                        LastLsn = GetLastLsn(backupEndTime),
                        RecoveryModel = Microsoft.SqlServer.Management.Smo.RecoveryModel.Full,
                        FileInfo = file
                    };
                item.CheckpointLsn = item.FirstLsn;

                result = new List<BackupItem>(1);
                result.Add(item);
            }

            return result;
        }

        /// <summary>
        ///     Get LSN of the transaction last committed in the imaginary database at the specified point in time.
        /// </summary>
        /// <param name="time">
        /// </param>
        /// <returns></returns>
        /// <remarks>
        ///     Emulates database in which an instanteneous transaction is committed every 1 second around the clock.
        ///     Transactions take place exactly between second boundaries.
        ///     Checkpoint is instantaneous, so firstLSN will always be equal to checkpoint lsn in full backups.
        /// </remarks>
        public static decimal GetLastLsn(DateTime time)
        {
            return (decimal)(int)(time - TestSession.StartDate).TotalSeconds;
        }

        public static decimal GetLogBackupFirstLsn(DateTime logBackupStartTime)
        {
            if (logBackupStartTime.TimeOfDay == FirstLogBackupTime)
            {
                // all transactions since last log backup from the previous day
                if (logBackupStartTime.Date == TestSession.StartDate)
                {
                    // The FirstLSN and CheckpointLSN of the first transaction log backup is also the first full database backup CheckpointLSN
                    // if the backup is taken when the database is idle and no replication is configured 
                    return GetDatabaseBackupLsn(logBackupStartTime);
                }

                var previousLogBackupStartTime = logBackupStartTime.Date.AddDays(-1).Add(LastBackupTime);
                return GetLogBackupLastLsn(previousLogBackupStartTime);
            }

            return GetLogBackupLastLsn(logBackupStartTime.AddMinutes(-BackupIntervalMinutes));
        }

        private static decimal GetLogBackupLastLsn(DateTime logBackupStartTime)
        {
            return GetLastLsn(logBackupStartTime.AddSeconds(LogBackupDurationSeconds));
        }

        public static decimal GetDatabaseBackupLsn(DateTime pointInTime)
        {
            var fullBackupTime = new DateTime(pointInTime.Year, pointInTime.Month, 1).Add(FirstBackupTime);
            return GetLastLsn(fullBackupTime);
        }
    }
}