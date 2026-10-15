using System;
using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamBranchNotFoundException : Exception
    {
        public SteamBranchNotFoundException(AppId appId, string branch) : base(
            $"Branch {branch} could not be found for app id = {appId}")
        {
        }
    }
}