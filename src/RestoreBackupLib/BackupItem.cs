using System;
using System.IO;
using Microsoft.SqlServer.Management.Smo;

namespace RestoreBackupLib
{
    public class BackupItem : IBackupItem
    {
        /// <inheritdoc />
        public string DatabaseName { get; set; }

        /// <inheritdoc />
        public BackupType BackupType { get; set; }

        /// <inheritdoc />
        public int Position { get; set; }

        /// <inheritdoc />
        public decimal FirstLsn { get; set; }

        /// <inheritdoc />
        public decimal LastLsn { get; set; }

        /// <inheritdoc />
        public decimal DatabaseBackupLsn { get; set; }

        /// <inheritdoc />
        public decimal CheckpointLsn { get; set; }

        /// <inheritdoc />
        public decimal? DifferentialBaseLsn { get; set; }

        /// <inheritdoc />
        public Guid? DifferentialBaseGuid { get; set; }

        /// <inheritdoc />
        public FileInfo FileInfo { get; set; }

        /// <inheritdoc />
        public DateTime BackupStartTime { get; set; }

        /// <inheritdoc />
        public DateTime BackupEndTime { get; set; }

        /// <inheritdoc />
        public bool IsCopyOnly { get; set; }

        /// <inheritdoc />
        public RecoveryModel RecoveryModel { get; set; }

        public override string ToString()
        {
            return $"{BackupType}:{FileInfo.Name}#{Position}";
        }
    }
}