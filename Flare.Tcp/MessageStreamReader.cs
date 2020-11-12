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

        public async Task ReadMessageAsync(MessageHandler messageHandler, CancellationToken cancellationToken = default) {
            // read and parse header
            await Stream.ReadExactAsync(_headerBuffer, cancellationToken).ConfigureAwait(false);
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = RentReadBuffer(messageLength);
            await Stream.ReadExactAsync(messageBuffer.AsMemory(0, messageLength), cancellationToken).ConfigureAwait(false);

            // handle the read message
            messageHandler(messageBuffer.AsSpan(0, messageLength));
        }
        public Span<byte> ReadMessage() {
            // read and parse header
            Stream.ReadExact(_headerBuffer);
            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer);

            // read message into buffer
            var messageBuffer = RentReadBuffer(messageLength).AsSpan(0, messageLength);
            Stream.ReadExact(messageBuffer);
            return messageBuffer;
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
