// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2014-11-14
// Comment		
// **********************************************************************************************/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using log4net;

namespace RestoreBackupLib
{
    /// <summary>
    ///     This enum is aligned with codes returned by SQL Server.
    /// </summary>
    public enum BackupType
    {
        Full = 1,
        Log = 2,
        File = 4,
        DifferentialDatabase = 5,
        DifferentialFile = 6,
        Partial = 7,
        DifferentialPartial = 8
    }

    public class BackupFileHeader
    {
        public BackupFileHeader(FileInfo fileInfo, List<BackupItem> validItems)
        {
            Check.DoRequireArgumentNotNull(fileInfo, "fileInfo");
            Check.DoRequireArgumentNotNull(validItems, "validItems");
            Check.DoCheckArgument(validItems.Count > 0, "No valid backup items");

            FileInfo = fileInfo;
            FirstBackupTime = validItems.First().BackupStartTime;
            LastBackupTime = validItems[validItems.Count - 1].BackupStartTime;
            ValidItems = new List<BackupItem>(validItems);
        }

        public FileInfo FileInfo { get; private set; }
        public DateTime FirstBackupTime { get; private set; }
        public DateTime LastBackupTime { get; private set; }
        public IEnumerable<BackupItem> ValidItems { get; private set; }
    }

    public class MultiRestorer
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // only those which have something to restore will be placed here
        private readonly List<DatabaseRestorer> _databaseRestorers = new List<DatabaseRestorer>();
        private readonly List<string> _databasesToDrop = new List<string>(); 
        private bool _isPrepared;
        private readonly BackgroundWorker _worker;

        public Server Server { get; }

        /// <param name="rootPath">
        ///     Root path under which per-database backup folders are found; the folder names must match database names exactly.
        /// </param>
        /// <param name="targetTime">
        ///     Optional, point in time to restore to; latest if null
        /// </param>
        /// <param name="worker">
        ///     Optional, to report progress to and implement cancellation from.
        /// </param>
        /// <param name="targetServer">
        ///     Self-explanatory
        /// </param>
        /// <param name="namingConvention">
        ///     Defines historical backup directory structure.
        /// </param>
        public MultiRestorer(string rootPath, DateTime? targetTime, BackgroundWorker worker, string targetServer, IBackupDirectoryNamingConvention namingConvention)
        {
            Check.DoRequireArgumentNotNull(rootPath, "rootPath");
            Check.DoRequireArgumentNotNull(targetServer, "targetServer");
            Check.DoRequireArgumentNotNull(namingConvention, "namingConvention");

            NamingConvention = namingConvention;
            RootPath = rootPath;
            TargetTime = targetTime;
            _worker = worker;

            var bld = new SqlConnectionStringBuilder()
            {
                IntegratedSecurity = true,
                DataSource = targetServer,
                InitialCatalog = "master",
                ConnectTimeout = 2,              // seconds;
            };

            // keep same connection to be able to use single user mode for databases when restoring
            var conn = new ServerConnection(new SqlConnection(bld.ToString()));
            Server = new Server(conn);
            Server.ConnectionContext.StatementTimeout = 0;

            // read actual names
            var existingLogins = string.Join(", ", Server.Logins.Cast<Login>().Select(x => x.Name));
            Log.InfoFormat("Existing logins: {0}", existingLogins);
        }

        public IBackupDirectoryNamingConvention NamingConvention { get; set; }

        /// <summary>
        ///     Identifies series of split databases. Contains beginning of their names to which integer index is appended.
        /// </summary>
        /// <remarks>
        ///     Splitting is used to overcome size restrictions in eg Express edition. When size approaches limit, new database with incremented index in
        ///     the name suffix is created and all new data goes there. When restoring to a point before new database is created, such trailing databases
        ///     will be dropped.
        /// </remarks>
        public string[] SplitDatabaseBaseNames;

        /// <summary>
        ///     Require
        /// </summary>
        public bool RequireAllSplitDatabaseRestore;

        public DateTime? TargetTime
        {
            get;
            private set;
        }

        public string RootPath { get; }

        /// <summary>
        ///     Are we restoring the latest available version?
        /// </summary>
        public bool IsLatest => !TargetTime.HasValue;

        public bool IsPrepared
        {
            get => _isPrepared;
            set
            {
                if (!value)
                {
                    _databaseRestorers.Clear();
                    _databasesToDrop.Clear();
                }
                _isPrepared = value;
            }
        }

        public void Prepare()
        {
            IsPrepared = false;

            var dirInfo = new DirectoryInfo(RootPath);

            var databaseFolders = dirInfo.EnumerateDirectories().ToList();

            foreach (var folder in databaseFolders)
            {
                Log.DebugFormat("Scanning {0}", folder.FullName);
                var catalog = new BackupFileCatalog(NamingConvention, folder.FullName);
                var restorer = new DatabaseRestorer(catalog, folder.Name, this);
                restorer.Prepare();
                if (restorer.HaveItemsToRestore)
                {
                    Log.Debug("Restore plan created");
                    _databaseRestorers.Add(restorer);
                }
                else
                {
                    Log.Debug("Nothing to restore here");
                }
            }

            FindDatabasesToBeDropped();

            IsPrepared = true;
        }

        public bool Restore(bool rebindUsersWithSqlLogins = false)
        {
            if (!IsPrepared)
            {
                Prepare();
            }
            Check.Ensure(IsPrepared);

            try
            {
                for (var n = 0; n < _databaseRestorers.Count; ++n)
                {
                    _databaseRestorers[n].Restore();
                    if (rebindUsersWithSqlLogins)
                        _databaseRestorers[n].ReBindDatabaseUsersWithSqlLogins();
                    if (_worker != null)
                    {
                        _worker.ReportProgress((n + 1) * 100 / _databaseRestorers.Count);
                        if (_worker.CancellationPending)
                        {
                            Log.Warn("Cancelled");
                            return false;
                        }
                    }
                }

                foreach (var name in _databasesToDrop)
                {
                    DropDatabase(name);
                }

                return true;
            }
            catch (Exception e)
            {
                Log.ErrorFormat("Error when restoring: {0}", e);
                throw;
            }
            finally
            {
                IsPrepared = false;
            }
        }

        private void DropDatabase(string name)
        {
            var database = Server.Databases[name];
            if (database == null)
            {
                Log.WarnFormat("Database {0} marked for being dropped does not exist", name);
            }
            else
            {
                Log.InfoFormat("Dropping database {0}", database.Name);
                database.DatabaseOptions.UserAccess = DatabaseUserAccess.Single;
                database.Alter(TerminationClause.RollbackTransactionsImmediately);
                database.Drop();
            }
        }

        /// <remarks>
        ///     Checks continuity of split databases to be restored in series. E.g. if there are Documents1 ... Documents10,
        ///     consecutive restores should be prepared, e.g. Documents5 ... Documents8 (5,6,7,8). In this case 9,10 will be dropped.
        ///     If there is a gap (e.g. 5,7,8), exception is raised. Also, exception is raised when no restore is prepared for a series.
        ///     Databases 1,2,3,4 will not be dropped.
        /// </remarks>
        private void FindDatabasesToBeDropped()
        {
            if (SplitDatabaseBaseNames == null)
            {
                return;
            }

            foreach (var nameBase in SplitDatabaseBaseNames)
            {
                var restorers = _databaseRestorers.Where(r => r.DatabaseName.StartsWith(nameBase, StringComparison.InvariantCultureIgnoreCase) &&
                    IsInteger(r.DatabaseName.Substring(nameBase.Length))).ToList();
                
                Check.DoAssertLambda(restorers.Count > 0 || !RequireAllSplitDatabaseRestore, () => string.Format("No backup found to restore database series {0}", nameBase));

                if (restorers.Count > 0)
                {
                    var restorersWithDatabaseIndexes = restorers.Select(r => new { DatabaseIndex = int.Parse(r.DatabaseName.Substring(nameBase.Length)), Restorer = r })
                        .OrderBy(o => o.DatabaseIndex).ToList();

                    var restorersWithSequenceIndexes = Enumerable.Range(0, restorersWithDatabaseIndexes.Count)
                        .Select(n => new
                        {
                            SequenceIndex = n,
                            DatabaseIndex = restorersWithDatabaseIndexes[n].DatabaseIndex,
                            Restorer = restorersWithDatabaseIndexes[n].Restorer
                        }).ToList();

                    var nonSequentialPairs = from r1 in restorersWithSequenceIndexes
                                             from r2 in restorersWithSequenceIndexes
                                             where r1.SequenceIndex + 1 == r2.SequenceIndex &&
                                                   r1.DatabaseIndex + 1 != r2.DatabaseIndex
                                             select new { PreviousRestorer = r1, NextRestorer = r2 };

                    var firstNonSequentialPair = nonSequentialPairs.FirstOrDefault();

                    Check.DoAssertLambda(firstNonSequentialPair == null, () => string.Format("Gap in database series {0}, #{1} follows #{2}"
                        , nameBase
                        , firstNonSequentialPair.PreviousRestorer.DatabaseIndex
                        , firstNonSequentialPair.NextRestorer.DatabaseIndex));

                    var maxRestoredIndex = restorersWithSequenceIndexes.Last().DatabaseIndex;

                    _databasesToDrop.AddRange(GetDatabasesFromSeriesAfterIndex(nameBase, maxRestoredIndex));
                }
            }
        }

        private List<string> GetDatabasesFromSeriesAfterIndex(string nameBase, int databaseIndex)
        {
            return Enumerable.Cast<Database>(Server.Databases)
                .Where(d => d.Name.StartsWith(nameBase, StringComparison.InvariantCultureIgnoreCase) && IsInteger(d.Name.Substring(nameBase.Length)))
                .Select(d => new {Name = d.Name, DatabaseIndex = int.Parse(d.Name.Substring(nameBase.Length))})
                .Where(o => o.DatabaseIndex > databaseIndex)
                .Select(o => o.Name)
                .ToList();
        }

        private bool IsInteger(string value)
        {
            return int.TryParse(value, out var val);
        }
    }
}
