using Flare.Tcp.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ValueTaskSupplement;

namespace Flare.Tcp {
    public class ConcurrentFlareTcpClient : FlareTcpClientBase {
        private readonly Channel<PendingMessage> _pendingMessages = Channel.CreateUnbounded<PendingMessage>(new UnboundedChannelOptions() { SingleReader = true });

        public event MessageReceivedEventHandler? MessageReceived;
        public delegate void MessageReceivedEventHandler(Span<byte> message);

        protected virtual void OnMessageReceived(Span<byte> message) {
            MessageReceived?.Invoke(message);
        }

        protected override void OnConnected() {
            base.OnConnected();

            var clientCancellationTokenSource = new CancellationTokenSource();
            var linkedToken = GetLinkedCancellationToken(clientCancellationTokenSource.Token);

            var stream = Client!.GetStream();

            var readTask = TaskUtils.StartLongRunning(async () => {
                using var reader = new MessageStreamReader(stream);
                while (IsConnected && !linkedToken.IsCancellationRequested)
                    await reader.ReadMessageAsync(OnMessageReceived, linkedToken);
            }, linkedToken);

            var writeTask = TaskUtils.StartLongRunning(async () => {
                var writer = new MessageStreamWriter(stream);
                await foreach (var message in _pendingMessages.Reader.ReadAllAsync(linkedToken))
                    await writer.WriteMessageAsync(message.MessageContent, linkedToken);
            }, linkedToken);

            Task.WhenAny(readTask, writeTask).ContinueWith(_ => {
                // handle client disconnect or failure
                OnDisconnected();

                // ensure both tasks complete
                clientCancellationTokenSource.Cancel();
                clientCancellationTokenSource.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            Task.WhenAll(readTask, writeTask).ContinueWith(_ => {
                _pendingMessages.Writer.Complete();

                // dispose stream
                stream.Close();
                stream.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        public void EnqueueMessage(ReadOnlyMemory<byte> message) {
            EnqueueMessage(PendingMessage.Create(message));
        }
        public ValueTask EnqueueMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            return EnqueueMessageAsync(PendingMessage.Create(message), cancellationToken);
        }

        public void EnqueueMessageAndWait(ReadOnlyMemory<byte> message) {
            EnqueueMessageAndWaitAsync(message).WaitAndUnwrap();
        }
        public async Task EnqueueMessageAndWaitAsync(ReadOnlyMemory<byte> message) {
            var (pending, taskSource) = PendingMessage.CreateWithWait(message);
            await EnqueueMessageAsync(in pending);
            await taskSource.Task;
        }

        public void EnqueueMessages(IEnumerable<ReadOnlyMemory<byte>> messages) {
            foreach (var message in messages)
                EnqueueMessage(message);
        }
        public ValueTask EnqueueMessagesAsync(IEnumerable<ReadOnlyMemory<byte>> messages, CancellationToken cancellationToken = default) {
            var linkedToken = GetLinkedCancellationToken(cancellationToken);

            return ValueTaskEx.WhenAll(messages.Select(message => EnqueueMessageAsync(PendingMessage.Create(message), linkedToken)));
        }

        private void EnqueueMessage(in PendingMessage message) {
            _pendingMessages.Writer.TryWrite(message);
        }
        private ValueTask EnqueueMessageAsync(in PendingMessage message, CancellationToken cancellationToken = default) {
            return _pendingMessages.Writer.WriteAsync(message, cancellationToken);
        }
    }
}
