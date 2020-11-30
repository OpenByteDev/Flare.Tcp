using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;

namespace Flare.Tcp.Benchmark {
    public static class Program {
        public static void Main() {
            var config = DefaultConfig.Instance
                .AddDiagnoser(MemoryDiagnoser.Default);
            BenchmarkRunner.Run<MessageRoundtripBenchmark>(config);

            Console.Read();
        }
    }
}
