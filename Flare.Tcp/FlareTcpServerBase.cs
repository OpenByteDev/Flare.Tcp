using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Flare.Tcp {
    public abstract class FlareTcpServerBase : IDisposable {
        public TcpListener? Server { get; set; }
        public bool IsRunning { get; private set; }
        public bool IsStopped => !IsRunning;

        [MemberNotNull(nameof(Server))]
        protected internal void StartListener(int port) {
            EnsureStopped();
            Server = TcpListener.Create(port);
            Server.Server.LingerState = new LingerOption(true, 0);
            Server.Start();
            IsRunning = true;
        }
        [MemberNotNull(nameof(Server))]
        protected internal void StartListener(IPAddress address, int port) => StartListener(new IPEndPoint(address, port));
        [MemberNotNull(nameof(Server))]
        protected internal void StartListener(IPEndPoint endPoint) {
            EnsureStopped();
            Server = new TcpListener(endPoint);
            Server.Server.LingerState = new LingerOption(true, 0);
            Server.Start();
            IsRunning = true;
        }

        public virtual void Shutdown() {
            EnsureRunning();
            Cleanup();
        }

        [MemberNotNull(nameof(Server))]
        protected void EnsureRunning() {
            ThrowIfDiposed();

            if (IsStopped)
                ThrowNotRunning();

            Debug.Assert(Server != null);

            [DoesNotReturn]
            static void ThrowNotRunning() => throw new InvalidOperationException("Server is not currently running.");
        }
        protected void EnsureStopped() {
            ThrowIfDiposed();

            if (IsRunning)
                ThrowAlreadyRunning();

            [DoesNotReturn]
            static void ThrowAlreadyRunning() => throw new InvalidOperationException("Server is already running.");
        }

        protected void ThrowIfDiposed() {
            if (_disposed)
                ThrowObjectDisposedException();

            [DoesNotReturn]
            void ThrowObjectDisposedException() => throw new ObjectDisposedException(GetType().FullName);
        }

        protected virtual void Cleanup() {
            Server?.Stop();
            Server = null;
            IsRunning = false;
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
