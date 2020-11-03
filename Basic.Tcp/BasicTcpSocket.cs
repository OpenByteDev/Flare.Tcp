using Basic.Tcp.Extensions;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Basic.Tcp {
    public class BasicTcpSocket : IDisposable {

        protected internal delegate void MessageHandler(Span<byte> message);

        protected internal CancellationTokenSource? _cancellationTokenSource;
        protected internal CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        protected CancellationToken GetLinkedCancellationToken(CancellationToken cancellationToken) {
            if (!cancellationToken.CanBeCanceled)
                return CancellationToken;
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken).Token;
        }

        protected static ValueTask WriteMessageToStreamAsync(Stream stream, ReadOnlyMemory<byte> message, CancellationToken cancellationToken) =>
            WriteMessageToStreamAsync(stream, message, new byte[sizeof(int)], cancellationToken);
        protected static async ValueTask WriteMessageToStreamAsync(Stream stream, ReadOnlyMemory<byte> message, byte[] headerBuffer, CancellationToken cancellationToken) {
            // write message length
            BinaryPrimitives.WriteInt32LittleEndian(headerBuffer, message.Length);
            await stream.WriteAsync(headerBuffer, cancellationToken).ConfigureAwait(false);

            // write message
            await stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);

            // flush stream
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        protected static Task ReadMessageFromStreamAsync(Stream stream, MessageHandler messageHandler, CancellationToken cancellationToken) =>
            ReadMessageFromStreamAsync(stream, messageHandler, new byte[sizeof(int)], cancellationToken);

        protected static async Task ReadMessageFromStreamAsync(Stream stream, MessageHandler messageHandler, byte[] headerBuffer, CancellationToken cancellationToken) {
            // read and parse header
            await stream.ReadExactAsync(headerBuffer, cancellationToken).ConfigureAwait(false);
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);

            // read and handle message
            var shouldRentBuffer = messageLength > 1024;
            byte[]? messageBuffer = null;
            try {
                // allocate or rent buffer
                messageBuffer = shouldRentBuffer ? ArrayPool<byte>.Shared.Rent(messageLength) : new byte[messageLength];

                // read message into buffer
                await stream.ReadExactAsync(messageBuffer.AsMemory(0, messageLength), cancellationToken).ConfigureAwait(false);

                // handle the read message
                messageHandler(messageBuffer.AsSpan(0, messageLength));
            } finally {
                // return the rented array if needed.
                if (shouldRentBuffer && messageBuffer != null)
                    ArrayPool<byte>.Shared.Return(messageBuffer);
            }
        }

        public virtual void Dispose() {
            _cancellationTokenSource?.Dispose();
        }
    }
}
