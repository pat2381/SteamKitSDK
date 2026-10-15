using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamDepotAccessDeniedException : SteamAccessDeniedException
    {
        public SteamDepotAccessDeniedException(DepotId depotId) : base($"Access to depot id = {depotId} denied.")
        {
        }
    }
}