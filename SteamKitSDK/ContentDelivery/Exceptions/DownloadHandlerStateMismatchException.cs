using System;
using SteamKitSDK.ContentDelivery.Models.Downloading;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class DownloadHandlerStateMismatchException : Exception
    {
        public DownloadHandlerStateMismatchException(
            DownloadHandlerStateEnum expectedState,
            DownloadHandlerStateEnum actualState) : base(
            $"Action cannot be performed because download handlers current state '{actualState}' does not match expected state '{expectedState}'")
        {
        }
    }
}