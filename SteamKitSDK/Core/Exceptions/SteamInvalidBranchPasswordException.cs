using System;
using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
#nullable enable

    public class SteamInvalidBranchPasswordException : Exception
    {
        public SteamInvalidBranchPasswordException(
            AppId appId,
            DepotId depotId,
            string branch,
            string password,
            Exception? innerException = null) : base("Invalid password for branch = " +
                                                    $"{branch} of app id = {appId}, depot id = {depotId}{(innerException != null ? ". View the inner exception for more details." : "")}",
            innerException)
        {
            _ = password;
        }
    }
}
