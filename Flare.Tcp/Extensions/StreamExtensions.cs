using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp.Extensions {
    internal static class StreamExtensions {

        public static void ReadExact(this Stream stream, Span<byte> buffer) {
            while (true) {
                var read = stream.Read(buffer);
                if (read == 0)
                    throw new EndOfStreamException();
                if (read == buffer.Length)
                    return;
                buffer = buffer[read..];
            }
        }

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
