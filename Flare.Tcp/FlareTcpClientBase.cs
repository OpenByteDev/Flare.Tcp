using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static Flare.Tcp.ThreadSafeGuard;

namespace Flare.Tcp {
    public abstract class FlareTcpClientBase : IDisposable {
        public TcpClient? Client { get; set; }
        private readonly ThreadSafeGuard _connectGuard = new();

        public event ConnectedEventHandler? Connected;
        public delegate void ConnectedEventHandler();

        public event DisconnectedEventHandler? Disconnected;
        public delegate void DisconnectedEventHandler();

        public bool IsConnected => Client?.Connected == true;
        public bool IsConnecting => _connectGuard.Get() && !IsConnected; // Ensure that IsConnected and IsConnecting are never both true at the same time.
        public bool IsDisconnected => !IsConnected;
        public IPEndPoint? RemoteEndPoint => Client?.Client.RemoteEndPoint as IPEndPoint;

        protected FlareTcpClientBase() { }

        internal void DirectConnect(TcpClient client) {
            Client = client;
            OnConnected();
        }

        public void Connect(IPEndPoint endPoint) {
            if (endPoint is null)
                throw new ArgumentNullException(nameof(endPoint));

            Connect(endPoint.Address, endPoint.Port);
        }
        public void Connect(IPAddress address, int port) {
            using var token = StartConnecting();

            Client = new TcpClient();
            Client.Connect(address, port);
            OnConnected();
        }
        public ValueTask ConnectAsync(IPEndPoint endPoint, CancellationToken cancellationToken = default) {
            if (endPoint is null)
                throw new ArgumentNullException(nameof(endPoint));

            return ConnectAsync(endPoint.Address, endPoint.Port, cancellationToken);
        }
        public async ValueTask ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default) {
            using var token = StartConnecting();

            Client = new TcpClient();
            await Client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            OnConnected();
        }

        public void Disconnect() {
            EnsureConnected();

            Client!.Close();
            Client!.Dispose();
            Client = null;

            _connectGuard.Unset();

            OnDisconnected();
        }

        protected virtual void OnConnected() {
            Connected?.Invoke();
        }

        protected virtual void OnDisconnected() {
            Disconnected?.Invoke();
        }

        [MemberNotNull(nameof(Client))]
        protected void EnsureConnected() {
            if (IsDisconnected)
                throw new InvalidOperationException("The client is disconnected.");
            Debug.Assert(Client != null);
        }
        protected void EnsureDisonnected() {
            if (IsConnected)
                throw new InvalidOperationException("The client is already connected.");
        }

        private ThreadSafeGuardToken StartConnecting() {
            EnsureDisonnected();
            return _connectGuard.UseOrThrow(() => new InvalidOperationException("The client is already connecting."));
        }

        #region IDisposable
        private bool _disposed;

        protected virtual void Dispose(bool disposing) {
            if (!_disposed) {
                if (disposing) {
                    Client?.Dispose();
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
