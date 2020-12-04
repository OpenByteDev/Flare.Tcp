using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Flare.Tcp.Test {
    [TestFixture]
    public static class FlareTcpServerTests {
        [Test]
        public static void FreesSocket() {
            var port = Utils.GetRandomClientPort();
            using var server = new FlareTcpServer();
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.Disconnect();
            });
            server.Start(port);
            using var serverClient = server.AcceptClient();
            serverClient.Disconnect();
            server.Shutdown();
            Assert.IsTrue(clientTask.Wait(TimeSpan.FromSeconds(5)), "Client Task did not complete successfully.");
            Assert.IsFalse(Utils.IsPortInUse(port), "Port is still in use after server shutdown.");
        }

        [Test]
        public static void CanAcceptClient() {
            var port = Utils.GetRandomClientPort();
            using var server = new FlareTcpServer();
            server.Start(port);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.Disconnect();
            });
            using var client = server.AcceptClient();
            client.Disconnect();
            Assert.IsNotNull(client);
            server.Shutdown();
            Assert.IsTrue(clientTask.Wait(TimeSpan.FromSeconds(5)), "Client Task did not complete successfully.");
        }

        [Test]
        public static async Task CanAcceptClientAsync() {
            var port = Utils.GetRandomClientPort();
            using var server = new FlareTcpServer();
            server.Start(port);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.Disconnect();
            });
            using var client = await server.AcceptClientAsync().ConfigureAwait(false);
            Assert.IsNotNull(client);
            server.Shutdown();
            await clientTask.ConfigureAwait(false);
        }

        [Test]
        public static void CanReceiveMessage() {
            var port = Utils.GetRandomClientPort();
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(port);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.WriteMessage(testMessage);
                client.Disconnect();
            });
            using var client = server.AcceptClient();
            using var message = client.ReadNextMessage();
            Assert.IsNotNull(message);
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            server.Shutdown();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static void CanReceiveMessageSpanOwner() {
            var port = Utils.GetRandomClientPort();
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(port);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.WriteMessage(testMessage);
                client.Disconnect();
            });
            using var client = server.AcceptClient();
            using var message = client.ReadNextMessageSpanOwner();
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            server.Shutdown();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static async Task CanReceiveMessageAsync() {
            var port = Utils.GetRandomClientPort();
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(port);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.WriteMessage(testMessage);
                client.Disconnect();
            });
            using var client = await server.AcceptClientAsync().ConfigureAwait(false);
            using var message = await client.ReadNextMessageAsync().ConfigureAwait(false);
            Assert.IsNotNull(message);
            Assert.AreEqual(message.Span.ToArray(), testMessage);
            server.Shutdown();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static void CanSendMessage() {
            var port = Utils.GetRandomClientPort();
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(port);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, port);
                using var message = client.ReadNextMessage();
                Assert.AreEqual(message.Span.ToArray(), testMessage);
                client.Disconnect();
            });
            using var client = server.AcceptClient();
            client.WriteMessage(testMessage);
            server.Shutdown();
            clientTask.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public static async Task CanSendMessageAsync() {
            var port = Utils.GetRandomClientPort();
            byte[] testMessage = Encoding.UTF8.GetBytes("Test");

            using var server = new FlareTcpServer();
            server.Start(port);
            var clientTask = Task.Run(() => {
                using var client = new FlareTcpClient();
                client.Connect(IPAddress.Loopback, port);
                using var message = client.ReadNextMessage();
                Assert.AreEqual(message.Span.ToArray(), testMessage);
                client.Disconnect();
            });
            using var client = await server.AcceptClientAsync().ConfigureAwait(false);
            await client.WriteMessageAsync(testMessage).ConfigureAwait(false);
            server.Shutdown();
            await clientTask.ConfigureAwait(false);
        }
    }
}
