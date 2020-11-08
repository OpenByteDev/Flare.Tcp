using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Basic.Tcp {
    public class BasicTcpClient : BasicTcpSocket {

        private readonly TcpClient _client;

        private NetworkStream _networkStream;
        private ThreadSafeGuard _readGuard;
        private ThreadSafeGuard _connectGuard;

        public bool IsConnected => _client.Connected;
        public bool IsConnecting => _connectGuard.Get();
        public bool IsDisconnected => !IsConnected;

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(ReadOnlySpan<byte> message);

        public BasicTcpClient() {
            _client = new TcpClient();
            _readGuard = new ThreadSafeGuard(false);
            _connectGuard = new ThreadSafeGuard(false);
        }

        public ValueTask ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken = default) =>
            ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
        public async ValueTask ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default) {
            using var token = StartConnecting();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            await _client.ConnectAsync(address, port, linkedToken).ConfigureAwait(false);
            _networkStream = _client.GetStream();
        }
        public void Connect(IPEndPoint endPoint) =>
           Connect(endPoint.Address, endPoint.Port);
        public void Connect(IPAddress address, int port) {
            using var token = StartConnecting();
            _client.Connect(address, port);
            _networkStream = _client.GetStream();
        }

        public ValueTask SendMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            EnsureConnected();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            return WriteMessageToStreamAsync(_networkStream, message, linkedToken);
        }
        public void SendMessage(ReadOnlySpan<byte> message) {
            EnsureConnected();
            WriteMessageToStream(_networkStream, message);
        }

        public Task ReadMessageAsync(CancellationToken cancellationToken = default) {
            EnsureConnected();
            using var token = StartReading();

            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            return ReadMessageFromStreamAsync(_networkStream, message => MessageReceived?.Invoke(message), cancellationToken);
        }
        public void ReadMessage() {
            EnsureConnected();
            ReadMessageFromStream(_networkStream, message => MessageReceived?.Invoke(message));
        }

        public async Task ReadMessagesAsync(CancellationToken cancellationToken = default) {
            EnsureConnected();
            using var token = StartReading();

            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            var headerBuffer = new byte[4];
            while (_client.Connected && !linkedToken.IsCancellationRequested)
                await ReadMessageFromStreamAsync(_networkStream, message => MessageReceived?.Invoke(message), headerBuffer, cancellationToken).ConfigureAwait(false); ;
        }
        public void ReadMessages() {
            EnsureConnected();

            ReadMessagesFromStream(_networkStream, message => MessageReceived?.Invoke(message), () => _client.Connected);
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
            return _readGuard.UseOrThrow(() => new InvalidOperationException("A read operation already in progress."));
        }
        private IDisposable StartConnecting() {
            EnsureDisonnected();
            return _readGuard.UseOrThrow(() => new InvalidOperationException("The client is already connecting."));
        }

        public void Disconnect() {
            EnsureConnected();

            _client.Close();
            _client.Dispose();
            _networkStream?.Dispose();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _readGuard.Unset();
            _connectGuard.Unset();
        }

        public override void Dispose() {
            if (IsConnected)
                Disconnect();
        }
    }
}
