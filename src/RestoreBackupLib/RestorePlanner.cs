// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-28
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
    public class BackupItemReference
    {
        public BackupItem BackupItem { get; set; }
        public BackupFileHeader FileHeader { get; set; }
    }

    public interface IRestorePlanner
    {
        List<BackupItem> CreatePlan(DateTime? pointInTime);
    }

    public class RestorePlanner : IRestorePlanner
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public RestorePlanner(IBackupFileCatalog backupFileCatalog, ISqlServerProxy sqlServerProxy, string databaseName)
        {
            Check.DoRequireArgumentNotNull(backupFileCatalog, "backupFileCatalog");
            Check.DoRequireArgumentNotNull(sqlServerProxy, "sqlServerProxy");
            Check.DoRequireArgumentNotNull(databaseName, "databaseName");

            BackupFileCatalog = backupFileCatalog;
            SqlServerProxy = sqlServerProxy;
            DatabaseName = databaseName;
        }

        public IBackupFileCatalog BackupFileCatalog { get; }

        public ISqlServerProxy SqlServerProxy { get; }

        public string DatabaseName { get; }

        /// <summary>
        ///     Supports out of line full backups finding diff backup compatible with last existing full backup.
        ///     Log backup sequence must not be broken.
        /// </summary>
        /// <param name="pointInTime"></param>
        /// <returns></returns>
        public List<BackupItem> CreatePlan(DateTime? pointInTime)
        {
            var result = new List<BackupItem>(500);

            var fullAndDiffBackups = GetNonLogBackupItems(pointInTime);
            var logBackups = GetLogBackupItems(fullAndDiffBackups.Last(), pointInTime);

            Log.InfoFormat("Found {0} database backups and {1} log backups to restore for database {2}"
                            , fullAndDiffBackups.Count, logBackups.Count, DatabaseName);

            result.AddRange(fullAndDiffBackups);

            if (logBackups.Count > 0)
            {
                CheckSequence(fullAndDiffBackups.Last(), logBackups.First(), true);
            }
 
            result.AddRange(logBackups);

            if (result.Count == 0)
            {
                Log.WarnFormat("No backups found for {0} at {1}", DatabaseName, pointInTime);
            }

            return result;
        }

        /// <summary>
        ///     Get database (full and diff) backups to start restoring to achieve configured outcome,
        ///     in correct and verified order.
        ///     Returns empty list if there are no suitable database backups to restore.
        /// </summary>
        /// <returns>
        ///     never null
        /// </returns>
        private List<BackupItem> GetNonLogBackupItems(DateTime? targetTime)
        {
            // find last suitable full backup; no scanning further for full backups because only valid backups are supported by automatic restore, otherwise
            // dangerous
            var fullItem = GetReverseBackupSequence(null, targetTime, SupportedBackupType.Full).FirstOrDefault();

            var result = new List<BackupItem>(2);

            if (fullItem != null)
            {
                result.Add(fullItem.BackupItem);

                var candidateDiffBackups = GetReverseBackupSequence(fullItem.BackupItem.BackupEndTime, targetTime, SupportedBackupType.Diff);

                foreach (var candidateDiffBackup in candidateDiffBackups)
                {
                    if (CheckSequence(fullItem.BackupItem, candidateDiffBackup.BackupItem, false))
                    {
                        result.Add(candidateDiffBackup.BackupItem);
                        break;
                    }
                }
            }

            return result;
        }

        private IEnumerable<BackupItemReference> GetReverseBackupSequence(
            DateTime? minBackupEndTime, DateTime? restoreTargetTime, SupportedBackupType backupType)
        {
            var reverseFileSequence = BackupFileCatalog.GetReverseBackupSequence(restoreTargetTime, backupType);

            return GetReverseBackupSequence(minBackupEndTime, restoreTargetTime, reverseFileSequence);
        }

        private IEnumerable<BackupItemReference> GetReverseBackupSequence(
            DateTime? minBackupEndTime, DateTime? restoreTargetTime, IEnumerable<BackupFileInfo> reverseBackupFileSequence)
        {
            BackupItemReference result = null;
            var sequence = reverseBackupFileSequence.GetEnumerator();
            while (sequence.MoveNext() && result == null)
            {
                Check.DoCheckOperationValid(sequence.Current.IsDatabase);
                var header = ParseBackupHeader(sequence.Current.FileInfo);

                var suitableItems = header.ValidItems
                                 .OrderByDescending(i => i.BackupEndTime)
                                 .Where(b => !restoreTargetTime.HasValue || b.BackupEndTime < restoreTargetTime.Value)
                                 .Where(b => !minBackupEndTime.HasValue || b.BackupEndTime >= minBackupEndTime.Value);

                foreach (var item in suitableItems)
                {
                    yield return new BackupItemReference() { BackupItem = item, FileHeader = header };
                }
            }
        }

        /// <summary>
        ///     Verified sequence of backup items to restore to achieve configured outcome.
        ///     Never returns null.
        /// </summary>
        /// <param name="lastDatabaseBackupItem"></param>
        /// <returns></returns>
        private List<BackupItem> GetLogBackupItems(BackupItem lastDatabaseBackupItem, DateTime? targetTime)
        {
            Check.DoRequireArgumentNotNull(lastDatabaseBackupItem, "lastDatabaseBackupItem");

            var result = new List<BackupItem>(1000);

            var logFiles = BackupFileCatalog.GetLogBackupsSequence(lastDatabaseBackupItem.BackupStartTime);

            // need only first log backup after target time; flag indicates that file is already processed
            var targetReached = false;
            var fileIterator = logFiles.GetEnumerator();

            var lastBackupItem = lastDatabaseBackupItem;

            while (!targetReached && fileIterator.MoveNext())
            {
                var header = ParseBackupHeader(fileIterator.Current.FileInfo);

                if (header != null)
                {
                    var itemIterator = header.ValidItems.GetEnumerator();

                    while (!targetReached && itemIterator.MoveNext())
                    {
                        var item = itemIterator.Current;

                        Check.DoAssertLambda(CheckSequence(lastBackupItem, item, false), () =>
                            $"Item #{item.Position} in file {item.FileInfo.Name} cannot be applied after item #{lastBackupItem.Position} in" +
                            $" file {lastBackupItem.FileInfo.Name}");
                        lastBackupItem = item;

                        result.Add(item);

                        targetReached = (targetTime.HasValue && item.BackupStartTime > targetTime.Value);
                    }
                }
            }

            return result;
        }

        private bool CheckSequence(BackupItem first, BackupItem next, bool raiseException)
        {
            bool result;
            if (first.BackupType == BackupType.Full)
            {
                if (next.BackupType == BackupType.DifferentialDatabase)
                {
                    result = Verify(next.DatabaseBackupLsn == first.CheckpointLsn, raiseException
                        , $"The differential backup {next} cannot be applied after the full backup {first} due to LSN mismatch: {next.DatabaseBackupLsn}" +
                          $" vs {first.CheckpointLsn}; another full backup was likely taken between the 2.");
                }
                else
                {
                    result =
                        Verify(next.BackupType == BackupType.Log, raiseException
                            , $"Next backup {next} is of unsupported type {next.BackupType} after full {first}")
                        && Verify(next.FirstLsn <= first.LastLsn && next.LastLsn >= first.LastLsn, raiseException
                            , $"The log backup {next} is out of sequence with the full backup {first}");
                }
            }
            else if (first.BackupType == BackupType.DifferentialDatabase)
            {
                result =
                    Verify(next.BackupType == BackupType.Log, raiseException
                        , $"Cannot apply {next.BackupType} type backup after differential")
                    && Verify(next.FirstLsn <= first.LastLsn && next.LastLsn >= first.LastLsn, raiseException
                             , $"The log backup {next} is out of sequence with the diff backup {first}")
                    && Verify(next.DatabaseBackupLsn == first.DatabaseBackupLsn, raiseException
                        , $"The log backup {next} has a different base with the diff backup {first}");
            }
            else
            {
                result =
                    Verify(first.BackupType == BackupType.Log && next.BackupType == BackupType.Log, raiseException
                        , $"Pair {first.BackupType} - {next.BackupType} is not supported")
                    && Verify(first.LastLsn == next.FirstLsn, raiseException
                        , $"Log backups {first.FileInfo.Name}#{first.Position} and {next.FileInfo.Name}#{next.Position} are out of sequence");
            }
            return result;
        }

        private bool Verify(bool result, bool throwException, string message)
        {
            if (!result)
            {
                if (throwException) throw new BackupLogicException(message);

                Log.Warn(message);
            }

            return result;
        }

        /// <returns>
        ///     null if file contains no valid backups
        /// </returns>
        private BackupFileHeader ParseBackupHeader(FileInfo fileInfo)
        {
            Check.DoRequireArgumentNotNull(fileInfo, "fileInfo");

            BackupFileHeader result = null;

            var allBackupItems = SqlServerProxy.GetBackupItems(fileInfo);
            var validItems = GetValidBackups(allBackupItems);
            var wrongItems = GetInvalidBackups(allBackupItems);

            if (wrongItems.Count > 0)
            {
                Log.WarnFormat("File {0} contains {1} backups from database other than {2}; ignoring them", fileInfo.FullName, wrongItems.Count, DatabaseName);
            }

            if (validItems.Count == 0)
            {
                Log.WarnFormat("File {0} contains no backups for database {1}; skipping it.", fileInfo.FullName, DatabaseName);
            }
            else
            {
                result = new BackupFileHeader(fileInfo, validItems);
            }

            return result;
        }

        /// <summary>
        ///     Get backups for the correct database
        /// </summary>
        private List<BackupItem> GetValidBackups(IEnumerable<BackupItem> items)
        {
            return items.Where(i => string.Equals(DatabaseName, i.DatabaseName, StringComparison.InvariantCultureIgnoreCase) && !i.IsCopyOnly).ToList();
        }

        private List<BackupItem> GetInvalidBackups(IEnumerable<BackupItem> items)
        {
            return items.Where(i => !string.Equals(DatabaseName, i.DatabaseName, StringComparison.InvariantCultureIgnoreCase) || i.IsCopyOnly).ToList();
        }
    }
}