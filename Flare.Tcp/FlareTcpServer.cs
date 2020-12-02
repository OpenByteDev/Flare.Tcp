using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Flare.Tcp {
    public class FlareTcpServer : FlareTcpServerBase {
        public void Start(int port) => StartListener(port);
        public void Start(IPAddress address, int port) => StartListener(address, port);
        public void Start(IPEndPoint endPoint) => StartListener(endPoint);

        public FlareTcpClient AcceptClient() {
            EnsureRunning();
            var client = Server.AcceptTcpClient();
            return WrapIntoClient(client);
        }
        /*public async Task<FlareTcpClient> AcceptClientAsync(CancellationToken cancellationToken = default) {
           EnsureRunning();
           var client = await Listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
           return new FlareTcpClient(client);
        }*/
        public async Task<FlareTcpClient> AcceptClientAsync() {
            EnsureRunning();
            var client = await Server.AcceptTcpClientAsync().ConfigureAwait(false);
            return WrapIntoClient(client);
        }

        private static FlareTcpClient WrapIntoClient(TcpClient socket) {
            var client = new FlareTcpClient();
            client.DirectConnect(socket);
            return client;
        }
    }
}
