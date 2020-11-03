using NUnit.Framework;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Basic.Tcp.Test {
    [TestFixture]
    public static class BasicTests {

        [Test]
        public static async Task ClientConnectionEventsTrigger() {
            using var connectedEvent = new ManualResetEventSlim(false);
            using var disconnectedEvent = new ManualResetEventSlim(false);

            using var server = new BasicTcpServer(8888);
            server.ClientConnected += _ => {
                Assert.IsFalse(connectedEvent.IsSet, "ClientConnected raised twice.");
                connectedEvent.Set();
            };
            server.ClientDisconnected += _ => {
                Assert.IsFalse(disconnectedEvent.IsSet, "ClientDisconnected raised twice.");
                disconnectedEvent.Set();
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new BasicTcpClient();
            await client.ConnectAsync(IPAddress.Loopback, 8888).ConfigureAwait(false);
            Assert.IsTrue(connectedEvent.Wait(TimeSpan.FromSeconds(5)), "ClientConnected not raised.");
            client.Disconnect();

            Assert.IsTrue(disconnectedEvent.Wait(TimeSpan.FromSeconds(5)), "ClientDisconnected not raised.");
            server.Stop();
        }

        [Test]
        public static async Task SimpleClientServerMessageTransfer() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new BasicTcpServer(8888);
            server.MessageReceived += (_, message) => {
                var decoded = encoding.GetString(message);
                Assert.AreEqual(testMessage, decoded);
                messageReceivedEvent.Set();
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new BasicTcpClient();
            await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8888));
            await client.SendMessageAsync(encoding.GetBytes(testMessage));

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static async Task SimpleServerClientMessageTransfer() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new BasicTcpServer(8888);
            server.ClientConnected += clientId => {
                server.EnqueueMessage(clientId, encoding.GetBytes(testMessage));
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new BasicTcpClient();
            client.MessageReceived += message => {
                var decoded = encoding.GetString(message);
                Assert.AreEqual(testMessage, decoded);
                messageReceivedEvent.Set();
            };
            await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8888));
            await client.ReadMessageAsync();

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(50)));
            client.Disconnect();
            server.Stop();
        }


        [Test]
        public static async Task MultipleBidirectionalMessageTransferWithMultipleClients() {
            const int messageCount = 100;
            const int clientCount = 5;

            using var server = new BasicTcpServer(8888);
            server.MessageReceived += (clientId, message) => {
                TestContext.WriteLine("server received");
                server.EnqueueMessage(clientId, message.ToArray());
            };
            _ = Task.Run(() => server.ListenAsync());

            using var clientCountdown = new CountdownEvent(clientCount);
            var taskList = new Task[clientCount];
            for (var i=0; i<taskList.Length; i++) {
                taskList[i] = Task.Run(async () => {
                    using var messageCountdown = new CountdownEvent(messageCount);
                    using var client = new BasicTcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, 8888);
                    client.MessageReceived += _ => {
                        TestContext.WriteLine("client received");
                        messageCountdown.Signal();
                    };
                    _ = Task.Run(async () => {
                        for (var i = 0; i < messageCount; i++)
                            await client.SendMessageAsync(Encoding.UTF8.GetBytes(i.ToString()));
                    });
                    _ = Task.Run(() => client.ReadMessagesAsync());
                    Assert.IsTrue(messageCountdown.Wait(TimeSpan.FromSeconds(10)));
                    client.Disconnect();
                    clientCountdown.Signal();
                });
            }
            await Task.WhenAll(taskList);

            Assert.IsTrue(clientCountdown.Wait(TimeSpan.FromSeconds(10)));
            server.Stop();
        }


        [Test]
        public static async Task MultipleSimultaneousClientReadsFail() {
            using var server = new BasicTcpServer(8888);
            _ = Task.Run(() => server.ListenAsync());

            using var client = new BasicTcpClient();
            await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8888));

            var read1 = Task.Run(() => client.ReadMessagesAsync());
            var read2 = Task.Run(() => client.ReadMessagesAsync());
            var first = await Task.WhenAny(read1, read2);
            var second = first == read1 ? read2 : read1;
            Assert.IsTrue(first.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(first.Exception.GetBaseException());
            Assert.IsFalse(second.IsCompleted);
            Assert.IsFalse(second.IsFaulted);

            client.Disconnect();
            server.Stop();
        }
    }
}
