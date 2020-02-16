
using System;
using System.IO;
using Microsoft.SqlServer.Management.Smo;

namespace RestoreBackupLib
{
    public interface IBackupItem
    {
        string DatabaseName { get; }

        BackupType BackupType { get; }

        int Position { get; }

        /// <summary>
        ///     Log sequence number of the first log record in the backup set.
        /// </summary>
        decimal FirstLsn { get; }

        /// <summary>
        ///     Log sequence number of the next log record after the backup set.
        /// </summary>
        decimal LastLsn { get; }

        /// <summary>
        ///     Log sequence number of the most recent full database backup.
        ///     DatabaseBackupLSN is the “begin of checkpoint” that is triggered when the backup starts. This LSN will coincide with FirstLSN
        ///     if the backup is taken when the database is idle and no replication is configured.
        /// </summary>
        decimal DatabaseBackupLsn { get; }

        /// <summary>
        ///     Log sequence number of the most recent checkpoint at the time the backup was created.
        ///     The DatabaseBackupLSN value for the differential backup will match its base full database backup CheckpointLSN
        ///     The CheckpointLSN maps to the CheckpointLSN of the first transaction log backup after the differential backup 
        /// </summary>
        decimal CheckpointLsn { get; }

        /// <summary>
        ///     For a single-based differential backup, the value equals the FirstLSN of the differential base; changes with LSNs greater than or equal
        ///     to DifferentialBaseLSN are included in the differential.
        ///     For a multi-based differential, the value is NULL, and the base LSN must be determined at the file level. For more information, see RESTORE FILELISTONLY (Transact-SQL).
        ///     For non-differential backup types, the value is always NULL.
        /// </summary>
        decimal? DifferentialBaseLsn { get; }

        /// <summary>
        ///     For a single-based differential backup, the value is the unique identifier of the differential base.
        ///     For multi-based differentials, the value is NULL, and the differential base must be determined per file.
        ///     For non-differential backup types, the value is NULL
        /// </summary>
        Guid? DifferentialBaseGuid { get; }

        FileInfo FileInfo { get; }

        DateTime BackupStartTime { get; }

        DateTime BackupEndTime { get; }

        bool IsCopyOnly { get; }

        RecoveryModel RecoveryModel { get; set; }
    }
}