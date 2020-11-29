using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Flare.Tcp.Test {
    [TestFixture]
    public static class FlareTcpServerTests {
        [Test]
        public static void CanAcceptClient() {
            using var server = new FlareTcpServer();
            server.Start(8888);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, 8888);
                client.Disconnect();
            });
            var client = server.AcceptClient();
            Assert.IsNotNull(client);
            server.Stop();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static async Task CanAcceptClientAsync() {
            using var server = new FlareTcpServer();
            server.Start(8888);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, 8888);
                client.Disconnect();
            });
            var client = await server.AcceptClientAsync().ConfigureAwait(false);
            Assert.IsNotNull(client);
            server.Stop();
            await clientTask.ConfigureAwait(false);
        }

        [Test]
        public static void CanReceiveMessage() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(8888);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, 8888);
                client.WriteMessage(testMessage);
                client.Disconnect();
            });
            var client = server.AcceptClient();
            using var message = client.ReadNextMessage();
            Assert.IsNotNull(message);
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            server.Stop();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static void CanReceiveMessageIntoSpan() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(8888);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, 8888);
                client.WriteMessage(testMessage);
                client.Disconnect();
            });
            var client = server.AcceptClient();
            using var message = client.ReadNextMessageIntoSpan();
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            server.Stop();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static async Task CanReceiveMessageAsync() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(8888);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, 8888);
                client.WriteMessage(testMessage);
                client.Disconnect();
            });
            var client = await server.AcceptClientAsync().ConfigureAwait(false);
            using var message = await client.ReadNextMessageAsync().ConfigureAwait(false);
            Assert.IsNotNull(message);
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            server.Stop();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static void CanSendMessage() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(8888);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, 8888);
                using var message = client.ReadNextMessage();
                Assert.AreEqual(message.Span.ToArray(), testMessage);
                client.Disconnect();
            });
            var client = server.AcceptClient();
            client.WriteMessage(testMessage);
            server.Stop();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static async Task CanSendMessageAsync() {
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(8888);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, 8888);
                using var message = client.ReadNextMessage();
                Assert.AreEqual(message.Span.ToArray(), testMessage);
                client.Disconnect();
            });
            var client = await server.AcceptClientAsync().ConfigureAwait(false);
            await client.WriteMessageAsync(testMessage).ConfigureAwait(false);
            server.Stop();
            await clientTask.ConfigureAwait(false);
        }
    }
}
