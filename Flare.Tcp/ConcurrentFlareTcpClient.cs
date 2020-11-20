using Flare.Tcp.Extensions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public class ConcurrentFlareTcpClient : FlareTcpClientBase {
        private readonly MultiProducerSingleConsumerQueue<PendingMessage> _pendingMessages = new ();

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

            var readTask = TaskUtils.StartLongRunning(() => {
                using var reader = new MessageStreamReader(stream);
                while (IsConnected && !linkedToken.IsCancellationRequested)
                    OnMessageReceived(reader.ReadMessage());
            }, linkedToken);

            var writeTask = TaskUtils.StartLongRunning(() => {
                var writer = new MessageStreamWriter(stream);
                while (IsConnected && !linkedToken.IsCancellationRequested) {
                    // wait for new packets.
                    _pendingMessages.Wait(linkedToken);

                    // write queued message to stream
                    while (_pendingMessages.TryDequeue(out var message)) {
                        writer.WriteMessage(message.MessageContent.Span);
                        message.SendTask?.TrySetResult();
                    }
                }
            }, linkedToken);

            Task.WhenAny(readTask, writeTask).ContinueWith(_ => {
                // handle client disconnect or failure
                OnDisconnected();

                // ensure both tasks complete
                clientCancellationTokenSource.Cancel();
                clientCancellationTokenSource.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            Task.WhenAll(readTask, writeTask).ContinueWith(_ => {
                // dispose stream
                stream.Close();
                stream.Dispose();
            }, TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        public void EnqueueMessage(ReadOnlyMemory<byte> message) => EnqueueMessage(PendingMessage.Create(message));
        public void EnqueueMessageAndWait(ReadOnlyMemory<byte> message) {
            EnqueueMessageAndWaitAsync(message).WaitAndUnwrap();
        }
        public Task EnqueueMessageAndWaitAsync(ReadOnlyMemory<byte> message) {
            var (pending, taskSource) = PendingMessage.CreateWithWait(message);
            EnqueueMessage(in pending);
            return taskSource.Task;
        }
        public void EnqueueMessages(IEnumerable<ReadOnlyMemory<byte>> messages) {
            foreach (var message in messages)
                EnqueueMessage(message);
        }
        private void EnqueueMessage(in PendingMessage message) => _pendingMessages.Enqueue(message);
    }
}
