using System;

namespace SteamKitSDK.Core.Exceptions
{
    public class SteamClientAlreadyRunningException : Exception
    {
        public SteamClientAlreadyRunningException() : base("Steam client is already running.")
        {
        }
    }
}