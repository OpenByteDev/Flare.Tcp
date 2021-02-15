using System;
using System.Net;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Flare.Tcp.Test;

namespace Flare.Tcp.Benchmark {
    public class MessageRoundtripBenchmark {
        private ConcurrentFlareTcpServer server;
        private FlareTcpClient client;
        private byte[] data;

        [Params(1, 1_000, 1_000_000)]
        public int MessageBytes = 1;

        [Params(1, 1_000)]
        public int MessageCount = 1000;

        [GlobalSetup]
        public void Setup() {
            var port = Utils.GetRandomClientPort();
            var random = new Random();
            data = new byte[MessageBytes];
            random.NextBytes(data);

            server = new ConcurrentFlareTcpServer();
            client = new FlareTcpClient();
            server.MessageReceived += (clientId, message) => {
                server.EnqueueMessage(clientId, message);
            };
            _ = Task.Run(() => server.ListenAsync(port));
            client.Connect(IPAddress.Loopback, port);
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

        [GlobalCleanup]
        public void Cleanup() {
            client.Disconnect();
            client.Dispose();
            server.Shutdown();
            server.Dispose();
        }
    }
}
