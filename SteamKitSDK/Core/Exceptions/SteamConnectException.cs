using System;

namespace SteamKitSDK.Core.Exceptions
{
    public class SteamConnectException : Exception
    {
        public SteamConnectException() : base("Could not connect to Steam within the maximum attempts allowed.")
        {
        }
    }
}