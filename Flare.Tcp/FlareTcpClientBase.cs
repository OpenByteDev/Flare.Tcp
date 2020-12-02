using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public abstract class FlareTcpClientBase : IDisposable {
        public TcpClient? Client { get; set; }
        protected NetworkStream? NetworkStream { get; private set; }

        public event ConnectedEventHandler? Connected;
        public delegate void ConnectedEventHandler();

        public event DisconnectedEventHandler? Disconnected;
        public delegate void DisconnectedEventHandler();

        public bool IsConnected => Client?.Connected == true;
        public bool IsConnecting => _connectGuard.Get() && !IsConnected; // Ensure that IsConnected and IsConnecting are never both true at the same time.
        public bool IsDisconnected => !IsConnected;
        public IPEndPoint? RemoteEndPoint => Client?.Client.RemoteEndPoint as IPEndPoint;
        public IPEndPoint? LocalEndPoint { get; set; }

        private readonly ThreadSafeGuard _connectGuard = new();

        protected FlareTcpClientBase() { }

        [MemberNotNull(nameof(Client))]
        internal void DirectConnect(TcpClient client) {
            Client = client;
            OnConnected();
        }

        public void Connect(IPEndPoint endPoint) {
            if (endPoint is null)
                throw new ArgumentNullException(nameof(endPoint));

            Connect(endPoint.Address, endPoint.Port);
        }
        [MemberNotNull(nameof(Client))]
        public virtual void Connect(IPAddress address, int port) {
            using var token = StartConnecting();

            Client = CreateClient();
            Client.Connect(address, port);
            OnConnected();
        }

        public ValueTask ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken = default) {
            if (endPoint is null)
                throw new ArgumentNullException(nameof(endPoint));

            return ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
        }
        [MemberNotNull(nameof(Client))]
        public virtual async ValueTask ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default) {
            using var token = StartConnecting();

            Client = CreateClient();
            await Client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            OnConnected();
        }

        private TcpClient CreateClient() {
            var client = LocalEndPoint is null ? new TcpClient() : new TcpClient(LocalEndPoint);
            // client.Client.LingerState = new LingerOption(true, 0);
            // client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, 1);
            // client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            return client;
        }

        public virtual void Disconnect() {
            EnsureConnected();

            Cleanup();

            OnDisconnected();
        }

        [MemberNotNull(nameof(NetworkStream))]
        protected virtual void OnConnected() {
            NetworkStream = Client!.GetStream();
            Connected?.Invoke();
        }

        protected virtual void OnDisconnected() {
            Disconnected?.Invoke();
        }

        [MemberNotNull(nameof(Client))]
        [MemberNotNull(nameof(NetworkStream))]
        protected void EnsureConnected() {
            if (IsDisconnected)
                throw new InvalidOperationException("The client is disconnected.");
            Debug.Assert(Client != null);
            Debug.Assert(NetworkStream != null);
        }
        protected void EnsureDisonnected() {
            if (IsConnected)
                throw new InvalidOperationException("The client is already connected.");
        }

        private ThreadSafeGuardToken StartConnecting() {
            EnsureDisonnected();
            return _connectGuard.Use() ?? throw new InvalidOperationException("The client is already connecting.");
        }

        protected virtual void Cleanup() {
            if (Client?.Connected == true)
                Client?.Client.Shutdown(SocketShutdown.Both);

            // cleanup the underlying stream
            NetworkStream?.Close();
            NetworkStream?.Dispose();
            NetworkStream = null;

            // cleanup the client itself
            Client?.Close();
            Client?.Dispose();
            Client = null;

            // mark the client as not connecting.
            _connectGuard.Unset();
        }

        #region IDisposable
        private bool _disposed;

        protected virtual void Dispose(bool disposing) {
            if (!_disposed) {
                if (disposing) {
                    Cleanup();
                }
                _disposed = true;
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
