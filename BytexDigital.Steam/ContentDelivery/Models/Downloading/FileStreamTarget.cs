using System.IO;
using System.Threading.Tasks;
using System;

namespace BytexDigital.Steam.ContentDelivery.Models.Downloading
{
#nullable enable

    public class FileStreamTarget : FileTarget
    {
        private readonly string _filePath;
        private readonly ulong _fileSize;
        public FileStream? FileStream { get; private set; }

        public FileStreamTarget(string filePath, ulong fileSize)
        {
            _filePath = filePath;
            _fileSize = fileSize;
            TotalBytes = fileSize;
        }
        
        public override async Task CompleteAsync()
        {
            if (FileStream is null)
            {
                return;
            }

            await FileStream.FlushAsync().ConfigureAwait(false);
            FileStream.Close();
            FileStream = null;
        }

        public override async Task WriteAsync(ulong offset, byte[] data)
        {
            if (FileStream == null)
            {
                FileStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
                FileStream.SetLength((long) _fileSize);
            }
            
            ArgumentNullException.ThrowIfNull(data);

            var fileStream = FileStream ?? throw new InvalidOperationException("The file stream could not be opened.");
            fileStream.Seek((long) offset, SeekOrigin.Begin);
            await fileStream.WriteAsync(data.AsMemory()).ConfigureAwait(false);
            WrittenBytes += (ulong) data.Length;
        }

        public override async Task CancelAsync()
        {
            try
            {
                if (FileStream is null)
                {
                    return;
                }

                await FileStream.FlushAsync().ConfigureAwait(false);
                FileStream.Close();
                FileStream = null;
            }
            catch
            {
            }
        }

        public override async ValueTask DisposeAsync()
        {
            await CancelAsync();
        }
    }
}
