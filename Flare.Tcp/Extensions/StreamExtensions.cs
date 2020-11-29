using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp.Extensions {
    internal static class StreamExtensions {
        public static void ReadExact(this Stream stream, Span<byte> buffer) {
            if (!stream.TryReadExact(buffer))
                throw new EndOfStreamException();
        }
        public static async Task ReadExactAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default) {
            if (!await stream.TryReadExactAsync(buffer, cancellationToken).ConfigureAwait(false))
                throw new EndOfStreamException();
        }
        public static bool TryReadExact(this Stream stream, Span<byte> buffer) {
            if (buffer.IsEmpty)
                return true;

            while (true) {
                var read = stream.Read(buffer);
                if (read == 0)
                    return false;
                if (read == buffer.Length)
                    return true;
                buffer = buffer[read..];
            }
        }
        public static async Task<bool> TryReadExactAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default) {
            if (buffer.IsEmpty)
                return true;

            while (true) {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return false;
                if (read == buffer.Length)
                    return true;
                buffer = buffer[read..];
            }
        }
    }
}
