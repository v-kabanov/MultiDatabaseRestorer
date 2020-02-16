using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using log4net;
using log4net.Core;

namespace RestoreBackupLib
{
    public enum Status
    {
        Success,
        Warning,
        Error
    }


    /// <remarks>
    ///     Allows modifications because technical success could be logical warning or error, such as when last restored backup is too old.
    /// </remarks>>
    public class DatabaseRestorationSummary
    {
        public DatabaseRestorationSummary()
        {
        }

        public DatabaseRestorationSummary(string databaseName, Status status, IBackupItem lastRestoredBackup, string message)
        {
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(databaseName));

            if (status != Status.Error && lastRestoredBackup == null)
                throw new ArgumentException("Last restored backup not provided");

            DatabaseName = databaseName;
            Status = status;
            LastRestoredBackup = lastRestoredBackup;
            Message = message;
        }

        public string DatabaseName { get; set; }

        public Status Status { get; set; }

        public IBackupItem LastRestoredBackup { get; set; }

        public string Message { get; set; }
    }

    public class RestorationSummary
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        ///     Top level status not associated with a particular database (e.g. could be an error accessing backup file server).
        /// </summary>
        public Status Status { get; set; }

        public string StatusMessage { get; set; }

        public IList<DatabaseRestorationSummary> DatabaseSummaries { get; } = new List<DatabaseRestorationSummary>();

        public void Add(DatabaseRestorationSummary summary)
        {
            if (DatabaseSummaries.Any(x => x.DatabaseName == summary.DatabaseName))
                throw new ArgumentException($"Summary for {summary.DatabaseName} already added.");

            DatabaseSummaries.Add(summary);
        }

        public void AddSuccess(string databaseName, IBackupItem lastBackupItem, string message = null)
            => Add(new DatabaseRestorationSummary(databaseName, Status.Success, lastBackupItem, message));

        public void AddWarning(string databaseName, IBackupItem lastBackupItem, string message)
            => Add(new DatabaseRestorationSummary(databaseName, Status.Warning, lastBackupItem, message));

        public void AddFailure(string databaseName, string message, IBackupItem lastBackupItem = null)
            => Add(new DatabaseRestorationSummary(databaseName, Status.Error, lastBackupItem, message));

        public bool IsFailure => Status == Status.Error || DatabaseSummaries.Any(x => x.Status == Status.Error);

        /// <summary>
        ///     Maximum severity of all included statuses.
        /// </summary>
        public Status MaxSeverity => DatabaseSummaries.Select(x => x.Status)
            .Concat(Enumerable.Repeat(Status, 1))
            .Max();

        public void LogSummary()
        {
            var level = GetLogLevel(MaxSeverity);

            if (!Log.Logger.IsEnabledFor(level))
                return;

            var stringBuilder = new StringBuilder(2048)
                .AppendLine(StatusMessage)
                .AppendLine();

            foreach (var summary in DatabaseSummaries.OrderByDescending(x => x.Status))
            {
                stringBuilder.AppendLine(summary.DatabaseName)
                    .Append(summary.Status.ToString()).Append(": ").AppendLine(summary.Message);
                if (summary.LastRestoredBackup != null)
                {
                    var lrb = summary.LastRestoredBackup;
                    stringBuilder.Append("Last restored backup: ")
                        .Append(lrb).Append(" started: ").Append(lrb.BackupStartTime).Append(", finished: ").Append(lrb.BackupEndTime)
                        .AppendLine()
                        .AppendLine();
                }
            }

            Log.Logger.Log(GetType(), level, stringBuilder.ToString(), null);
        }

        public void LogSummary(string summary, Status status)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));

            var level = GetLogLevel(status);

            if (!Log.Logger.IsEnabledFor(level))
                return;

            Log.Logger.Log(GetType(), level, summary, null);
        }

        private static Level GetLogLevel(Status status)
            => status == Status.Success
                ? Level.Info
                : status == Status.Warning
                    ? Level.Warn
                    : Level.Error;
    }
}