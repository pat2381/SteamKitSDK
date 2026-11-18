using System;
using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamNoContentServerFoundException : Exception
    {
        public SteamNoContentServerFoundException(AppId appId) : base(
            $"No content servers could be found for app id = {appId}.")
        {
        }

        internal SteamNoContentServerFoundException(AppId appId, Exception innerException)
            : base($"No Steam content server became available for app {appId}.", innerException)
        {
        }
    }
}
