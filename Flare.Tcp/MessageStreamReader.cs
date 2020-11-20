using Flare.Tcp.Extensions;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public class MessageStreamReader : IDisposable {
        private const int HeaderLength = sizeof(int);

        /// <summary>
        /// Holds the underlying stream used for read and write operations.
        /// </summary>
        public Stream Stream { get; }

        private readonly ArrayPool<byte> _pool;
        private readonly byte[] _headerBuffer;
        private byte[]? _messageBuffer;
        private bool disposedValue;

        public MessageStreamReader(Stream stream, ArrayPool<byte>? arrayPool = null) {
            Stream = stream!;
            _headerBuffer = new byte[HeaderLength];
            _messageBuffer = null;
            _pool = arrayPool ?? ArrayPool<byte>.Shared;
        }

        public Task ReadMessageAsync(SpanAction<byte> messageHandler, CancellationToken cancellationToken = default) {
            return ReadMessageAsync<bool>((message, _) => messageHandler(message), default, cancellationToken);
        }
        public async Task ReadMessageAsync<TArg>(SpanAction<byte, TArg> messageHandler, TArg arg, CancellationToken cancellationToken = default) {
            if (!await TryReadMessageAsync(messageHandler, arg, cancellationToken).ConfigureAwait(false))
                throw new EndOfStreamException();
        }
        public Task<bool> TryReadMessageAsync(SpanAction<byte> messageHandler, CancellationToken cancellationToken = default) {
            return TryReadMessageAsync<bool>((message, _) => messageHandler(message), default, cancellationToken);
        }
        public async Task<bool> TryReadMessageAsync<TArg>(SpanAction<byte, TArg> messageHandler, TArg arg, CancellationToken cancellationToken = default) {
            // read and parse header
            if (!await Stream.TryReadExactAsync(_headerBuffer, cancellationToken).ConfigureAwait(false))
                return false;
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = RentReadBuffer(messageLength);
            if (!await Stream.TryReadExactAsync(messageBuffer.AsMemory(0, messageLength), cancellationToken).ConfigureAwait(false))
                return false;

            // handle the read message
            messageHandler(messageBuffer.AsSpan(0, messageLength), arg);

            // read successfully
            return true;
        }

        public Span<byte> ReadMessage() {
            if (!TryReadMessage(out var message))
                throw new EndOfStreamException();
            return message;
        }
        public bool TryReadMessage(out Span<byte> message) {
            message = default;

            // read and parse header
            if (!Stream.TryReadExact(_headerBuffer))
                return false;
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = RentReadBuffer(messageLength).AsSpan(0, messageLength);
            if (!Stream.TryReadExact(messageBuffer))
                return false;

            message = messageBuffer;
            return true;
        }

        private byte[] RentReadBuffer(int messageLength) {
            if (_messageBuffer is null) {
                _messageBuffer = _pool.Rent(messageLength);
            } else if (messageLength > _messageBuffer.Length) {
                _pool.ReturnAndSetNull(ref _messageBuffer);
                _messageBuffer = _pool.Rent(messageLength);
            }
            return _messageBuffer;
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // dispose managed state
                }

                // free unmanaged resources
                _pool.ReturnAndSetNull(ref _messageBuffer);

                disposedValue = true;
            }
        }

        ~MessageStreamReader() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
