using System;

namespace SteamKitSDK.Core.Exceptions
{
    public class SteamNoCmServerFoundException : Exception
    {
        public SteamNoCmServerFoundException() : base(
            "Could not connect to a CM server within the maximum attempts allowed. Maybe Steam is offline for maintenance?")
        {
        }
    }
}