// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2014-11-19
// Comment		
// **********************************************************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.Management.Smo;
using log4net;

namespace RestoreBackupLib
{
    /// <summary>
    /// 
    /// </summary>
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
    public class DatabaseRestorer
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly string _databaseName;
        private readonly MultiRestorer _multiRestorer;
        private List<BackupItem> _backupItems = new List<BackupItem>();
        private bool _preExistingDatabase;

        private readonly IBackupFileCatalog _backupCatalog;

        /// <summary>
        ///     Last backup successfully restored by <see cref="Restore"/>.
        /// </summary>
        public IBackupItem LastRestoredBackup { get; private set; }

        public bool IsPrepared { get; private set; }

        private class DatabaseFile
        {
            public string LogicalName;
            public string PhysicalName;
            public bool IsLog;
            public long Size;
            public Guid UniqueId;
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="T:System.Object"/> class.
        /// </summary>
        public DatabaseRestorer(IBackupFileCatalog backupCatalog, string databaseName, MultiRestorer multiRestorer)
        {
            Check.DoRequireArgumentNotNull(backupCatalog, "backupCatalog");
            _backupCatalog = backupCatalog;
            _databaseName = databaseName;
            _multiRestorer = multiRestorer;

            _preExistingDatabase = Database != null;
        }

        private DateTime? TargetTime => _multiRestorer.TargetTime;

        /// <summary>
        ///     Whether restorer is prepared and has items to restore.
        /// </summary>
        public bool HaveItemsToRestore => IsPrepared && _backupItems.Count > 0;

        public string DatabaseName
        {
            get
            {
                if (_preExistingDatabase)
                {
                    return Database.Name;
                }
                return _databaseName;
            }
        }

        public Server Server => _multiRestorer.Server;

        public Database Database => _multiRestorer.Server.Databases[_databaseName];

        public List<IBackupItem> GetRestorePlan() => _backupItems.Cast<IBackupItem>().ToList();

        /// <summary>
        ///     Finds and remembers the sequence of backups to restore.
        ///     Throws exception on error. Allows situation when there's no backups to restore, but raises exception when e.g. log backups
        ///     are found but full backup isn't.
        /// </summary>
        public void Prepare()
        {
            IsPrepared = false;
            var sqlServerProxy = new SqlServer2014Proxy(Server);
            var planner = new RestorePlanner(_backupCatalog, sqlServerProxy, _databaseName);
            _backupItems = planner.CreatePlan(TargetTime);

            IsPrepared = true;
        }

        /// <summary>
        ///     May drop database!!
        /// </summary>
        private void ForceRecoveringDatabase()
        {
            try
            {
                Server.ConnectionContext.ExecuteNonQuery($"use master; restore database [{DatabaseName}] with recovery;");
            }
            catch (Exception e)
            {
                Log.ErrorFormat("Recovering database {0} failed: {1}; dropping it", DatabaseName, e);
                Database.Drop();
                _preExistingDatabase = false;
                Server.Databases.Refresh();
            }
        }

        public void Restore()
        {
            Check.DoCheckOperationValid(IsPrepared, "Restorer is not prepared");

            Log.InfoFormat("Restoring {0}, Target Time: {1}"
                , DatabaseName, TargetTime.HasValue ? TargetTime.Value.ToString("yy-MM-dd HH:mm:ss:fff") : "Latest");

            if (HaveItemsToRestore)
            {
                var originalUserAccess = DatabaseUserAccess.Multiple;
                if (_preExistingDatabase)
                {
                    if (Database.Status == DatabaseStatus.Restoring)
                    {
                        ForceRecoveringDatabase();
                    }
                    if (_preExistingDatabase)
                    {
                        originalUserAccess = Database.DatabaseOptions.UserAccess;
                        Database.DatabaseOptions.UserAccess = DatabaseUserAccess.Single;
                        Database.Alter(TerminationClause.RollbackTransactionsImmediately);
                    }
                }

                try
                {
                    foreach (var item in _backupItems)
                    {
                        Restore(item);
                        LastRestoredBackup = item;
                    }

                    Server.ConnectionContext.ExecuteNonQuery($"restore database [{DatabaseName}] with recovery;");
                    Log.InfoFormat("Database {0} restored successfully", DatabaseName);
                }
                catch (Exception e)
                {
                    Log.ErrorFormat("Exception restoring {0}: {1}", _databaseName, e);
                    throw;
                }
                finally
                {
                    if (_preExistingDatabase)
                    {
                        Log.InfoFormat("Setting {0} user access to original value {1}", DatabaseName, originalUserAccess);
                        Database.DatabaseOptions.UserAccess = originalUserAccess;
                        Database.Alter();
                    }
                }
            }
        }

        private Restore CreateRestoreInstance()
        {
            var result = new Restore()
            {
                Database = DatabaseName,
                Action = RestoreActionType.Database,
                NoRecovery = true,
                ReplaceDatabase = true,
            };
            return result;
        }

        private void Restore(BackupItem item)
        {
            Check.DoRequireArgumentNotNull(item, "item");

            Log.InfoFormat("{0}: restoring {1} from {2}", DatabaseName, item, item.FileInfo.FullName);

            var actionType = item.BackupType == BackupType.Log ? RestoreActionType.Log : RestoreActionType.Database;

            var restore = CreateRestoreInstance();

            restore.Action = actionType;
            restore.Devices.AddDevice(item.FileInfo.FullName, DeviceType.File);
            restore.FileNumber = item.Position;

            if (TargetTime.HasValue && item.BackupType == BackupType.Log)
            {
                // note that another format, yyyy-MM-ddTHH:mm:ss.fff failed with error saying "already restored past the point"
                restore.ToPointInTime = TargetTime.Value.ToString("MMM dd, yyyy hh:mm:ss tt");
                Log.InfoFormat("Setting Point In Time {0} on {1}", restore.ToPointInTime, item);
            }

            AddMoveFileInstructionsIfNeeded(item, restore);

            restore.PercentCompleteNotification = 10;
            restore.PercentComplete += RestoreOnPercentComplete;

            try
            {
                Server.ConnectionContext.ExecuteNonQuery("use master;");
                restore.SqlRestore(Server);
            }
            finally 
            {
                restore.PercentComplete -= RestoreOnPercentComplete;
            }
        }

        public void ReBindDatabaseUsersWithSqlLogins()
        {
            for (var i = 0; i < Database.Users.Count; ++i)
            {
                var user = Database.Users[i];
                if (user.IsSystemObject || user.LoginType != LoginType.SqlLogin)
                    continue;

                if (!string.IsNullOrEmpty(user.Login))
                {
                    Log.InfoFormat("User {0} is already mapped to login {1}", user.Name, user.Login);
                    continue;
                }

                var login = Server.Logins[user.Name];
                if (login == null)
                {
                    Log.InfoFormat("No login for orphaned user {0}", user.Name);
                    continue;
                }

                Database.ExecuteNonQuery($"alter user [{user.Name}] with login = [{login.Name}]");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="backupItem"></param>
        /// <param name="restore"></param>
        /// <remarks>
        ///     <paramref name="restore"/> must have backup device already added
        /// </remarks>
        private void AddMoveFileInstructionsIfNeeded(BackupItem backupItem, Restore restore)
        {
            Check.DoRequireArgumentNotNull(restore, "restore");
            Check.DoCheckArgument(restore.Devices.Count > 0, "Restore instance must have backup device already added");

            if (backupItem.BackupType == BackupType.Full)
            {
                var databaseFilesInBackup = ReadDatabaseFileList(restore);
                foreach (var dbFile in databaseFilesInBackup)
                {
                    var targetDir = dbFile.IsLog ? DefaultLogPath : DefaultDataPath;
                    var physicalFilePath = FindExistingDatabaseFile(dbFile.LogicalName, dbFile.IsLog);

                    if (string.IsNullOrEmpty(physicalFilePath))
                    {
                        // no such file in the existing database; maybe it was dropped
                        physicalFilePath = Path.Combine(targetDir, Path.GetFileName(dbFile.PhysicalName));
                        physicalFilePath = PickNewFileName(physicalFilePath);
                    }

                    Log.InfoFormat("Moving {0} to {1}", dbFile.LogicalName, physicalFilePath);

                    restore.RelocateFiles.Add(new RelocateFile(dbFile.LogicalName, physicalFilePath));
                }
            }
        }

        /// <summary>
        ///     Find existing database file in the target <see cref="Database"/> by its logical name and type.
        ///     Supports new database, treats all files as non-existent.
        /// </summary>
        /// <param name="logicalName"></param>
        /// <param name="isLog"></param>
        /// <returns>
        ///     null if file does no exist, including when database itself did not exist before restore started
        /// </returns>
        private string FindExistingDatabaseFile(string logicalName, bool isLog)
        {
            string result = null;
            if (_preExistingDatabase)
            {
                if (isLog)
                {
                    var file = Enumerable.Cast<LogFile>(Database.LogFiles).Where(
                        f => string.Equals(f.Name, logicalName, StringComparison.InvariantCultureIgnoreCase))
                        .FirstOrDefault();
                    if (file != null)
                    {
                        result = file.FileName;
                    }
                }
                else
                {
                    var file = Enumerable.Cast<FileGroup>(Database.FileGroups).Where(g => g.Files.Contains(logicalName)).Select(g => g.Files[logicalName]).FirstOrDefault();
                    if (file != null)
                    {
                        result = file.FileName;
                    }
                }
            }

            return result;
        }

        private List<DatabaseFile> ReadDatabaseFileList(Restore restore)
        {
            return Enumerable.Cast<DataRow>(restore.ReadFileList(Server).Rows).Select(
                r => new DatabaseFile()
                {
                    LogicalName = (string)r["LogicalName"],
                    PhysicalName = (string)r["PhysicalName"],
                    IsLog = "L".Equals((string)r["Type"]),
                    Size = Convert.ToInt64(r["Size"]),
                    UniqueId = (Guid)r["UniqueId"]
                }).ToList();
        }

        private string DefaultDataPath
        {
            get
            {
                var result = Server.DefaultFile;
                if (string.IsNullOrEmpty(result))
                {
                    result = Server.MasterDBPath;
                }
                return result;
            }
        }

        private string DefaultLogPath
        {
            get
            {
                var result = Server.DefaultLog;
                if (string.IsNullOrEmpty(result))
                {
                    result = Server.MasterDBLogPath;
                }
                return result;
            }
        }

        private string PickNewFileName(string fullFilePath)
        {
            var name = Path.GetFileName(fullFilePath);
            var dir = Path.GetDirectoryName(fullFilePath);

            var nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);

            var result = fullFilePath;

            // limiting number of tries to 100; there shouldn't be hundreds of files there
            for (var n = 1; File.Exists(result) && n < 100; ++n)
            {
                var newName = Regex.Replace(nameWithoutExtension, @"(?<=([\D]+|^))\d*$", n.ToString());
                // not using Path.ChangeExtension because name can contain '.' and the extension would replace part of the file name
                result = Path.Combine(dir, string.Format("{0}{1}", newName, extension));
            }

            Check.DoAssertLambda(!File.Exists(result), () => $"Failed to pick nonexistent file name for {fullFilePath}");

            return result;
        }

        private void RestoreOnPercentComplete(object sender, PercentCompleteEventArgs percentCompleteEventArgs)
        {
        }

    }
}