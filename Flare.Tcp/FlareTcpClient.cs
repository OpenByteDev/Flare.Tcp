using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Toolkit.HighPerformance.Buffers;

namespace Flare.Tcp {
    public class FlareTcpClient : FlareTcpClientBase {
        public MessageStreamReader? MessageReader { get; private set; }
        public MessageStreamWriter? MessageWriter { get; private set; }
        private readonly ThreadSafeGuard _readGuard = new();
        private readonly ThreadSafeGuard _writeGuard = new();

        [MemberNotNull(nameof(MessageReader))]
        [MemberNotNull(nameof(MessageWriter))]
        protected override void OnConnected() {
            base.OnConnected();

            MessageReader = new MessageStreamReader(NetworkStream);
            MessageWriter = new MessageStreamWriter(NetworkStream);
        }

        public void WriteMessage(ReadOnlySpan<byte> message) {
            using var token = StartWriting();
            MessageWriter!.WriteMessage(message);
        }
        public async ValueTask WriteMessageAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default) {
            using var token = StartWriting();
            await MessageWriter!.WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }

        public MemoryOwner<byte> ReadNextMessage() {
            using var readToken = StartReading();
            return MessageReader!.ReadMessage();
        }
        public SpanOwner<byte> ReadNextMessageSpanOwner() {
            using var readToken = StartReading();
            return MessageReader!.ReadMessageSpanOwner();
        }
        public bool TryReadNextMessage([NotNullWhen(true)] out MemoryOwner<byte>? message) {
            using var readToken = StartReading();
            return MessageReader!.TryReadMessage(out message);
        }
        public MemoryOwner<byte>? TryReadNextMessage() {
            using var readToken = StartReading();
            return MessageReader!.TryReadMessage();
        }

        public async Task<MemoryOwner<byte>> ReadNextMessageAsync(CancellationToken cancellationToken = default) {
            using var readToken = StartReading();
            // we need to await here because otherwise the token would immediately be disposed.
            return await MessageReader!.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
        }
        public async Task<MemoryOwner<byte>?> TryReadNextMessageAsync(CancellationToken cancellationToken = default) {
            using var readToken = StartReading();
            // we need to await here because otherwise the token would immediately be disposed.
            return await MessageReader!.TryReadMessageAsync(cancellationToken).ConfigureAwait(false);
        }

        private ThreadSafeGuardToken StartReading() {
            EnsureConnected();
            return _readGuard.Use() ?? ThrowAlreadyReading();

            [DoesNotReturn]
            static ThreadSafeGuardToken ThrowAlreadyReading() => throw new InvalidOperationException("A read operation already in progress.");

        }
        private ThreadSafeGuardToken StartWriting() {
            EnsureConnected();
            return _writeGuard.Use() ?? ThrowAlreadyWriting();

            [DoesNotReturn]
            static ThreadSafeGuardToken ThrowAlreadyWriting() => throw new InvalidOperationException("A write operation already in progress.");
        }

        protected override void Cleanup() {
            base.Cleanup();

            MessageReader = null;
            MessageWriter = null;

            // mark as not reading and not writing
            _readGuard.Unset();
            _writeGuard.Unset();
        }
    }
}
