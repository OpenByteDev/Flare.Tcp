using Microsoft.Toolkit.HighPerformance.Buffers;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static Flare.Tcp.ThreadSafeGuard;

namespace Flare.Tcp {
    public class FlareTcpClient : FlareTcpClientBase {
        private NetworkStream? _networkStream;
        private MessageStreamReader? _messageReader;
        private MessageStreamWriter? _messageWriter;
        private readonly ThreadSafeGuard _readGuard = new();
        private readonly ThreadSafeGuard _writeGuard = new();

        protected override void OnConnected() {
            base.OnConnected();

            _networkStream = Client!.GetStream();
            _messageReader = new MessageStreamReader(_networkStream);
            _messageWriter = new MessageStreamWriter(_networkStream);
        }

        public MessageStreamReader? GetMessageReader() {
            return _messageReader;
        }
        public MessageStreamWriter? GetMessageWriter() {
            return _messageWriter;
        }

        public void WriteMessage(ReadOnlySpan<byte> message) {
            using var token = StartWriting();
            _messageWriter!.WriteMessage(message);
        }
        public async ValueTask WriteMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            using var token = StartWriting();
            await _messageWriter!.WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public MemoryOwner<byte> ReadNextMessage() {
            using var readToken = StartReading();
            return _messageReader!.ReadMessage();
        }
        public SpanOwner<byte> ReadNextMessageIntoSpan() {
            using var readToken = StartReading();
            return _messageReader!.ReadMessageIntoSpan();
        }
        public bool TryReadNextMessage([NotNullWhen(true)] out MemoryOwner<byte>? message) {
            using var readToken = StartReading();
            return _messageReader!.TryReadMessage(out message);
        }
        public MemoryOwner<byte>? TryReadNextMessage() {
            using var readToken = StartReading();
            return _messageReader!.TryReadMessage();
        }

        public async Task<MemoryOwner<byte>> ReadNextMessageAsync(CancellationToken cancellationToken = default) {
            using var readToken = StartReading();
            // we need to await here because otherwise the token would immediately be disposed.
            return await _messageReader!.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
        }
        public async Task<MemoryOwner<byte>?> TryReadNextMessageAsync(CancellationToken cancellationToken = default) {
            using var readToken = StartReading();
            // we need to await here because otherwise the token would immediately be disposed.
            return await _messageReader!.TryReadMessageAsync(cancellationToken).ConfigureAwait(false);
        }

        private ThreadSafeGuardToken StartReading() {
            EnsureConnected();
            return _readGuard.UseOrThrow(() => new InvalidOperationException("A read operation already in progress."));
        }
        private ThreadSafeGuardToken StartWriting() {
            EnsureConnected();
            return _writeGuard.UseOrThrow(() => new InvalidOperationException("A write operation already in progress."));
        }

        protected override void OnDisconnected() {
            base.OnDisconnected();

            _readGuard.Unset();
            _writeGuard.Unset();

            _networkStream?.Close();
            _networkStream?.Dispose();
            _networkStream = null;
            _messageReader = null;
            _messageWriter = null;
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            if (disposing) {
                _networkStream?.Dispose();
            }
        }
    }
}
