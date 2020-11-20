using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Flare.Tcp {
    internal class MultiProducerSingleConsumerQueue<T> : IDisposable {

        private readonly ConcurrentQueue<T> _queue = new();
        private readonly ManualResetEventSlim _unconsumedEvent = new(false);

        public void Wait(CancellationToken cancellationToken = default) {
            if (!_queue.IsEmpty)
                return;
            _unconsumedEvent.Wait(cancellationToken);
            _unconsumedEvent.Reset();
        }
        public bool TryDequeue([MaybeNullWhen(false)] out T result) {
            return _queue.TryDequeue(out result);
        }
        public void Enqueue(T item) {
            _queue.Enqueue(item);
            _unconsumedEvent.Set();
        }

        public void Dispose() {
            _queue?.Clear();
            _unconsumedEvent?.Dispose();
        }
    }
}
