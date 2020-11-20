using System;
using System.Threading.Tasks;

namespace Flare.Tcp {
    internal readonly struct PendingMessage {
        public readonly ReadOnlyMemory<byte> MessageContent;
        public readonly TaskCompletionSource? SendTask;

        private PendingMessage(ReadOnlyMemory<byte> messageContent, TaskCompletionSource? sendTask = null) {
            MessageContent = messageContent;
            SendTask = sendTask;
        }

        public static PendingMessage Create(ReadOnlyMemory<byte> messageContent) => new(messageContent);
        public static (PendingMessage, TaskCompletionSource) CreateWithWait(ReadOnlyMemory<byte> messageContent) {
            var taskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return (new PendingMessage(messageContent, taskSource), taskSource);
        }
    }
}