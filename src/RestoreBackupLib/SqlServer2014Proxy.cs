// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-05-04
// Comment		
// **********************************************************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.SqlServer.Management.Smo;

namespace RestoreBackupLib
{
    public interface ISqlServerProxy
    {
        List<BackupItem> GetBackupItems(FileInfo file);
    }

    public class SqlServer2014Proxy : ISqlServerProxy
    {
        public SqlServer2014Proxy(Server server)
        {
            Check.DoRequireArgumentNotNull(server, "server");

            Server = server;
        }

        public Server Server { get; }

        public List<BackupItem> GetBackupItems(FileInfo file)
        {
            Check.DoRequireArgumentNotNull(file, "file");

            var restore = new Restore();
            restore.Devices.AddDevice(file.FullName, DeviceType.File);
            var headerTable = restore.ReadBackupHeader(Server);
            var result = new List<BackupItem>(headerTable.Rows.Count);
            foreach (DataRow row in headerTable.Rows)
            {
                result.Add(GetBackupItem(row, file));
            }

            return result;
        }

        private BackupItem GetBackupItem(DataRow row, FileInfo backupFileInfo)
        {
            var result = new BackupItem()
                {
                    DatabaseName = (string)row["DatabaseName"],
                    BackupType = (BackupType)Convert.ToInt32(row["BackupType"]),
                    Position = Convert.ToInt32(row["Position"]),
                    FirstLsn = (decimal)row["FirstLSN"],            // numeric(25,0)
                    LastLsn = (decimal)row["LastLSN"],
                    DatabaseBackupLsn = (decimal)row["DatabaseBackupLSN"],
                    DifferentialBaseLsn = (decimal?)(row["DifferentialBaseLSN"] != DBNull.Value ? row["DifferentialBaseLSN"] : null),
                    CheckpointLsn = Convert.ToDecimal(row["CheckpointLSN"]),
                    DifferentialBaseGuid = (Guid?)(row["DifferentialBaseGUID"] != DBNull.Value ? row["DifferentialBaseGUID"] : null),
                    FileInfo = backupFileInfo,
                    BackupStartTime = (DateTime)row["BackupStartDate"],
                    BackupEndTime = (DateTime)row["BackupFinishDate"],
                    IsCopyOnly = (bool)row["IsCopyOnly"],
                    RecoveryModel = (RecoveryModel)Enum.Parse(typeof(RecoveryModel), (string)row["RecoveryModel"], true)
                };
            return result;
        }
    }
}