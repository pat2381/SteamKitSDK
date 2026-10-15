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
    }
}
