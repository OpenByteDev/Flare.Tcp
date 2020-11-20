using Flare.Tcp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ValueTaskSupplement;

namespace Flare.Tcp {
    public class FlareTcpServer : CancellableObject {
        private TcpListener? _listener;
        private readonly ConcurrentDictionary<long, ClientToken> _clients = new();
        private readonly ThreadSafeGuard _listenGuard = new();
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
        public FlareTcpServer(IPAddress localAddress, int port) : this(new IPEndPoint(localAddress, port)) { }
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
                try {
                    var socket = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    AcceptClient(socket);
                } catch (ObjectDisposedException) when (linkedToken.IsCancellationRequested) {
                    return;
                } catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted || e.SocketErrorCode == SocketError.OperationAborted) {
                    return;
                }
            }
        }

        public void Listen() {
            using var token = StartListening();

            SetupAndStartListener();

            while (IsListening && !CancellationToken.IsCancellationRequested) {
                try {
                    var socket = _listener.AcceptTcpClient();
                    AcceptClient(socket);
                } catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted || e.SocketErrorCode == SocketError.OperationAborted) {
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

        protected virtual void AcceptClient(TcpClient socket) {
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

            var readTask = TaskUtils.StartLongRunning(async () => {
                using var reader = new MessageStreamReader(stream);
                while (socket.Connected && !linkedToken.IsCancellationRequested)
                    await reader.ReadMessageAsync(message => OnMessageReceived(client.Id, message), linkedToken);
            }, linkedToken);

            var writeTask = TaskUtils.StartLongRunning(async () => {
                var writer = new MessageStreamWriter(stream);
                await foreach (var message in client.PendingMessages.Reader.ReadAllAsync(linkedToken))
                    await writer.WriteMessageAsync(message.MessageContent, linkedToken);
            }, linkedToken);

            Task.WhenAny(readTask, writeTask).ContinueWith(_ => {
                // handle client disconnect or failure
                _clients.TryRemove(client.Id);
                OnClientDisconnected(client.Id);

                // ensure both tasks complete
                clientCancellationTokenSource.Cancel();
                clientCancellationTokenSource.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            Task.WhenAll(readTask, writeTask).ContinueWith(_ => {
                // dispose resources
                client.Close();
                client.Dispose();
                stream.Close();
                stream.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        public bool DisconnectClient(long clientId) {
            var client = GetClientToken(clientId);
            return DisconnectClient(client);
        }
        private bool DisconnectClient(ClientToken client) {
            if (!_clients.TryRemove(client.Id))
                return false;
            client.Close();
            client.Dispose();
            return true;
        }

        public Task EnqueueMessageAndWaitAsync(long clientId, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            var client = GetClientToken(clientId);
            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            return client.EnqueueMessageAndWaitAsync(message, linkedToken);
        }
        public void EnqueueMessageAndWait(long clientId, ReadOnlyMemory<byte> message) {
            var client = GetClientToken(clientId);
            client.EnqueueMessageAndWait(message);
        }

        public void EnqueueMessage(long clientId, ReadOnlyMemory<byte> message) {
            var client = GetClientToken(clientId);
            client.EnqueueMessage(message);
        }
        public ValueTask EnqueueMessageAsync(long clientId, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            var client = GetClientToken(clientId);
            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            return client.EnqueueMessageAsync(message, linkedToken);
        }

        public void EnqueueMessages(long clientId, IEnumerable<ReadOnlyMemory<byte>> messages) {
            var client = GetClientToken(clientId);

            foreach (var message in messages)
                client.EnqueueMessage(message);
        }
        public ValueTask EnqueueMessagesAsync(long clientId, IEnumerable<ReadOnlyMemory<byte>> messages, CancellationToken cancellationToken = default) {
            var client = GetClientToken(clientId);
            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            return ValueTaskEx.WhenAll(messages.Select(message => client.EnqueueMessageAsync(message, linkedToken)));
        }

        public void EnqueueBroadcastMessage(ReadOnlyMemory<byte> message) {
            foreach (var (_, clientToken) in _clients)
                clientToken.EnqueueMessage(message);
        }
        public ValueTask EnqueueBroadcastMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            return ValueTaskEx.WhenAll(_clients.Values.Select(client => client.EnqueueMessageAsync(message, linkedToken)));
        }

        public void EnqueueBroadcastMessages(IEnumerable<ReadOnlyMemory<byte>> messages) {
            foreach (var (_, clientToken) in _clients)
                foreach (var message in messages)
                    clientToken.EnqueueMessage(message);
        }
        public ValueTask EnqueueBroadcastMessageAsync(IEnumerable<ReadOnlyMemory<byte>> messages, CancellationToken cancellationToken = default) {
            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            return ValueTaskEx.WhenAll(_clients.Values.Cartesian(messages).Select(e => e.Item1.EnqueueMessageAsync(e.Item2, linkedToken)));
        }

        public void Stop() {
            EnsureListening();

            _listener!.Stop();
            _listener = null;
            _listenGuard.Unset();

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
            public readonly Channel<PendingMessage> PendingMessages = Channel.CreateUnbounded<PendingMessage>(new UnboundedChannelOptions() { SingleReader = true });

            public ClientToken(long clientId, TcpClient socket) {
                Socket = socket;
                Id = clientId;
            }

            public void EnqueueMessageAndWait(ReadOnlyMemory<byte> message) {
                EnqueueMessageAndWaitAsync(message).WaitAndUnwrap();
            }
            public async Task EnqueueMessageAndWaitAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
                var (pending, taskSource) = PendingMessage.CreateWithWait(message);
                await EnqueueMessageAsync(pending, cancellationToken);
                await taskSource.Task;
            }

            public void EnqueueMessage(ReadOnlyMemory<byte> message) {
                EnqueueMessage(PendingMessage.Create(message));
            }
            public ValueTask EnqueueMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
                return EnqueueMessageAsync(PendingMessage.Create(message), cancellationToken);
            }

            private void EnqueueMessage(in PendingMessage message) {
                PendingMessages.Writer.TryWrite(message);
            }
            private ValueTask EnqueueMessageAsync(in PendingMessage message, CancellationToken cancellationToken = default) {
                return PendingMessages.Writer.WriteAsync(message, cancellationToken);
            }

            public void Close() {
                Socket.Close();
                PendingMessages.Writer.Complete();
            }

            public void Dispose() {
                Socket.Dispose();
            }
        }
    }
}
