using BenchmarkDotNet.Attributes;
using System;
using System.Buffers;
using System.Net;
using System.Threading.Tasks;

namespace Flare.Tcp.Benchmark {
    public class MessageRoundtripBenchmark {
        private ConcurrentFlareTcpServer server;
        private FlareTcpClient client;
        private byte[] data;

        [Params(1, 1_000, 1_000_000)]
        public int MessageBytes;

        [Params(1, 1_000)]
        public int MessageCount;

        [GlobalSetup]
        public void Setup() {
            var random = new Random();
            data = new byte[MessageBytes];
            random.NextBytes(data);

            server = new ConcurrentFlareTcpServer();
            client = new FlareTcpClient();
            server.MessageReceived += (clientId, message) => {
                Task.Run(async () => {
                    using (message)
                        await server.EnqueueMessageAndWaitAsync(clientId, message.Memory).ConfigureAwait(false);
                });
            };
            _ = Task.Run(() => server.ListenAsync(8888));
            client.Connect(IPAddress.Loopback, 8888);
        }

        [Benchmark]
        public async Task MessageRoundtripAsync() {
            for (var i = 0; i < MessageCount; i++) {
                await client.WriteMessageAsync(data).ConfigureAwait(false);
                using var message = await client.ReadNextMessageAsync().ConfigureAwait(false);
            }
        }

        [Benchmark]
        public void MessageRoundtripSync() {
            for (var i = 0; i < MessageCount; i++) {
                client.WriteMessage(data);
                using var message = client.ReadNextMessage();
            }
        }

        [Benchmark]
        public void MessageRoundtripSyncIntoSpan() {
            for (var i = 0; i < MessageCount; i++) {
                client.WriteMessage(data);
                using var message = client.ReadNextMessageIntoSpan();
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
