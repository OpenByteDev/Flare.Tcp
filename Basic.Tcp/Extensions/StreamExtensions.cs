using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Basic.Tcp.Extensions {
    internal static class StreamExtensions {

        /*
        public static Task ReadExactAsync(this Stream stream, byte[] buffer, CancellationToken cancellationToken = default) {
            return ReadExactAsync(stream, buffer, 0, buffer.Length, cancellationToken);
        }
        public static async Task ReadExactAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) {
            while (offset < count) {
                var read = await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
                count -= read;
            }
        }*/
        
        public static async Task ReadExactAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default) {
            while (true) {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException();
                if (read == buffer.Length)
                    return;
                buffer = buffer[read..];
            }
        }

    }
}
