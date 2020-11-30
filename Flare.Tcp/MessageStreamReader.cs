using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Flare.Tcp.Extensions;
using Microsoft.Toolkit.HighPerformance.Buffers;

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

        public MemoryOwner<byte> ReadMessage() => TryReadMessage() ?? throw new EndOfStreamException();
        public bool TryReadMessage([NotNullWhen(true)] out MemoryOwner<byte>? message) => (message = TryReadMessage()) != null;
        public MemoryOwner<byte>? TryReadMessage() {
            // read and parse header
            if (!Stream.TryReadExact(_headerBuffer))
                return null;
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = MemoryOwner<byte>.Allocate(messageLength);
            if (!Stream.TryReadExact(messageBuffer.Span))
                return null;

            return messageBuffer;
        }
        public SpanOwner<byte> ReadMessageIntoSpan() {
            // read and parse header
            if (!Stream.TryReadExact(_headerBuffer))
                throw new EndOfStreamException();
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = SpanOwner<byte>.Allocate(messageLength);
            if (!Stream.TryReadExact(messageBuffer.Span))
                throw new EndOfStreamException();

            return messageBuffer;
        }

        public async Task<MemoryOwner<byte>> ReadMessageAsync(CancellationToken cancellationToken = default) =>
            await TryReadMessageAsync(cancellationToken).ConfigureAwait(false) ?? throw new EndOfStreamException();
        public async Task<MemoryOwner<byte>?> TryReadMessageAsync(CancellationToken cancellationToken = default) {
            // read and parse header
            if (!await Stream.TryReadExactAsync(_headerBuffer, cancellationToken).ConfigureAwait(false))
                return null;
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = MemoryOwner<byte>.Allocate(messageLength);
            if (!await Stream.TryReadExactAsync(messageBuffer.Memory, cancellationToken).ConfigureAwait(false))
                return null;

            // read successfully
            return messageBuffer;
        }
    }
}
