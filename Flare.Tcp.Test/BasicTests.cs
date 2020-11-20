using NUnit.Framework;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp.Test {
    [TestFixture]
    public static class BasicTests {

        [Test]
        public static void ServerConnectionEvents() {
            using var connectedEvent = new ManualResetEventSlim(false);
            using var disconnectedEvent = new ManualResetEventSlim(false);

            using var server = new FlareTcpServer(8888);
            _ = Task.Run(() => server.ListenAsync());

            server.ClientConnected += _ => {
                Assert.IsFalse(connectedEvent.IsSet, "ClientConnected raised twice.");
                connectedEvent.Set();
            };
            server.ClientDisconnected += _ => {
                Assert.IsFalse(disconnectedEvent.IsSet, "ClientDisconnected raised twice.");
                disconnectedEvent.Set();
            };

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            Assert.IsTrue(connectedEvent.Wait(TimeSpan.FromSeconds(5)), "ClientConnected not raised.");

            client.Disconnect();
            Assert.IsTrue(disconnectedEvent.Wait(TimeSpan.FromSeconds(5)), "ClientDisconnected not raised.");

            server.Stop();
        }

        [Test]
        public static void ClientConnectionEvents() {
            using var connectedEvent = new ManualResetEventSlim(false);
            using var disconnectedEvent = new ManualResetEventSlim(false);

            using var server = new FlareTcpServer(8888);
            _ = Task.Run(() => server.ListenAsync());
            using var client = new ConcurrentFlareTcpClient();

            server.ClientConnected += _ => {
                Assert.IsFalse(connectedEvent.IsSet, "ClientConnected raised twice.");
                connectedEvent.Set();
            };
            server.ClientDisconnected += _ => {
                Assert.IsFalse(disconnectedEvent.IsSet, "ClientDisconnected raised twice.");
                disconnectedEvent.Set();
            };

            client.Connect(IPAddress.Loopback, 8888);
            Assert.IsTrue(connectedEvent.Wait(TimeSpan.FromSeconds(5)), "ClientConnected not raised.");

            client.Disconnect();
            Assert.IsTrue(disconnectedEvent.Wait(TimeSpan.FromSeconds(5)), "ClientDisconnected not raised.");

            server.Stop();
        }

        [Test]
        public static void ClientConnectionEventsTriggerSync() {
            using var connectedEvent = new ManualResetEventSlim(false);
            using var disconnectedEvent = new ManualResetEventSlim(false);

            using var server = new FlareTcpServer(8888);
            server.ClientConnected += _ => {
                Assert.IsFalse(connectedEvent.IsSet, "ClientConnected raised twice.");
                connectedEvent.Set();
            };
            server.ClientDisconnected += _ => {
                Assert.IsFalse(disconnectedEvent.IsSet, "ClientDisconnected raised twice.");
                disconnectedEvent.Set();
            };
            _ = Task.Run(() => server.Listen());

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            Assert.IsTrue(connectedEvent.Wait(TimeSpan.FromSeconds(5)), "ClientConnected not raised.");

            client.Disconnect();
            Assert.IsTrue(disconnectedEvent.Wait(TimeSpan.FromSeconds(5)), "ClientDisconnected not raised.");
            server.Stop();
        }

        [Test]
        public static async Task SimpleClientServerMessageTransferAsync() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (_, message) => {
                var decoded = encoding.GetString(message);
                Assert.AreEqual(testMessage, decoded);
                messageReceivedEvent.Set();
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new FlareTcpClient();
            await client.ConnectAsync(IPAddress.Loopback, 8888);
            await client.WriteMessageAsync(encoding.GetBytes(testMessage));

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static async Task SimpleMessageRoundtripTransferAsyncConcurrent() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (clientId, message) => {
                server.EnqueueMessage(clientId, message.ToArray());
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new ConcurrentFlareTcpClient();
            client.MessageReceived += _ => {
                messageReceivedEvent.Set();
            };
            await client.ConnectAsync(IPAddress.Loopback, 8888);
            client.EnqueueMessage(encoding.GetBytes(testMessage));

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static async Task SimpleMessageRoundtripTransferAsync() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (clientId, message) => {
                server.EnqueueMessage(clientId, message.ToArray());
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new FlareTcpClient();
            await client.ConnectAsync(IPAddress.Loopback, 8888);
            await client.WriteMessageAsync(encoding.GetBytes(testMessage));
            await client.ReadNextMessageAsync(_ => messageReceivedEvent.Set());

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static async Task SimpleMessageRoundtripTransferSync() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (clientId, message) => {
                server.EnqueueMessage(clientId, message.ToArray());
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new FlareTcpClient();
            await client.ConnectAsync(IPAddress.Loopback, 8888);
            client.WriteMessage(encoding.GetBytes(testMessage));
            client.ReadNextMessage();
            messageReceivedEvent.Set();

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static async Task SimpleClientServerMessageTransferAsyncConcurrent() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (_, message) => {
                var decoded = encoding.GetString(message);
                Assert.AreEqual(testMessage, decoded);
                messageReceivedEvent.Set();
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new ConcurrentFlareTcpClient();
            await client.ConnectAsync(IPAddress.Loopback, 8888);
            client.EnqueueMessage(encoding.GetBytes(testMessage));

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static void SimpleClientServerMessageTransferSync() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (_, message) => {
                var decoded = encoding.GetString(message);
                Assert.AreEqual(testMessage, decoded);
                messageReceivedEvent.Set();
            };
            _ = Task.Run(() => server.Listen());

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            client.WriteMessage(encoding.GetBytes(testMessage));

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static async Task SimpleServerClientMessageTransferAsync() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new FlareTcpServer(8888);
            server.ClientConnected += clientId => {
                server.EnqueueMessage(clientId, encoding.GetBytes(testMessage));
            };
            _ = Task.Run(() => server.ListenAsync());

            using var client = new ConcurrentFlareTcpClient();
            client.MessageReceived += message => {
                var decoded = encoding.GetString(message);
                Assert.AreEqual(testMessage, decoded);
                messageReceivedEvent.Set();
            };
            await client.ConnectAsync(IPAddress.Loopback, 8888);

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));

            client.Disconnect();
            server.Stop();
        }
        [Test]
        public static void SimpleServerClientMessageTransferSync() {
            using var messageReceivedEvent = new ManualResetEventSlim(false);

            var encoding = Encoding.UTF8;
            const string testMessage = "Test";

            using var server = new FlareTcpServer(IPAddress.Any, 8888);
            server.ClientConnected += clientId => {
                server.EnqueueMessage(clientId, encoding.GetBytes(testMessage));
            };
            Assert.IsFalse(server.IsListening);
            _ = Task.Run(() => server.Listen());

            using var client = new ConcurrentFlareTcpClient();
            Assert.IsFalse(client.IsConnected);
            Assert.IsFalse(client.IsConnecting);
            client.MessageReceived += message => {
                var decoded = encoding.GetString(message);
                Assert.AreEqual(testMessage, decoded);
                messageReceivedEvent.Set();
            };
            client.Connect(IPAddress.Loopback, 8888);
            Assert.IsTrue(server.IsListening);
            Assert.IsTrue(client.IsConnected);
            Assert.IsFalse(client.IsConnecting);

            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));

            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static async Task MultipleBidirectionalMessageTransferWithMultipleClientsAsync() {
            const int messageCount = 100;
            const int clientCount = 1;

            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (clientId, message) => {
                // TestContext.WriteLine("server received");
                server.EnqueueMessage(clientId, message.ToArray());
            };
            var listenTask = Task.Run(() => server.ListenAsync());

            using var clientCountdown = new CountdownEvent(clientCount);
            var taskList = new Task[clientCount];
            for (var i = 0; i < taskList.Length; i++) {
                taskList[i] = Task.Run(async () => {
                    using var messageCountdown = new CountdownEvent(messageCount);
                    using var client = new ConcurrentFlareTcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, 8888);
                    client.MessageReceived += _ => {
                        // TestContext.WriteLine("client received");
                        messageCountdown.Signal();
                    };
                    for (var i = 0; i < messageCount; i++)
                        client.EnqueueMessage(Encoding.UTF8.GetBytes(i.ToString()));

                    Assert.IsTrue(messageCountdown.Wait(TimeSpan.FromSeconds(10)));
                    client.Disconnect();
                    clientCountdown.Signal();
                });
            }
            foreach (var task in taskList)
                await task;

            Assert.IsTrue(clientCountdown.Wait(TimeSpan.FromSeconds(10)));
            server.Stop();

            await listenTask;
        }
        [Test]
        public static void MultipleBidirectionalMessageTransferWithMultipleClientsSymc() {
            const int messageCount = 100;
            const int clientCount = 5;

            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (clientId, message) => {
                // TestContext.WriteLine("server received");
                server.EnqueueMessage(clientId, message.ToArray());
            };
            _ = Task.Run(() => server.Listen());

            using var clientCountdown = new CountdownEvent(clientCount);
            var taskList = new Task[clientCount];
            for (var i = 0; i < taskList.Length; i++) {
                taskList[i] = Task.Run(() => {
                    using var messageCountdown = new CountdownEvent(messageCount);
                    using var client = new ConcurrentFlareTcpClient();
                    client.Connect(IPAddress.Loopback, 8888);
                    client.MessageReceived += _ => {
                        // TestContext.WriteLine("client received");
                        messageCountdown.Signal();
                    };
                    for (var i = 0; i < messageCount; i++)
                        client.EnqueueMessage(Encoding.UTF8.GetBytes(i.ToString()));

                    Assert.IsTrue(messageCountdown.Wait(TimeSpan.FromSeconds(10)));
                    client.Disconnect();
                    clientCountdown.Signal();
                });
            }

            Assert.IsTrue(clientCountdown.Wait(TimeSpan.FromSeconds(10)));
            server.Stop();
        }

        [Test]
        public static async Task MultipleSimultaneousClientReadsFail() {
            using var server = new FlareTcpServer(8888);
            _ = Task.Run(() => server.ListenAsync());

            using var client = new FlareTcpClient();
            await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8888));

            var read1 = Task.Run(() => client.ReadNextMessageAsync(delegate { }));
            var read2 = Task.Run(() => client.ReadNextMessageAsync(delegate { }));
            var first = await Task.WhenAny(read1, read2);
            var second = first == read1 ? read2 : read1;
            Assert.IsTrue(first.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(first.Exception.GetBaseException());
            Assert.IsFalse(second.IsCompleted);
            Assert.IsFalse(second.IsFaulted);

            client.Disconnect();
            server.Stop();
        }

        [Test]
        public static async Task MultipleSimultaneousClientConnectsFail() {
            using var client = new FlareTcpClient();

            using var barrier = new Barrier(2);
            var connect1 = Task.Run(async () => {
                barrier.SignalAndWait();
                await client.ConnectAsync(IPAddress.Loopback, 8888);
            });
            var connect2 = Task.Run(async () => {
                barrier.SignalAndWait();
                await client.ConnectAsync(IPAddress.Loopback, 8888);
            });
            var first = await Task.WhenAny(connect1, connect2);
            var second = first == connect1 ? connect2 : connect1;
            Assert.IsTrue(first.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(first.Exception.GetBaseException());
            Assert.IsFalse(second.IsFaulted);
        }


        [Test]
        public static void CanReuseServerAndClient() {
            using var server = new FlareTcpServer(8888);
            server.MessageReceived += (clientId, message) => {
                server.EnqueueMessage(clientId, message.ToArray());
            };

            using var client = new FlareTcpClient();

            for (var i = 0; i < 5; i++) {
                var listenTask = Task.Run(() => {
                    server.Listen();
                });
                client.Connect(IPAddress.Loopback, 8888);
                client.WriteMessage(new byte[] { 1, 2, 3, 4 });
                client.ReadNextMessage();

                client.Disconnect();
                server.Stop();

                listenTask.Wait();
            }
        }
    }
}
