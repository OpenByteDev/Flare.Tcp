using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public class MessageStreamWriter {
        private const int HeaderLength = sizeof(int);

        /// <summary>
        /// Gets the underlying stream used for read and write operations.
        /// </summary>
        public Stream Stream { get; }

        private readonly byte[] _headerBuffer = new byte[HeaderLength];

        public MessageStreamWriter(Stream stream) {
            Stream = stream!;
        }

        public async ValueTask WriteMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            // write message length
            BinaryPrimitives.WriteInt32LittleEndian(_headerBuffer, message.Length);
            await Stream.WriteAsync(_headerBuffer, cancellationToken).ConfigureAwait(false);

            // write message
            await Stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);

            // flush stream
            await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        public void WriteMessage(ReadOnlySpan<byte> message) {
            // write message length
            BinaryPrimitives.WriteInt32LittleEndian(_headerBuffer, message.Length);
            Stream.Write(_headerBuffer);

            // write message
            Stream.Write(message);

            // flush stream
            Stream.Flush();
        }
    }
}
