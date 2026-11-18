using System;
using System.Threading;

namespace SteamKitSDK.ContentDelivery.Models.Downloading;

internal sealed class DownloadOperation
{
    private const int Idle = 0;
    private const int Busy = 1;
    private const int Disposed = 2;
    private int _state;

    internal Scope Enter()
    {
        switch (Interlocked.CompareExchange(ref _state, Busy, Idle))
        {
            case Disposed:
                throw new ObjectDisposedException("Download handler");
            case Busy:
                throw new InvalidOperationException("This download handler is already running an operation.");
            default:
                return new(this);
        }
    }

    internal bool StartDisposal() => Interlocked.CompareExchange(ref _state, Disposed, Idle) switch
    {
        Disposed => false,
        Busy => throw new InvalidOperationException("Cancel and await the active download operation before disposing its handler."),
        _ => true
    };

    internal readonly struct Scope(DownloadOperation owner) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref owner._state, Idle);
    }
}
