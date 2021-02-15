using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Memowned;
using ValueTaskSupplement;

namespace Flare.Tcp {
    public class ConcurrentFlareTcpClient : FlareTcpClientBase {
        private CancellationTokenSource? _cancellationTokenSource;
        private Channel<PendingMessage>? _pendingMessages;

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(RentedMemory<byte> message);

        protected virtual void OnMessageReceived(RentedMemory<byte> message) {
            MessageReceived?.Invoke(message);
        }

        protected internal override void HandleConnect() {
            base.HandleConnect();

            _pendingMessages = Channel.CreateUnbounded<PendingMessage>(new UnboundedChannelOptions() { SingleReader = true });
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            var readTask = TaskUtils.StartLongRunning(ReadLoop, cancellationToken);
            var writeTask = TaskUtils.StartLongRunning(WriteLoop, cancellationToken);

            var readWriteTasks = new Task[] { readTask, writeTask };
            var whenAnyTask = Task.WhenAny(readWriteTasks).ContinueWith(_ => {
                // ensure both tasks complete
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            var whenAllTask = Task.WhenAll(readWriteTasks).ContinueWith(_ => {
                Disconnect();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            void ReadLoop() {
                var reader = new MessageStreamReader(NetworkStream);
                while (IsConnected && !cancellationToken.IsCancellationRequested) {
                    var message = reader.ReadMessage();
                    OnMessageReceived(message);
                }
            }
            async Task WriteLoop() {
                var writer = new MessageStreamWriter(NetworkStream);
                while (await _pendingMessages.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
                    while (_pendingMessages.Reader.TryRead(out var message)) {
                        using (message) {
                            if (message.CancellationToken.IsCancellationRequested)
                                continue;

                            writer.WriteMessage(message.Content.Memory.Span);
                            message.TrySetSent();
                        }
                    }
                }
            }
        }

        public void EnqueueMessage(ReadOnlyMemory<byte> message) =>
            EnqueueMessage(PendingMessage.Create(message));
        public void EnqueueMessage(IMemoryOwner<byte> message) =>
            EnqueueMessage(PendingMessage.Create(message));
        public ValueTask EnqueueMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
            EnqueueMessageAsync(PendingMessage.Create(message, cancellationToken), cancellationToken);
        public ValueTask EnqueueMessageAsync(IMemoryOwner<byte> message, CancellationToken cancellationToken = default) =>
            EnqueueMessageAsync(PendingMessage.Create(message, cancellationToken), cancellationToken);
        public Task EnqueueMessageAndWaitUntilSentAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
            EnqueueMessageAndWaitUntilSentAsync(PendingMessage.CreateAwaitable(message, cancellationToken), cancellationToken);
        public Task EnqueueMessageAndWaitUntilSentAsync(IMemoryOwner<byte> message, CancellationToken cancellationToken = default) =>
            EnqueueMessageAndWaitUntilSentAsync(PendingMessage.CreateAwaitable(message, cancellationToken), cancellationToken);

        public void EnqueueMessages(IEnumerable<ReadOnlyMemory<byte>> messages) {
            if (messages is null)
                throw new ArgumentNullException(nameof(messages));

            foreach (var message in messages)
                EnqueueMessage(message);
        }
        public void EnqueueMessages(IEnumerable<IMemoryOwner<byte>> messages) {
            if (messages is null)
                throw new ArgumentNullException(nameof(messages));

            foreach (var message in messages)
                EnqueueMessage(message);
        }
        public ValueTask EnqueueMessagesAsync(IEnumerable<ReadOnlyMemory<byte>> messages, CancellationToken cancellationToken = default) =>
            ValueTaskEx.WhenAll(messages.Select(message => EnqueueMessageAsync(PendingMessage.Create(message, cancellationToken), cancellationToken)));
        public ValueTask EnqueueMessagesAsync(IEnumerable<IMemoryOwner<byte>> messages, CancellationToken cancellationToken = default) =>
            ValueTaskEx.WhenAll(messages.Select(message => EnqueueMessageAsync(PendingMessage.Create(message, cancellationToken), cancellationToken)));
        public Task EnqueueMessagesAndWaitUntilSentAsync(IEnumerable<ReadOnlyMemory<byte>> messages, CancellationToken cancellationToken = default) =>
            Task.WhenAll(messages.Select(message => EnqueueMessageAndWaitUntilSentAsync(message, cancellationToken)));
        public Task EnqueueMessagesAndWaitUntilSentAsync(IEnumerable<IMemoryOwner<byte>> messages, CancellationToken cancellationToken = default) =>
            Task.WhenAll(messages.Select(message => EnqueueMessageAndWaitUntilSentAsync(message, cancellationToken)));

        private void EnqueueMessage(in PendingMessage message) {
            EnsureConnected();
            _pendingMessages!.Writer.TryWrite(message);
        }
        private ValueTask EnqueueMessageAsync(in PendingMessage message, CancellationToken cancellationToken = default) {
            EnsureConnected();
            return _pendingMessages!.Writer.WriteAsync(message, cancellationToken);
        }
        private async Task EnqueueMessageAndWaitUntilSentAsync(PendingMessage message, CancellationToken cancellationToken = default) {
            await EnqueueMessageAsync(in message, cancellationToken).ConfigureAwait(false);
            await message.WaitUntilSentAsync().ConfigureAwait(false);
        }

        protected override void Cleanup() {
            base.Cleanup();

            if (_cancellationTokenSource is null)
                return;

            if (!_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource.Cancel();

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}
