using System;
using System.IO;
using Microsoft.SqlServer.Management.Smo;

namespace RestoreBackupLib
{
    public class BackupItem
    {
        public string DatabaseName { get; set; }

        public BackupType BackupType { get; set; }

        public int Position { get; set; }

        /// <summary>
        ///     Log sequence number of the first log record in the backup set.
        /// </summary>
        public decimal FirstLsn { get; set; }

        /// <summary>
        ///     Log sequence number of the next log record after the backup set.
        /// </summary>
        public decimal LastLsn { get; set; }

        /// <summary>
        ///     Log sequence number of the most recent full database backup.
        ///     DatabaseBackupLSN is the “begin of checkpoint” that is triggered when the backup starts. This LSN will coincide with FirstLSN
        ///     if the backup is taken when the database is idle and no replication is configured.
        /// </summary>
        public decimal DatabaseBackupLsn { get; set; }

        /// <summary>
        ///     Log sequence number of the most recent checkpoint at the time the backup was created.
        ///     The DatabaseBackupLSN value for the differential backup will match its base full database backup CheckpointLSN
        ///     The CheckpointLSN maps to the CheckpointLSN of the first transaction log backup after the differential backup 
        /// </summary>
        public decimal CheckpointLsn { get; set; }

        /// <summary>
        ///     For a single-based differential backup, the value equals the FirstLSN of the differential base; changes with LSNs greater than or equal
        ///     to DifferentialBaseLSN are included in the differential.
        ///     For a multi-based differential, the value is NULL, and the base LSN must be determined at the file level. For more information, see RESTORE FILELISTONLY (Transact-SQL).
        ///     For non-differential backup types, the value is always NULL.
        /// </summary>
        public decimal? DifferentialBaseLsn { get; set; }

        /// <summary>
        ///     For a single-based differential backup, the value is the unique identifier of the differential base.
        ///     For multi-based differentials, the value is NULL, and the differential base must be determined per file.
        ///     For non-differential backup types, the value is NULL
        /// </summary>
        public Guid? DifferentialBaseGuid { get; set; }

        public FileInfo FileInfo { get; set; }

        public DateTime BackupStartTime { get; set; }

        public DateTime BackupEndTime { get; set; }

        public bool IsCopyOnly { get; set; }

        public RecoveryModel RecoveryModel { get; set; }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        public override string ToString()
        {
            return $"{BackupType}:{FileInfo.Name}#{Position}";
        }
    }
}