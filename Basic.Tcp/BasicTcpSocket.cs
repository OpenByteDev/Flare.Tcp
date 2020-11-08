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

        protected static void WriteMessageToStream(Stream stream, ReadOnlySpan<byte> message) {
            // write message length
            Span<byte> headerBuffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(headerBuffer, message.Length);
            stream.Write(headerBuffer);

            // write message
            stream.Write(message);

            // flush stream
            stream.Flush();
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
        protected static async Task ReadMessagesFromStreamAsync(Stream stream, MessageHandler messageHandler, Func<bool> readUntil, CancellationToken cancellationToken) {
            var pool = ArrayPool<byte>.Shared;
            byte[] headerBuffer = new byte[sizeof(int)];
            byte[]? rentedMessageBuffer = null;

            try {
                while (readUntil()) {
                    // read and parse header
                    await stream.ReadExactAsync(headerBuffer, cancellationToken).ConfigureAwait(false);
                    var messageLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);

                    // rent or rerent buffer if needed
                    if (rentedMessageBuffer is null) {
                        rentedMessageBuffer = pool.Rent(messageLength);
                    } else if (rentedMessageBuffer.Length < messageLength) {
                        pool.Return(rentedMessageBuffer);
                        rentedMessageBuffer = pool.Rent(messageLength);
                    }

                    // read message into buffer 
                    await stream.ReadExactAsync(rentedMessageBuffer.AsMemory(0, messageLength), cancellationToken).ConfigureAwait(false);
                    messageHandler(rentedMessageBuffer.AsSpan(0, messageLength));
                }
            } finally {
                if (rentedMessageBuffer != null)
                    pool.Return(rentedMessageBuffer);
            }
        }
        
        protected static void ReadMessageFromStream(Stream stream, MessageHandler messageHandler) {
            Span<byte> headerBuffer = stackalloc byte[sizeof(int)];

            // read and parse header
            stream.ReadExact(headerBuffer);
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);

            // read and handle message
            if (messageLength <= 512) {
                Span<byte> messageBuffer = stackalloc byte[messageLength];
                stream.ReadExact(messageBuffer);
                messageHandler(messageBuffer);
            } else {
                byte[]? messageBuffer = null;
                try {
                    messageBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
                    var messageSpan = messageBuffer.AsSpan(0, messageLength);
                    stream.ReadExact(messageSpan);
                    messageHandler(messageSpan);
                } finally {
                    if (messageBuffer != null)
                        ArrayPool<byte>.Shared.Return(messageBuffer);
                }
            }
        }
        protected static void ReadMessagesFromStream(Stream stream, MessageHandler messageHandler, Func<bool> readUntil) {
            var pool = ArrayPool<byte>.Shared;
            Span<byte> headerBuffer = stackalloc byte[sizeof(int)];
            byte[]? rentedMessageBuffer = null;

            try {
                while (readUntil()) {
                    // read and parse header
                    stream.ReadExact(headerBuffer);
                    var messageLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);

                    if (rentedMessageBuffer is null)
                        rentedMessageBuffer = pool.Rent(messageLength);
                    else if (rentedMessageBuffer.Length < messageLength) {
                        pool.Return(rentedMessageBuffer);
                        rentedMessageBuffer = pool.Rent(messageLength);
                    }
                    var messageSpan = rentedMessageBuffer.AsSpan(0, messageLength);
                    stream.ReadExact(messageSpan);
                    messageHandler(messageSpan);
                }
            } finally {
                if (rentedMessageBuffer != null)
                    pool.Return(rentedMessageBuffer);
            }
        }

        public virtual void Dispose() {
            _cancellationTokenSource?.Dispose();
        }
    }
}
