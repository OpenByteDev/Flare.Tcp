using BenchmarkDotNet.Running;
using System;

namespace Flare.Tcp.Benchmark {
    public static class Program {
        public static void Main() {
            BenchmarkRunner.Run<MessageRoundtripSyncBenchmark>();
            BenchmarkRunner.Run<MessageRoundtripAsyncBenchmark>();
            Console.Read();
        }
    }
}
