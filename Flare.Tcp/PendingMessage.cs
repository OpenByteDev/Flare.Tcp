using System;
using System.Threading.Tasks;
using Flare.Tcp.Extensions;

namespace Flare.Tcp {
    internal readonly struct PendingMessage {
        public readonly ReadOnlyMemory<byte> Content;
        private readonly TaskCompletionSource? _sendTask;

        private PendingMessage(ReadOnlyMemory<byte> messageContent, TaskCompletionSource? sendTask = null) {
            Content = messageContent;
            _sendTask = sendTask;
        }

        public static PendingMessage Create(ReadOnlyMemory<byte> messageContent) => new(messageContent);
        public static PendingMessage CreateWithWait(ReadOnlyMemory<byte> messageContent) {
            var taskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new PendingMessage(messageContent, taskSource);
        }

        public void SetSent() => _sendTask?.TrySetResult();
        public void WaitForSend() => WaitForSendAsync().WaitAndUnwrap();
        public Task WaitForSendAsync() => _sendTask?.Task ?? throw new InvalidOperationException("Message does not support awaiting.");
    }
}