using Basic.Tcp.Extensions;
using System;
using System.Collections.Concurrent;
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

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(long clientId, ReadOnlySpan<byte> message);

        public event ClientConnectedEventHandler? ClientConnected;
        public delegate void ClientConnectedEventHandler(long clientId);

        public event ClientDisconnectedEventHandler? ClientDisconnected;
        public delegate void ClientDisconnectedEventHandler(long clientId);

        public BasicTcpServer(int port) {
            _listener = TcpListener.Create(port);
            _clients = new ConcurrentDictionary<long, ClientToken>();
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
                ClientConnected?.Invoke(clientId);
                HandleClient(client);
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
                var headerBuffer = new byte[4];
                while (socket.Connected && !linkedToken.IsCancellationRequested)
                    await ReadMessageFromStreamAsync(stream, message => MessageReceived?.Invoke(client.Id, message), headerBuffer, linkedToken).ConfigureAwait(false);
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

        public void EnqueueMessage(long clientId, ReadOnlyMemory<byte> message) {
            if (!_clients.TryGetValue(clientId, out var client))
                throw new InvalidOperationException("Target client id is not valid.");

            client.EnqueueMessage(message);
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
