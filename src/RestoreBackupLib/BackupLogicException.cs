// /**********************************************************************************************
// Author:  Vasily Kabanov
// Created  2020-01-24
// Comment
// **********************************************************************************************/

using System;
using System.Runtime.Serialization;

namespace RestoreBackupLib
{
    public class BackupLogicException : Exception
    {
        public BackupLogicException()
        {
        }

        public BackupLogicException(string message) : base(message)
        {
        }

        public BackupLogicException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected BackupLogicException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}