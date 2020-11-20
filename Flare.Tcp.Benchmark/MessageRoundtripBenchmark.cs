using BenchmarkDotNet.Attributes;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Flare.Tcp.Benchmark {
    public class MessageRoundtripBenchmark {

        private FlareTcpServer server;
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

            server = new FlareTcpServer(8888);
            client = new FlareTcpClient();
            server.MessageReceived += (clientId, message) => {
                server.EnqueueMessage(clientId, message.ToArray());
            };
            _ = Task.Run(() => server.ListenAsync());
            client.Connect(IPAddress.Loopback, 8888);
        }

        [Benchmark]
        public async Task MessageRoundtripAsync() {
            for (var i=0; i<MessageCount; i++) {
                await client.WriteMessageAsync(data).ConfigureAwait(false);
                await client.ReadNextMessageAsync(delegate { }).ConfigureAwait(false);
            }
        }

        [Benchmark]
        public void MessageRoundtripSync() {
            for (var i = 0; i < MessageCount; i++) {
                client.WriteMessage(data);
                client.ReadNextMessage();
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
