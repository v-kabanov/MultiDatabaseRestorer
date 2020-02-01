// /**********************************************************************************************
// Author:		Vasily Kabanov
// Created		2015-04-13
// Comment		
// **********************************************************************************************/

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RestoreBackupLib
{
    /// <summary>
    ///     Object which parses filesystem object (eg file or directory) name and extracts backup metadata from it.
    /// </summary>
    public interface IBackupFilesystemNameParser
    {
        /// <summary>
        ///     May return null
        /// </summary>
        /// <param name="fileSystemName">
        ///     Filesystem object name, no path
        /// </param>
        /// <returns>
        ///     null if not supported or failed to extract
        /// </returns>
        string GetDatabaseName(string fileSystemName);

        /// <summary>
        ///     May return null
        /// </summary>
        /// <param name="fileSystemName">
        ///     Filesystem object name, no path
        /// </param>
        /// <returns>
        ///     null if not supported or failed to extract
        /// </returns>
        DateTime? GetTimestamp(string fileSystemName);
    }

    /// <summary>
    ///     Generic parser based on capturing regular expression.
    /// </summary>
    public class BackupFilesystemObjectNameParser : IBackupFilesystemNameParser
    {
        private readonly Regex _timestampExtractor;
        private readonly Regex _databaseNameExtractor;
        private readonly string _timestampFormat;

        /// <summary>
        ///     
        /// </summary>
        /// <param name="timestampExtractorExpression">
        ///     Must capture timestamp in the first capture of the first group.
        /// </param>
        /// <param name="timestampFormat">
        /// </param>
        /// <param name="databaseNameExtractorExpression">
        ///     Optional
        /// </param>
        public BackupFilesystemObjectNameParser(string timestampExtractorExpression, string timestampFormat, string databaseNameExtractorExpression)
        {
            Check.DoRequireArgumentNotNull(timestampExtractorExpression, "timestampExtractorExpression");
            Check.DoRequireArgumentNotNull(timestampFormat, "timestampFormat");

            _timestampExtractor = new Regex(timestampExtractorExpression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            _timestampFormat = timestampFormat;

            if (!string.IsNullOrEmpty(databaseNameExtractorExpression))
            {
                _databaseNameExtractor = new Regex(databaseNameExtractorExpression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        public string GetDatabaseName(string fileSystemName)
        {
            if (_databaseNameExtractor != null)
            {
                return Extract(fileSystemName, _databaseNameExtractor);
            }
            return null;
        }

        public DateTime? GetTimestamp(string fileSystemName)
        {
            if (_timestampExtractor != null)
            {
                var asString = Extract(fileSystemName, _timestampExtractor);
                if (!string.IsNullOrEmpty(asString))
                {
                    return ParseTimestamp(asString, _timestampFormat);
                }
            }
            return null;
        }

        /// <summary>
        ///     Parses whole timestamp string (separate) using invariant culture.
        /// </summary>
        /// <returns>
        ///     null if time cannot be extracted
        /// </returns>
        public static DateTime? ParseTimestamp(string timestampString, string timeFormat)
        {
            if (DateTime.TryParseExact(timestampString, timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }
            return null;
        }

        private string Extract(string input, Regex regex)
        {
            Check.DoRequireArgumentNotNull(input, "input");
            Check.DoRequireArgumentNotNull(regex, "regex");

            string result = null;

            var match = regex.Match(input);
            if (match.Success)
            {
                // first occurrence such as when capturing subexpression repeats: @"\b(\w+\s*)+\.";
                return match.Groups[1].Captures[0].Value;
            }

            return null;
        }
    }
}