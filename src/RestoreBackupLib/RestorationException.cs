using System;

namespace RestoreBackupLib
{
    public class RestorationException : Exception
    {
        /// <summary>
        ///     May be null
        /// </summary>
        public RestorationSummary RestorationSummary { get; set; }

        public RestorationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public RestorationException(RestorationSummary restorationSummary)
        {
            RestorationSummary = restorationSummary;
        }

        public RestorationException(string message, RestorationSummary restorationSummary)
            : base(message)
        {
            RestorationSummary = restorationSummary;
        }

        public RestorationException(string message, Exception innerException, RestorationSummary restorationSummary)
            : base(message, innerException)
        {
            RestorationSummary = restorationSummary;
        }
    }
}