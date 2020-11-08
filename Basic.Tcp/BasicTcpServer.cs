using Basic.Tcp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Basic.Tcp {
    public class BasicTcpServer : BasicTcpSocket {

        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<long, ClientToken> _clients;
        private long _nextClientId /* = 0 */;

        public bool IsRunning { get; private set; } /* = false;*/
        public bool IsStopped => !IsRunning;
        public int ClientCount => _clients.Count;

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(long clientId, Span<byte> message);

        public event ClientConnectedEventHandler? ClientConnected;
        public delegate void ClientConnectedEventHandler(long clientId);

        public event ClientDisconnectedEventHandler? ClientDisconnected;
        public delegate void ClientDisconnectedEventHandler(long clientId);

        public BasicTcpServer(int port) {
            _listener = TcpListener.Create(port);
            _clients = new ConcurrentDictionary<long, ClientToken>();
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
            EnsureStopped();
            IsRunning = true;

            var linkedToken = GetLinkedCancellationToken(cancellationToken);
            _listener.Start();

            while (IsRunning && !CancellationToken.IsCancellationRequested) {
                var socket = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                var clientId = GetAndIncrementNextClientId();
                var client = new ClientToken(clientId, socket);
                _clients.TryAdd(clientId, client);
                OnClientConnected(clientId);
                HandleClient(client);
            }
        }
        public void Listen() {
            EnsureStopped();
            IsRunning = true;

            _listener.Start();

            while (IsRunning) {
                var socket = _listener.AcceptTcpClient();
                var clientId = GetAndIncrementNextClientId();
                var client = new ClientToken(clientId, socket);
                _clients.TryAdd(clientId, client);
                OnClientConnected(clientId);
                HandleClient2(client);
            }
        }

        protected long GetNextClientId() => _nextClientId;
        protected long GetAndIncrementNextClientId() => Interlocked.Increment(ref _nextClientId) - 1;

        private void HandleClient(ClientToken client) {
            var clientCancellationTokenSource = new CancellationTokenSource();
            var linkedToken = GetLinkedCancellationToken(clientCancellationTokenSource.Token);

            var socket = client.Socket;
            var stream = socket.GetStream();

            var readTask = Task.Run(async () => {
                await ReadMessagesFromStreamAsync(stream, message => MessageReceived?.Invoke(client.Id, message), () => socket.Connected && !linkedToken.IsCancellationRequested, linkedToken).ConfigureAwait(false);
            }, linkedToken);

            var writeTask = Task.Run(async () => {
                var headerBuffer = new byte[4];
                while (socket.Connected && !linkedToken.IsCancellationRequested) {
                    // wait for new packets.
                    client.WriteEvent.Wait(linkedToken);
                    client.WriteEvent.Reset();

                    // write queued message to stream
                    while (client.WriteQueue.TryDequeue(out var message))
                        await WriteMessageToStreamAsync(stream, message, linkedToken).ConfigureAwait(false);
                }
            }, linkedToken);

            Task.WhenAny(readTask, writeTask).ContinueWith(_ => {
                // handle client disconnect or failure
                _clients.TryRemove(client.Id);
                ClientDisconnected?.Invoke(client.Id);

                // ensure both tasks finish
                clientCancellationTokenSource.Cancel();

                // dispose resources
                clientCancellationTokenSource.Dispose();
                client.Dispose();
                stream.Dispose();
            });
        }
        private void HandleClient2(ClientToken client) {
            var clientCancellationTokenSource = new CancellationTokenSource();
            var linkedToken = GetLinkedCancellationToken(clientCancellationTokenSource.Token);

            var socket = client.Socket;
            var stream = socket.GetStream();

            var readTask = Task.Run(() => {
                ReadMessagesFromStream(stream, message => OnMessageReceived(client.Id, message), () => socket.Connected && !linkedToken.IsCancellationRequested);
            }, linkedToken);

            var writeTask = Task.Run(() => {
                Span<byte> headerBuffer = stackalloc byte[4];
                while (socket.Connected && !linkedToken.IsCancellationRequested) {
                    // wait for new packets.
                    client.WriteEvent.Wait(linkedToken);
                    client.WriteEvent.Reset();

                    // write queued message to stream
                    while (client.WriteQueue.TryDequeue(out var message))
                        WriteMessageToStream(stream, message.Span);
                }
            }, linkedToken);

            Task.WhenAny(readTask, writeTask).ContinueWith(_ => {
                // handle client disconnect or failure
                _clients.TryRemove(client.Id);
                OnClientDisconnected(client.Id);

                // ensure both tasks finish
                clientCancellationTokenSource.Cancel();

                // dispose resources
                clientCancellationTokenSource.Dispose();
                client.Dispose();
                stream.Dispose();
            });
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
            EnsureRunning();
            IsRunning = false;

            _listener.Stop();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            foreach (var client in _clients.Values) {
                client.Socket.Close();
                client.Dispose();
            }
            _clients.Clear();
        }

        protected void EnsureRunning() {
            if (IsStopped)
                throw new InvalidOperationException("Server is not running.");
        }
        protected void EnsureStopped() {
            if (IsRunning)
                throw new InvalidOperationException("Server is already running.");
        }
        private ClientToken GetClientToken(long clientId) {
            if (!_clients.TryGetValue(clientId, out var client))
                throw new InvalidOperationException("Target client id is not valid.");
            return client;
        }

        public override void Dispose() {
            if (IsRunning)
                Stop();
        }

        private class ClientToken : IDisposable {

            public TcpClient Socket;
            public long Id;
            public ConcurrentQueue<ReadOnlyMemory<byte>> WriteQueue;
            public ManualResetEventSlim WriteEvent;

            public ClientToken(long clientId, TcpClient socket) {
                Socket = socket;
                Id = clientId;
                WriteQueue = new ConcurrentQueue<ReadOnlyMemory<byte>>();
                WriteEvent = new ManualResetEventSlim(false);
            }

            public void EnqueueMessage(ReadOnlyMemory<byte> message) {
                WriteQueue.Enqueue(message);
                WriteEvent.Set();
            }

            public void Dispose() {
                Socket?.Dispose();
                WriteEvent?.Dispose();
            }
        }
    }
}
