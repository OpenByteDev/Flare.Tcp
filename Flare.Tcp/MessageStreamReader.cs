using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Flare.Tcp.Extensions;
using Memowned;

namespace Flare.Tcp {
    public class MessageStreamReader {
        private const int HeaderLength = sizeof(int);

        /// <summary>
        /// Gets the underlying stream used for read and write operations.
        /// </summary>
        public Stream Stream { get; }

        private readonly byte[] _headerBuffer = new byte[HeaderLength];

        public MessageStreamReader(Stream stream) {
            Stream = stream!;
        }

        public RentedMemory<byte> ReadMessage() => TryReadMessage() ?? throw new EndOfStreamException();
        public bool TryReadMessage([NotNullWhen(true)] out RentedMemory<byte>? message) => (message = TryReadMessage()) != null;
        public RentedMemory<byte>? TryReadMessage() {
            // read and parse header
            if (!Stream.TryReadExact(_headerBuffer))
                return null;
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = new RentedMemory<byte>(messageLength);
            if (!Stream.TryReadExact(messageBuffer.Span))
                return null;

            return messageBuffer;
        }

        public async Task<RentedMemory<byte>> ReadMessageAsync(CancellationToken cancellationToken = default) =>
            await TryReadMessageAsync(cancellationToken).ConfigureAwait(false) ?? throw new EndOfStreamException();
        public async Task<RentedMemory<byte>?> TryReadMessageAsync(CancellationToken cancellationToken = default) {
            // read and parse header
            if (!await Stream.TryReadExactAsync(_headerBuffer, cancellationToken).ConfigureAwait(false))
                return null;
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = new RentedMemory<byte>(messageLength);
            if (!await Stream.TryReadExactAsync(messageBuffer.Memory, cancellationToken).ConfigureAwait(false))
                return null;

            // read successfully
            return messageBuffer;
        }
    }
}
