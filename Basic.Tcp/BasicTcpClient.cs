using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Basic.Tcp {
    public class BasicTcpClient : BasicTcpSocket {

        private readonly TcpClient _client;

        private NetworkStream _networkStream;
        private ThreadSafeGuard _guard;

        public bool IsConnected => _client.Connected;
        public bool IsDisconnected => !IsConnected;

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(ReadOnlySpan<byte> message);

        public BasicTcpClient() {
            _client = new TcpClient();
        }

        public ValueTask ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken = default) =>
            ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
        public async ValueTask ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default) {
            EnsureDisonnected();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            await _client.ConnectAsync(address, port, linkedToken).ConfigureAwait(false);
            _networkStream = _client.GetStream();
            _guard = new ThreadSafeGuard(false);
        }

        public ValueTask SendMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            EnsureConnected();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            return WriteMessageToStreamAsync(_networkStream, message, linkedToken);
        }

        public Task ReadMessageAsync(CancellationToken cancellationToken = default) {
            EnsureConnected();
            using var token = StartReading();

            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            return ReadMessageFromStreamAsync(_networkStream, message => MessageReceived?.Invoke(message), cancellationToken);
        }

        public async Task ReadMessagesAsync(CancellationToken cancellationToken = default) {
            EnsureConnected();
            using var token = StartReading();

            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            var headerBuffer = new byte[4];
            while (_client.Connected && !linkedToken.IsCancellationRequested)
                await ReadMessageFromStreamAsync(_networkStream, message => MessageReceived?.Invoke(message), headerBuffer, cancellationToken).ConfigureAwait(false); ;
        }

        protected void EnsureConnected() {
            if (IsDisconnected)
                throw new InvalidOperationException("Client is disconnected");
        }
        protected void EnsureDisonnected() {
            if (IsConnected)
                throw new InvalidOperationException("Client is already connected");
        }
        private IDisposable StartReading() {
            return _guard.UseOrThrow(() => new InvalidOperationException("A read operation already in progress."));
        }

        public void Disconnect() {
            EnsureConnected();

            _client.Close();
            _client.Dispose();
            _networkStream?.Dispose();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }

        public override void Dispose() {
            if (IsConnected)
                Disconnect();
        }
    }
}
