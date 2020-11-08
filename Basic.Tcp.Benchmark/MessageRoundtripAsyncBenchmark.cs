using BenchmarkDotNet.Attributes;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Basic.Tcp.Benchmark {
    public class MessageRoundtripAsyncBenchmark {

        private BasicTcpServer server;
        private BasicTcpClient client;
        private byte[] data;

        [Params(1, 10, 100, 1000)]
        public int MessageCount;

        [GlobalSetup]
        public void Setup() {
            var random = new Random();
            data = new byte[1000];
            random.NextBytes(data);

            server = new BasicTcpServer(8888);
            client = new BasicTcpClient();
            server.MessageReceived += (clientId, message) => {
                server.EnqueueMessage(clientId, message.ToArray());
            };
            Task.Run(() => server.ListenAsync());
            client.ConnectAsync(IPAddress.Loopback, 8888).AsTask().GetAwaiter().GetResult();
        }

        [Benchmark]
        public async Task MessageRoundtrip() {
            for (var i = 0; i < MessageCount; i++) {
                await client.SendMessageAsync(data).ConfigureAwait(false);
                await client.ReadMessageAsync().ConfigureAwait(false);
            }
        }

        [GlobalCleanup]
        public void Cleanup() {
            client.Disconnect();
            client.Dispose();
            server.Stop();
            server.Dispose();
        }

    }
}
