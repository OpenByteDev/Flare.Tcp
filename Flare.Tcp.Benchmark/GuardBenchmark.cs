using BenchmarkDotNet.Attributes;

namespace Flare.Tcp.Benchmark {
    public class GuardBenchmark {

        private readonly ThreadSafeGuard _guard = new ThreadSafeGuard();

        [Benchmark]
        public void GetAndSetThenUnset() {
            _guard.GetAndSet();
            _guard.Unset();
        }

        [Benchmark]
        public void UseThenDispose() {
            _guard.Use()?.Dispose();
        }
    }
}
