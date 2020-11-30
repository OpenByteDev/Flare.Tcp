using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Flare.Tcp.Extensions;
using Microsoft.Toolkit.HighPerformance.Buffers;
using ValueTaskSupplement;

namespace Flare.Tcp {
    public class ConcurrentFlareTcpClient : FlareTcpClientBase {
        private readonly Channel<PendingMessage> _pendingMessages = Channel.CreateUnbounded<PendingMessage>(new UnboundedChannelOptions() { SingleReader = true });

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(MemoryOwner<byte> message);

        protected virtual void OnMessageReceived(MemoryOwner<byte> message) {
            MessageReceived?.Invoke(message);
        }

        protected override void OnConnected() {
            base.OnConnected();

            var clientCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = clientCancellationTokenSource.Token;

            var stream = Client!.GetStream();
            var readTask = TaskUtils.StartLongRunning(ReadLoop, cancellationToken);
            var writeTask = TaskUtils.StartLongRunning(WriteLoop, cancellationToken);

            Task.WhenAny(readTask, writeTask).ContinueWith(_ => {
                // handle client disconnect or failure
                OnDisconnected();

                // ensure both tasks complete
                clientCancellationTokenSource.Cancel();
                clientCancellationTokenSource.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            Task.WhenAll(readTask, writeTask).ContinueWith(_ => {
                _pendingMessages.Writer.Complete();

                // close and dispose stream
                stream.Close();
                stream.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            void ReadLoop() {
                var reader = new MessageStreamReader(stream!);
                while (IsConnected && !cancellationToken.IsCancellationRequested) {
                    var message = reader.ReadMessage();
                    OnMessageReceived(message);
                }
            }
            async Task WriteLoop() {
                var writer = new MessageStreamWriter(stream!);
                while (await _pendingMessages.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
                    // Fast loop around available jobs
                    while (_pendingMessages.Reader.TryRead(out var message)) {
                        writer.WriteMessage(message.Content.Span);
                        message.SetSent();
                    }
                }
            }
            /*
            async Task ReadLoop() {
                var reader = new MessageStreamReader(stream!);
                while (IsConnected && !cancellationToken.IsCancellationRequested) {
                    var message = await reader.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                    OnMessageReceived(message);
                }
            }
            async Task WriteLoop() {
                var writer = new MessageStreamWriter(stream!);
                await foreach (var message in _pendingMessages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                    await writer.WriteMessageAsync(message.Content, cancellationToken).ConfigureAwait(false);
                    message.SetSent();
                }
            }
            */
        }

        public void EnqueueMessage(ReadOnlyMemory<byte> message) =>
            EnqueueMessage(PendingMessage.Create(message));
        public ValueTask EnqueueMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) =>
            EnqueueMessageAsync(PendingMessage.Create(message), cancellationToken);

        public void EnqueueMessages(IEnumerable<ReadOnlyMemory<byte>> messages) {
            if (messages is null)
                throw new ArgumentNullException(nameof(messages));

            foreach (var message in messages)
                EnqueueMessage(message);
        }
        public ValueTask EnqueueMessagesAsync(IEnumerable<ReadOnlyMemory<byte>> messages, CancellationToken cancellationToken = default) =>
            ValueTaskEx.WhenAll(messages.Select(message => EnqueueMessageAsync(PendingMessage.Create(message), cancellationToken)));

        public void EnqueueMessageAndWait(ReadOnlyMemory<byte> message) {
            var pending = PendingMessage.CreateWithWait(message);
            EnqueueMessage(in pending);
            pending.WaitForSend();
        }
        public async Task EnqueueMessageAndWaitAsync(ReadOnlyMemory<byte> message) {
            var pending = PendingMessage.CreateWithWait(message);
            await EnqueueMessageAsync(in pending).ConfigureAwait(false);
            await pending.WaitForSendAsync().ConfigureAwait(false);
        }

        public void EnqueueMessagesAndWait(IEnumerable<ReadOnlyMemory<byte>> messages) =>
            EnqueueMessagesAndWaitAsync(messages).WaitAndUnwrap();
        public Task EnqueueMessagesAndWaitAsync(IEnumerable<ReadOnlyMemory<byte>> messages) =>
            Task.WhenAll(messages.Select(message => EnqueueMessageAndWaitAsync(message)));

        private void EnqueueMessage(in PendingMessage message) =>
            _pendingMessages.Writer.TryWrite(message);
        private ValueTask EnqueueMessageAsync(in PendingMessage message, CancellationToken cancellationToken = default) =>
            _pendingMessages.Writer.WriteAsync(message, cancellationToken);
    }
}
