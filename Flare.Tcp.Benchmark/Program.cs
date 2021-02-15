using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;

namespace Flare.Tcp.Benchmark {
    public static class Program {
        public static void Main(string[] args) {
            var config = DefaultConfig.Instance
                .AddDiagnoser(MemoryDiagnoser.Default);
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}
