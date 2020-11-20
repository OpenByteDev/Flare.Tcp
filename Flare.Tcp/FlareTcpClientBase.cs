using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public abstract class FlareTcpClientBase : CancellableObject {
        protected TcpClient? Client;
        private readonly ThreadSafeGuard _connectGuard;

        public event ConnectedEventHandler? Connected;
        public delegate void ConnectedEventHandler();

        public event DisconnectedEventHandler? Disconnected;
        public delegate void DisconnectedEventHandler();

        public bool IsConnected => Client?.Connected == true;
        public bool IsConnecting => _connectGuard.Get();
        public bool IsDisconnected => !IsConnected;
        public IPEndPoint? RemoteEndPoint => Client?.Client.RemoteEndPoint as IPEndPoint;

        protected FlareTcpClientBase() {
            _connectGuard = new ThreadSafeGuard();
        }

        public void Connect(IPEndPoint endPoint) =>
           Connect(endPoint.Address, endPoint.Port);
        public void Connect(IPAddress address, int port) {
            using var token = StartConnecting();

            Client = new TcpClient();
            Client.Connect(address, port);
            OnConnected();
        }
        public ValueTask ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken = default) =>
            ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
        public async ValueTask ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default) {
            using var token = StartConnecting();
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            Client = new TcpClient();
            await Client.ConnectAsync(address, port, linkedToken).ConfigureAwait(false);
            OnConnected();
        }

        protected virtual void OnConnected() {
            Connected?.Invoke();
        }

        protected void EnsureConnected() {
            if (IsDisconnected)
                throw new InvalidOperationException("The client is disconnected.");
        }
        protected void EnsureDisonnected() {
            if (IsConnected)
                throw new InvalidOperationException("The client is already connected.");
        }
        private IDisposable StartConnecting() {
            EnsureDisonnected();
            return _connectGuard.UseOrThrow(() => new InvalidOperationException("The client is already connecting."));
        }

        public void Disconnect() {
            EnsureConnected();

            Client!.Close();
            Client!.Dispose();
            Client = null;

            _connectGuard.Unset();

            ResetCancellationToken();

            OnDisconnected();
        }

        protected virtual void OnDisconnected() {
            Disconnected?.Invoke();
        }

        public override void Dispose() {
            base.Dispose();
            Client?.Dispose();
        }

    }
}
