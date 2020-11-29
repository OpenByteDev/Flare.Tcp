using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using NUnit.Framework;

namespace Flare.Tcp.Test {
    [TestFixture]
    public static class ConcurrentFlareTcpServerTests {
        [Test]
        public static void ConnectedEventRaised() {
            using var clientConnectedEvent = new ManualResetEventSlim();

            using var server = new ConcurrentFlareTcpServer();
            server.ClientConnected += _ => {
                clientConnectedEvent.Set();
            };
            var listenTask = Task.Run(() => server.ListenAsync(8888));

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            Assert.IsTrue(clientConnectedEvent.Wait(TimeSpan.FromSeconds(5)));

            client.Disconnect();
            server.Stop();
            Assert.IsTrue(listenTask.Wait(TimeSpan.FromSeconds(5)));
        }

        [Test]
        public static void DisconnectedEventRaised() {
            using var clientDisconnectedEvent = new ManualResetEventSlim();

            using var server = new ConcurrentFlareTcpServer();
            server.ClientDisconnected += _ => {
                clientDisconnectedEvent.Set();
            };
            var listenTask = Task.Run(() => server.ListenAsync(8888));

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            Assert.IsFalse(clientDisconnectedEvent.IsSet);

            client.Disconnect();
            Assert.IsTrue(clientDisconnectedEvent.Wait(TimeSpan.FromSeconds(5)));
            server.Stop();
            Assert.IsTrue(listenTask.Wait(TimeSpan.FromSeconds(5)));
        }

        [Test]
        public static void CanReceiveMessage() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");
            using var messageReceivedEvent = new ManualResetEventSlim();

            using var server = new ConcurrentFlareTcpServer();
            server.MessageReceived += (_, message) => {
                Assert.AreEqual(message.Span.ToArray(), testMessage);
                messageReceivedEvent.Set();
            };
            var listenTask = Task.Run(() => server.ListenAsync(8888));

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            Assert.IsFalse(messageReceivedEvent.IsSet);
            client.WriteMessage(testMessage);
            client.Disconnect();
            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            server.Stop();
            Assert.IsTrue(listenTask.Wait(TimeSpan.FromSeconds(5)));
        }

        [Test]
        public static void CanSendMessage() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new ConcurrentFlareTcpServer();
            server.ClientConnected += clientId => {
                server.EnqueueMessage(clientId, testMessage);
            };
            var listenTask = Task.Run(() => server.ListenAsync(8888));

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            using var message = client.ReadNextMessage();
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            client.Disconnect();
            server.Stop();
            Assert.IsTrue(listenTask.Wait(TimeSpan.FromSeconds(5)));
        }

        [Test]
        public static async Task CanSendMessageAsync() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");
            ValueTask messageWriteTask = default;

            using var server = new ConcurrentFlareTcpServer();
            server.ClientConnected += clientId => {
                messageWriteTask = server.EnqueueMessageAsync(clientId, testMessage);
            };
            var listenTask = Task.Run(() => server.ListenAsync(8888));

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            await Task.WhenAny(messageWriteTask.AsTask(), Task.Delay(TimeSpan.FromSeconds(5)));
            using var message = client.ReadNextMessage();
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            client.Disconnect();
            server.Stop();
            await listenTask.ConfigureAwait(false);
        }

        [Test]
        public static async Task CanSendMessageAndWait() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new ConcurrentFlareTcpServer();
            server.ClientConnected += clientId => {
                server.EnqueueMessageAndWait(clientId, testMessage);
            };
            var listenTask = Task.Run(() => server.ListenAsync(8888));

            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            using var message = client.ReadNextMessage();
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            client.Disconnect();
            server.Stop();
            await listenTask.ConfigureAwait(false);
        }

        [Test]
        public static async Task CanSendMessageAndWaitAsync() {
            using var messageReceivedEvent = new ManualResetEventSlim();
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");
            Task messageWriteTask = null;

            using var server = new ConcurrentFlareTcpServer();
            server.ClientConnected += clientId => {
                messageWriteTask = server.EnqueueMessageAndWaitAsync(clientId, testMessage);
                messageReceivedEvent.Set();
            };
            var listenTask = Task.Run(() => server.ListenAsync(8888));
            using var client = new FlareTcpClient();
            client.Connect(IPAddress.Loopback, 8888);
            Assert.IsTrue(messageReceivedEvent.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsNotNull(messageWriteTask);
            await Task.WhenAny(messageWriteTask, Task.Delay(TimeSpan.FromSeconds(5)));
            using var message = client.ReadNextMessage();
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            client.Disconnect();
            server.Stop();
            await listenTask.ConfigureAwait(false);
        }
    }
}
