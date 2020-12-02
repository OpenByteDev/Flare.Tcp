using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public abstract class FlareTcpClientBase : IDisposable {
        public TcpClient? Client { get; private set; }
        protected NetworkStream? NetworkStream { get; private set; }

        public event ConnectedEventHandler? Connected;
        public delegate void ConnectedEventHandler();

        public event DisconnectedEventHandler? Disconnected;
        public delegate void DisconnectedEventHandler();

        public bool IsConnected => Client?.Connected == true;
        public bool IsConnecting => _connectGuard.Get() && !IsConnected; // Ensure that IsConnected and IsConnecting are never both true at the same time.
        public bool IsDisconnected => !IsConnected;
        public IPEndPoint? RemoteEndPoint => Client?.Client.RemoteEndPoint as IPEndPoint;
        private IPEndPoint? _localEndPoint;
        public IPEndPoint? LocalEndPoint {
            get => _localEndPoint;
            set {
                EnsureDisconnected();
                _localEndPoint = value;
            }
        }

        private readonly ThreadSafeGuard _connectGuard = new();

        protected FlareTcpClientBase() { }

        [MemberNotNull(nameof(Client))]
        [MemberNotNull(nameof(NetworkStream))]
        internal void DirectConnect(TcpClient client) {
            Client = client;
            OnConnected();
        }

        [MemberNotNull(nameof(Client))]
        [MemberNotNull(nameof(NetworkStream))]
        public void Connect(IPEndPoint endPoint) {
            if (endPoint is null)
                throw new ArgumentNullException(nameof(endPoint));

            Connect(endPoint.Address, endPoint.Port);
        }
        [MemberNotNull(nameof(Client))]
        [MemberNotNull(nameof(NetworkStream))]
        public virtual void Connect(IPAddress address, int port) {
            using var token = StartConnecting();

            Client = CreateClient();
            Client.Connect(address, port);
            OnConnected();
        }

        [MemberNotNull(nameof(Client))]
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

        private TcpClient CreateClient() => LocalEndPoint is null ? new TcpClient() : new TcpClient(LocalEndPoint);

        public virtual void Disconnect() {
            EnsureConnected();

            Cleanup();

            OnDisconnected();
        }

        [MemberNotNull(nameof(Client))]
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
            ThrowIfDiposed();

            if (IsDisconnected)
                ThrowNotConnected();

            Debug.Assert(Client != null);
            Debug.Assert(NetworkStream != null);

            [DoesNotReturn]
            static void ThrowNotConnected() => throw new InvalidOperationException("The client is disconnected.");
        }
        protected void EnsureDisconnected() {
            ThrowIfDiposed();

            if (IsConnected)
                ThrowAlreadyConnected();

            [DoesNotReturn]
            static void ThrowAlreadyConnected() => throw new InvalidOperationException("The client is already connected.");
        }

        private ThreadSafeGuardToken StartConnecting() {
            EnsureDisconnected();
            return _connectGuard.Use() ?? ThrowAlreadyConnecting();

            [DoesNotReturn]
            static ThreadSafeGuardToken ThrowAlreadyConnecting() => throw new InvalidOperationException("The client is already connecting.");
        }

        protected void ThrowIfDiposed() {
            if (_disposed)
                ThrowObjectDisposedException();

            [DoesNotReturn]
            void ThrowObjectDisposedException() => throw new ObjectDisposedException(GetType().FullName);
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
