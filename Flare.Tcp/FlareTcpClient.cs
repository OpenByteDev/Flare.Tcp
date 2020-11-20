using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public class FlareTcpClient : FlareTcpClientBase {
        private NetworkStream? _networkStream;
        private MessageStreamReader? _messageReader;
        private MessageStreamWriter? _messageWriter;
        private readonly ThreadSafeGuard _readGuard;
        private readonly ThreadSafeGuard _writeGuard;

        public FlareTcpClient() {
            _readGuard = new ThreadSafeGuard();
            _writeGuard = new ThreadSafeGuard();
        }

        protected override void OnConnected() {
            base.OnConnected();

            // Client!.Client.Blocking = true;
            _networkStream = Client!.GetStream();
            _messageReader = new MessageStreamReader(_networkStream);
            _messageWriter = new MessageStreamWriter(_networkStream);
        }

        public void WriteMessage(ReadOnlySpan<byte> message) {
            using var token = StartWriting();
            _messageWriter!.WriteMessage(message);
        }
        public async ValueTask WriteMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            using var token = StartWriting();
            await _messageWriter!.WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public Span<byte> ReadNextMessage() {
            using var readToken = StartReading();
            return _messageReader!.ReadMessage();
        }
        public bool TryReadNextMessage(out Span<byte> message) {
            using var readToken = StartReading();
            return _messageReader!.TryReadMessage(out message);
        }
        public async Task ReadNextMessageAsync(SpanAction<byte> messageHandler, CancellationToken cancellationToken = default) {
            using var readToken = StartReading();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            await _messageReader!.ReadMessageAsync(messageHandler, linkedToken).ConfigureAwait(false);
        }

        private IDisposable StartReading() {
            EnsureConnected();
            return _readGuard.UseOrThrow(() => new InvalidOperationException("A read operation already in progress."));
        }
        private IDisposable StartWriting() {
            EnsureConnected();
            return _writeGuard.UseOrThrow(() => new InvalidOperationException("A write operation already in progress."));
        }

        protected override void OnDisconnected() {
            base.OnDisconnected();

            _readGuard.Unset();
            _writeGuard.Unset();

            _networkStream?.Close();
            _networkStream?.Dispose();
            _messageReader?.Dispose();
            _networkStream = null;
            _messageReader = null;
            _messageWriter = null;
        }

        public override void Dispose() {
            base.Dispose();
            _networkStream?.Dispose();
            _messageReader?.Dispose();
        }

    }
}
