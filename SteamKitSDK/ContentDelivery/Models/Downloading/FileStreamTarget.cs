using System.IO;
using System.Threading.Tasks;
using System;

namespace SteamKitSDK.ContentDelivery.Models.Downloading
{
#nullable enable

    public class FileStreamTarget : FileTarget
    {
        private readonly string _filePath;
        private readonly ulong _fileSize;
        private readonly Func<string>? _resolveDestination;
        private string? _stagingPath;
        public FileStream? FileStream { get; private set; }

        public FileStreamTarget(string filePath, ulong fileSize)
        {
            _filePath = filePath;
            _fileSize = fileSize;
            TotalBytes = fileSize;
        }

        internal FileStreamTarget(string filePath, ulong fileSize, Func<string> resolveDestination)
            : this(filePath, fileSize)
        {
            _resolveDestination = resolveDestination;
        }
        
        public override async Task CompleteAsync()
        {
            if (FileStream is null)
            {
                return;
            }

            await FileStream.FlushAsync().ConfigureAwait(false);
            await FileStream.DisposeAsync().ConfigureAwait(false);
            FileStream = null;
            if (_stagingPath is { } stagingPath)
            {
                File.Move(stagingPath, _resolveDestination!(), overwrite: true);
                _stagingPath = null;
            }
        }

        public override async Task WriteAsync(ulong offset, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (offset > _fileSize || (ulong)data.Length > _fileSize - offset || _fileSize > long.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "The write exceeds the declared file size.");
            }
            if (FileStream == null)
            {
                var destination = _resolveDestination?.Invoke() ?? _filePath;
                var path = _resolveDestination == null ? destination :
                    Path.Combine(Path.GetDirectoryName(destination)!, $".steam-download-{Guid.NewGuid():N}.tmp");
                FileStream = new FileStream(path, _resolveDestination == null ? FileMode.Create : FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 131072, true);
                if (_resolveDestination != null) _stagingPath = path;
                FileStream.SetLength((long) _fileSize);
            }

            var fileStream = FileStream ?? throw new InvalidOperationException("The file stream could not be opened.");
            fileStream.Seek((long) offset, SeekOrigin.Begin);
            await fileStream.WriteAsync(data.AsMemory()).ConfigureAwait(false);
            WrittenBytes += (ulong) data.Length;
        }

        public override async Task CancelAsync()
        {
            try
            {
                var stream = FileStream;
                FileStream = null;
                if (stream != null) await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (_stagingPath is { } stagingPath)
                {
                    File.Delete(stagingPath);
                    _stagingPath = null;
                }
            }
        }

        public override async ValueTask DisposeAsync()
        {
            await CancelAsync().ConfigureAwait(false);
        }
    }
}
