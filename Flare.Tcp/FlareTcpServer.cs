using Flare.Tcp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public class FlareTcpServer : CancellableObject {
        private TcpListener? _listener;
        private readonly ConcurrentDictionary<long, ClientToken> _clients = new ConcurrentDictionary<long, ClientToken>();
        private readonly ThreadSafeGuard _listenGuard = new ThreadSafeGuard();
        private long _nextClientId /* = 0 */;

        public bool IsListening => _listenGuard.Get();
        public bool IsStopped => !IsListening;
        public int ConnectedClients => _clients.Count;
        public IPEndPoint LocalEndPoint { get; }
        public bool DualMode { get; } /* = false; */

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(long clientId, Span<byte> message);

        public event ClientConnectedEventHandler? ClientConnected;
        public delegate void ClientConnectedEventHandler(long clientId);

        public event ClientDisconnectedEventHandler? ClientDisconnected;
        public delegate void ClientDisconnectedEventHandler(long clientId);

        public FlareTcpServer(int port) {
            // from TcpListener.Create
            if (Socket.OSSupportsIPv6) {
                // If OS supports IPv6 use dual mode so both address families work.
                LocalEndPoint = new IPEndPoint(IPAddress.IPv6Any, port);
                DualMode = true;
            } else {
                // If not, fall-back to old IPv4.
                LocalEndPoint = new IPEndPoint(IPAddress.Any, port);
                DualMode = false;
            }
        }
        public FlareTcpServer(IPAddress localAddress, int port) {
            LocalEndPoint = new IPEndPoint(localAddress, port);
        }
        public FlareTcpServer(IPEndPoint localEndPoint) {
            LocalEndPoint = localEndPoint;
        }

        protected virtual void OnMessageReceived(long clientId, Span<byte> message) {
            MessageReceived?.Invoke(clientId, message);
        }

        protected virtual void OnClientConnected(long clientId) {
            ClientConnected?.Invoke(clientId);
        }

        protected virtual void OnClientDisconnected(long clientId) {
            ClientDisconnected?.Invoke(clientId);
        }

        public async Task ListenAsync(CancellationToken cancellationToken = default) {
            using var token = StartListening();

            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            SetupAndStartListener();

            while (IsListening && !linkedToken.IsCancellationRequested) {
                var socket = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                OnClientAccepted(socket);
            }
        }

        public void Listen() {
            using var token = StartListening();

            SetupAndStartListener();

            while (IsListening && !CancellationToken.IsCancellationRequested) {
                try {
                    var socket = _listener.AcceptTcpClient();
                    OnClientAccepted(socket);
                } catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted) {
                    return;
                } catch (ObjectDisposedException) when (CancellationToken.IsCancellationRequested) {
                    return;
                }
            }
        }

        [MemberNotNull(nameof(_listener))]
        private void SetupAndStartListener() {
            _listener = new TcpListener(LocalEndPoint);

            // setting DualMode to false on a socket with an IPv4 address will throw.
            if (DualMode)
                _listener.Server.DualMode = DualMode;

            _listener.Start();
        }

        protected long GetNextClientId() => _nextClientId;
        protected long GetAndIncrementNextClientId() => Interlocked.Increment(ref _nextClientId) - 1;

        protected virtual void OnClientAccepted(TcpClient socket) {
            var clientId = GetAndIncrementNextClientId();
            var client = new ClientToken(clientId, socket);
            if (!_clients.TryAdd(clientId, client))
                throw new Exception("Client id already used when it should not.");
            OnClientConnected(clientId);
            HandleClient(client);
        }

        private void HandleClient(ClientToken client) {
            var clientCancellationTokenSource = new CancellationTokenSource();
            var linkedToken = GetLinkedCancellationToken(clientCancellationTokenSource.Token);

            var socket = client.Socket;
            var stream = socket.GetStream();

            var readTask = Task.Factory.StartNew(() => {
                using var reader = new MessageStreamReader(stream);
                while (socket.Connected && !linkedToken.IsCancellationRequested)
                    OnMessageReceived(client.Id, reader.ReadMessage());
            }, linkedToken, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);

            var writeTask = Task.Factory.StartNew(() => {
                var writer = new MessageStreamWriter(stream);
                while (socket.Connected && !linkedToken.IsCancellationRequested) {
                    // wait for new packets.
                    client.PendingMessageEvent.Wait(linkedToken);
                    client.PendingMessageEvent.Reset();

                    // write queued message to stream
                    while (client.PendingMessages.TryDequeue(out var message))
                        writer.WriteMessage(message.Span);
                }
            }, linkedToken, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);

            Task.WhenAny(readTask, writeTask).ContinueWith(_ => {
                // handle client disconnect or failure
                _clients.TryRemove(client.Id);
                OnClientDisconnected(client.Id);

                // ensure both tasks complete
                clientCancellationTokenSource.Cancel();

                // dispose resources
                clientCancellationTokenSource.Dispose();
                client.Dispose();
                stream.Close();
                stream.Dispose();
            });
        }

        public void DisconnectClient(long clientId) {
            var client = GetClientToken(clientId);
            DisconnectClient(client);
        }
        private void DisconnectClient(ClientToken client) {
            _clients.TryRemove(client.Id);
            client.Close();
            client.Dispose();
        }


        public void EnqueueMessage(long clientId, ReadOnlyMemory<byte> message) {
            var client = GetClientToken(clientId);

            client.EnqueueMessage(message);
        }
        public void EnqueueMessages(long clientId, IEnumerable<ReadOnlyMemory<byte>> messages) {
            var client = GetClientToken(clientId);

            foreach (var message in messages)
                client.EnqueueMessage(message);
        }
        public void EnqueueBroadcastMessage(ReadOnlyMemory<byte> message) {
            foreach (var (_, clientToken) in _clients)
                clientToken.EnqueueMessage(message);
        }
        public void EnqueueBroadcastMessages(IEnumerable<ReadOnlyMemory<byte>> messages) {
            foreach (var (_, clientToken) in _clients)
                foreach (var message in messages)
                    clientToken.EnqueueMessage(message);
        }

        public void Stop() {
            EnsureListening();

            _listener!.Stop();
            _listener = null;
            _listenGuard.Unset();

            foreach (var client in _clients.Values) {
                client.Close();
                client.Dispose();
            }
            _clients.Clear();

            ResetCancellationToken();
        }

        private IDisposable StartListening() {
            EnsureStopped();
            return _listenGuard.UseOrThrow(() => new InvalidOperationException("The server is already listening."));
        }
        protected void EnsureListening() {
            if (IsStopped)
                throw new InvalidOperationException("Server is not running.");
        }
        protected void EnsureStopped() {
            if (IsListening)
                throw new InvalidOperationException("Server is already running.");
        }
        private ClientToken GetClientToken(long clientId) {
            if (!_clients.TryGetValue(clientId, out var client))
                throw new InvalidOperationException("Target client id is not valid.");
            return client;
        }

        public override void Dispose() {
            if (IsListening)
                Stop();

            base.Dispose();
        }

        private class ClientToken : IDisposable {
            public readonly TcpClient Socket;
            public readonly long Id;
            public readonly ConcurrentQueue<ReadOnlyMemory<byte>> PendingMessages;
            public readonly ManualResetEventSlim PendingMessageEvent;

            public ClientToken(long clientId, TcpClient socket) {
                Socket = socket;
                Id = clientId;
                PendingMessages = new ConcurrentQueue<ReadOnlyMemory<byte>>();
                PendingMessageEvent = new ManualResetEventSlim(false);
            }

            public void EnqueueMessage(ReadOnlyMemory<byte> message) {
                PendingMessages.Enqueue(message);
                PendingMessageEvent.Set();
            }

            public void Close() {
                Socket.Close();
            }

            public void Dispose() {
                Socket.Dispose();
                PendingMessageEvent.Dispose();
            }
        }
    }
}
