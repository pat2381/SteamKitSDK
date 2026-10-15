using SteamKit2;
using SteamKit2.Discovery;

namespace SteamKitSDK.Core.Regional
{
    public interface IRegionSpecificServerListProvider : IServerListProvider
    {
        void SetSteamConfiguration(SteamConfiguration steamConfiguration);
    }
}