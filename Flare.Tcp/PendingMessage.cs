using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Memowned;

namespace Flare.Tcp {
    internal readonly struct PendingMessage : IDisposable {
        public readonly ReadOnlyOwnedMemory<byte, IMemoryOwner<byte>> Content;
        public readonly CancellationToken CancellationToken;
        private readonly TaskCompletionSource? messageSentTaskSource;

        private PendingMessage(ReadOnlyOwnedMemory<byte, IMemoryOwner<byte>> content, CancellationToken cancellationToken, TaskCompletionSource? messageSentTaskSource) {
            Content = content;
            CancellationToken = cancellationToken;
            this.messageSentTaskSource = messageSentTaskSource;
        }

        public static PendingMessage Create(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken = default) =>
            Create(ReadOnlyOwnedMemory<byte, IMemoryOwner<byte>>.Unowned(memory), cancellationToken);
        public static PendingMessage Create(IMemoryOwner<byte> memoryOwner, CancellationToken cancellationToken = default) =>
            Create(ReadOnlyOwnedMemory<byte, IMemoryOwner<byte>>.Owned(memoryOwner.Memory, memoryOwner), cancellationToken);
        private static PendingMessage Create(ReadOnlyOwnedMemory<byte, IMemoryOwner<byte>> content, CancellationToken cancellationToken = default) =>
            new(content, cancellationToken, null);

        public static PendingMessage CreateAwaitable(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken = default) =>
            CreateAwaitable(ReadOnlyOwnedMemory<byte, IMemoryOwner<byte>>.Unowned(memory), cancellationToken);
        public static PendingMessage CreateAwaitable(IMemoryOwner<byte> memoryOwner, CancellationToken cancellationToken = default) =>
            CreateAwaitable(ReadOnlyOwnedMemory<byte, IMemoryOwner<byte>>.Owned(memoryOwner.Memory, memoryOwner), cancellationToken);
        private static PendingMessage CreateAwaitable(ReadOnlyOwnedMemory<byte, IMemoryOwner<byte>> content, CancellationToken cancellationToken = default) {
            var taskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new(content, cancellationToken, taskSource);
        }

        public void TrySetSent() {
            messageSentTaskSource?.TrySetResult();
        }

        public Task WaitUntilSentAsync() {
            if (messageSentTaskSource is null)
                throw new InvalidOperationException("message is not awaitable.");

            return Core(messageSentTaskSource, CancellationToken);

            static async Task Core(TaskCompletionSource taskCompletionSource, CancellationToken cancellationToken) {
                using var registration = cancellationToken.Register(taskSource => ((TaskCompletionSource?)taskSource)?.TrySetCanceled(), taskCompletionSource);
                await taskCompletionSource.Task.ConfigureAwait(false);
            }
        }

        public void Dispose() {
            Content.Dispose();
        }
    }
}