using System;
using SteamKitSDK.ContentDelivery.Models.Downloading;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamDownloadTaskFileTargetWrappedException : Exception
    {
        public FileTarget ExceptionProducer { get; }

        public SteamDownloadTaskFileTargetWrappedException(FileTarget exceptionProducer, Exception innerException)
            : this(exceptionProducer, "The download file target could not be written.", innerException)
        {
        }

        internal SteamDownloadTaskFileTargetWrappedException(FileTarget exceptionProducer, string message, Exception innerException)
            : base(message, innerException) =>
            ExceptionProducer = exceptionProducer;
    }
}
