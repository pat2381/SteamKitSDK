using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamAppAccessTokenDeniedException : SteamAccessDeniedException
    {
        public SteamAppAccessTokenDeniedException(AppId appId) : base(
            $"Could not retrieve an access token for app id {appId}.")
        {
        }
    }
}
