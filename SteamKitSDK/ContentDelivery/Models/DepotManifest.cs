using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Models
{
    public class DepotManifest
    {
        public string BranchName { get; }
        public ManifestId ManifestId { get; }

        public DepotManifest(string branchName, ManifestId manifestId)
        {
            BranchName = branchName;
            ManifestId = manifestId;
        }
    }
}
