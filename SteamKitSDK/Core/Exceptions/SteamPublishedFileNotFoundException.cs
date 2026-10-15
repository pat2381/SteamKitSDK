using System;
using SteamKitSDK.Core.Structs;

namespace SteamKitSDK.ContentDelivery.Exceptions
{
    public class SteamPublishedFileNotFoundException : Exception
    {
        public SteamPublishedFileNotFoundException(PublishedFileId publishedFileId) : base(
            $"Workshop item {publishedFileId} not found.")
        {
        }
    }
}
