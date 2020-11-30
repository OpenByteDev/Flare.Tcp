﻿using Flare.Tcp.Extensions;
using Microsoft.Toolkit.HighPerformance.Buffers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ValueTaskSupplement;
using static Flare.Tcp.ThreadSafeGuard;

namespace Flare.Tcp {
    public class ConcurrentFlareTcpServer : FlareTcpServerBase {
        private readonly ConcurrentDictionary<long, ClientToken> _clients = new();
        private readonly ThreadSafeGuard _listenGuard = new();
        private long _nextClientId /* = 0 */;

        public int ClientCount => _clients.Count;

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(long clientId, MemoryOwner<byte> message);

        public event ClientConnectedEventHandler? ClientConnected;
        public delegate void ClientConnectedEventHandler(long clientId);

        public event ClientDisconnectedEventHandler? ClientDisconnected;
        public delegate void ClientDisconnectedEventHandler(long clientId);

        protected virtual void OnMessageReceived(long clientId, MemoryOwner<byte> message) {
            MessageReceived?.Invoke(clientId, message);
        }

        protected virtual void OnClientConnected(long clientId) {
            ClientConnected?.Invoke(clientId);
        }

        protected virtual void OnClientDisconnected(long clientId) {
            ClientDisconnected?.Invoke(clientId);
        }

        public void Listen(int port) {
            using var token = StartListening();
            StartListener(port);
            ListenInternal();
        }
        public void Listen(IPAddress address, int port) {
            using var token = StartListening();
            StartListener(address, port);
            ListenInternal();
        }
        public void Listen(IPEndPoint endPoint) {
            using var token = StartListening();
            StartListener(endPoint);
            ListenInternal();
        }
        private void ListenInternal() {
            while (IsRunning) {
                try {
                    var client = Server!.AcceptTcpClient();
                    AcceptClient(client);
                } catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted || e.SocketErrorCode == SocketError.OperationAborted) {
                    return;
                }
            }
        }

        public async Task ListenAsync(int port, CancellationToken cancellationToken = default) {
            using var token = StartListening();
            StartListener(port);
            await ListenInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        public async Task ListenAsync(IPAddress address, int port, CancellationToken cancellationToken = default) {
            using var token = StartListening();
            StartListener(address, port);
            await ListenInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        public async Task ListenAsync(IPEndPoint endPoint, CancellationToken cancellationToken = default) {
            using var token = StartListening();
            StartListener(endPoint);
            await ListenInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        private async Task ListenInternalAsync(CancellationToken cancellationToken = default) {
            while (IsRunning) {
                try {
                    var client = await Server!.AcceptTcpClientAsync().ConfigureAwait(false);
                    AcceptClient(client);
                } catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) {
                    return;
                } catch (SocketException e) when (e.SocketErrorCode == SocketError.Interrupted || e.SocketErrorCode == SocketError.OperationAborted) {
                    return;
                }
            }
        }

        protected long GetNextClientId() => _nextClientId;
        protected long GetAndIncrementNextClientId() => Interlocked.Increment(ref _nextClientId) - 1;

        protected virtual void AcceptClient(TcpClient socket) {
            var clientId = GetAndIncrementNextClientId();
            var client = new ClientToken(clientId, socket);
            var added = _clients.TryAdd(clientId, client);
            Debug.Assert(added, "Client id already used when it should not.");
            OnClientConnected(clientId);
            HandleClient(client);
        }

        private void HandleClient(ClientToken client) {
            client.Socket.Disconnected += () => OnClientDisconnected(client.Id);
            client.Socket.MessageReceived += message => OnMessageReceived(client.Id, message);
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

        public void EnqueueMessage(long clientId, ReadOnlyMemory<byte> message) {
            var client = GetClientToken(clientId);
            client.Socket.EnqueueMessage(message);
        }
        public ValueTask EnqueueMessageAsync(long clientId, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            var client = GetClientToken(clientId);
            return client.Socket.EnqueueMessageAsync(message, cancellationToken);
        }

        public void EnqueueMessageAndWait(long clientId, ReadOnlyMemory<byte> message) {
            var client = GetClientToken(clientId);
            client.Socket.EnqueueMessageAndWait(message);
        }
        public Task EnqueueMessageAndWaitAsync(long clientId, ReadOnlyMemory<byte> message) {
            var client = GetClientToken(clientId);
            return client.Socket.EnqueueMessageAndWaitAsync(message);
        }

        public void EnqueueMessages(long clientId, IEnumerable<ReadOnlyMemory<byte>> messages) {
            var client = GetClientToken(clientId);
            client.Socket.EnqueueMessages(messages);
        }
        public ValueTask EnqueueMessagesAsync(long clientId, IEnumerable<ReadOnlyMemory<byte>> messages, CancellationToken cancellationToken = default) {
            var client = GetClientToken(clientId);
            return client.Socket.EnqueueMessagesAsync(messages, cancellationToken);
        }

        public void EnqueueBroadcastMessage(ReadOnlyMemory<byte> message) {
            foreach (var (_, clientToken) in _clients)
                clientToken.Socket.EnqueueMessage(message);
        }
        public ValueTask EnqueueBroadcastMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            return ValueTaskEx.WhenAll(_clients.Values.Select(client => client.Socket.EnqueueMessageAsync(message, cancellationToken)));
        }

        public void EnqueueBroadcastMessages(IEnumerable<ReadOnlyMemory<byte>> messages) {
            if (messages is null)
                throw new ArgumentNullException(nameof(messages));

            foreach (var (_, clientToken) in _clients)
                foreach (var message in messages)
                    clientToken.Socket.EnqueueMessage(message);
        }
        public ValueTask EnqueueBroadcastMessagesAsync(IEnumerable<ReadOnlyMemory<byte>> messages, CancellationToken cancellationToken = default) {
            return ValueTaskEx.WhenAll(_clients.Values.Select(client => client.Socket.EnqueueMessagesAsync(messages, cancellationToken)));
        }

        public void Stop() {
            StopListener();

            _listenGuard.Unset();
            _clients.Clear();
        }

        private ThreadSafeGuardToken StartListening() {
            EnsureStopped();
            return _listenGuard.UseOrThrow(() => new InvalidOperationException("The server is already listening."));
        }
        private ClientToken GetClientToken(long clientId) {
            if (!_clients.TryGetValue(clientId, out var client))
                throw new InvalidOperationException("Target client id is not valid.");
            return client;
        }

        private class ClientToken : IDisposable {
            public readonly ConcurrentFlareTcpClient Socket;
            public readonly long Id;

            public ClientToken(long clientId, TcpClient socket) {
                Socket = new ConcurrentFlareTcpClient(socket);
                Id = clientId;
            }

            public void Close() {
                Socket.Disconnect();
            }

            public void Dispose() {
                Socket?.Dispose();
            }
        }
    }
}