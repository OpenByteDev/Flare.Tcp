using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public class FlareTcpClient : CancellableObject {
        private TcpClient? _client;
        private NetworkStream? _networkStream;
        private MessageStreamReader? _messageReader;
        private MessageStreamWriter? _messageWriter;
        private readonly ThreadSafeGuard _readGuard;
        private readonly ThreadSafeGuard _writeGuard;
        private readonly ThreadSafeGuard _connectGuard;

        public bool IsConnected => _client != null && _client.Connected;
        public bool IsConnecting => _connectGuard.Get();
        public bool IsDisconnected => !IsConnected;

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(Span<byte> message);

        public FlareTcpClient() {
            _readGuard = new ThreadSafeGuard();
            _writeGuard = new ThreadSafeGuard();
            _connectGuard = new ThreadSafeGuard();
        }

        protected virtual void OnMessageReceived(Span<byte> message) {
            MessageReceived?.Invoke(message);
        }

        protected virtual void OnConnected() {
            _networkStream = _client!.GetStream();
            _messageReader = new MessageStreamReader(_networkStream);
            _messageWriter = new MessageStreamWriter(_networkStream);
        }
        protected virtual void OnDisconnected() {
            // TODO
        }

        public ValueTask ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken = default) =>
            ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
        public async ValueTask ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default) {
            using var token = StartConnecting();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            _client = new TcpClient();
            await _client.ConnectAsync(address, port, linkedToken).ConfigureAwait(false);
            OnConnected();
        }
        public void Connect(IPEndPoint endPoint) =>
           Connect(endPoint.Address, endPoint.Port);
        public void Connect(IPAddress address, int port) {
            using var token = StartConnecting();

            _client = new TcpClient();
            _client.Connect(address, port);
            OnConnected();
        }

        public ValueTask SendMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            using var token = StartWriting();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            return _messageWriter!.WriteMessageAsync(message, linkedToken);
        }

        public void SendMessage(ReadOnlySpan<byte> message) {
            using var token = StartWriting();

            _messageWriter!.WriteMessage(message);
        }

        public Task ReadMessageAsync(CancellationToken cancellationToken = default) {
            using var token = StartReading();

            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            return _messageReader!.ReadMessageAsync(OnMessageReceived, cancellationToken);
        }
        public void ReadMessage() {
            using var token = StartReading();

            var message = _messageReader!.ReadMessage();
            OnMessageReceived(message);
        }

        public async Task ReadMessagesAsync(CancellationToken cancellationToken = default) {
            using var token = StartReading();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            while (IsConnected && !linkedToken.IsCancellationRequested)
                await _messageReader!.ReadMessageAsync(OnMessageReceived, cancellationToken).ConfigureAwait(false);
        }
        public void ReadMessages() {
            using var token = StartReading();

            while (IsConnected && !CancellationToken.IsCancellationRequested) {
                var message = _messageReader!.ReadMessage();
                OnMessageReceived(message);
            }
        }

        protected void EnsureConnected() {
            if (IsDisconnected)
                throw new InvalidOperationException("The client is disconnected.");
        }
        protected void EnsureDisonnected() {
            if (IsConnected)
                throw new InvalidOperationException("The client is already connected.");
        }
        private IDisposable StartReading() {
            EnsureConnected();
            return _readGuard.UseOrThrow(() => new InvalidOperationException("A read operation already in progress."));
        }
        private IDisposable StartWriting() {
            EnsureConnected();
            return _writeGuard.UseOrThrow(() => new InvalidOperationException("A write operation already in progress."));
        }
        private IDisposable StartConnecting() {
            EnsureDisonnected();
            return _connectGuard.UseOrThrow(() => new InvalidOperationException("The client is already connecting."));
        }

        public void Disconnect() {
            EnsureConnected();

            _client!.Close();
            _client!.Dispose();
            _client = null;

            ResetCancellationToken();

            _readGuard.Unset();
            _writeGuard.Unset();
            _connectGuard.Unset();

            _networkStream?.Close();
            _networkStream?.Dispose();
            _messageReader?.Dispose();
            _networkStream = null;
            _messageReader = null;
            _messageWriter = null;
        }

        public override void Dispose() {
            if (IsConnected)
                Disconnect();

            base.Dispose();
        }
    }
}
