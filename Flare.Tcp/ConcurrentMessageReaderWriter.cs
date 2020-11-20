using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Flare.Tcp {
    internal class ConcurrentMessageReaderWriter {

        private Socket _socket;
        private NetworkStream _networkStream;
        private Channel<PendingMessage> _pendingMessages;

        public ConcurrentMessageReaderWriter(TcpClient client) : this(client.Client, client.GetStream()) { }
        public ConcurrentMessageReaderWriter(Socket socket, NetworkStream stream) {
            _socket = socket;
            _networkStream = stream;
            _pendingMessages = Channel.CreateUnbounded<PendingMessage>(new UnboundedChannelOptions() { SingleReader = true });
        }

        public Task ReadLoopTask(SpanAction<byte> messageHandler, CancellationToken cancellationToken = default) {
            return TaskUtils.StartLongRunning(async () => {
                using var reader = new MessageStreamReader(_networkStream);
                while (_socket.Connected && !cancellationToken.IsCancellationRequested)
                    await reader.ReadMessageAsync(messageHandler, cancellationToken);
            }, cancellationToken);
        }

        public Task WriteLoopTask(CancellationToken cancellationToken = default) {
            return TaskUtils.StartLongRunning(async () => {
                var writer = new MessageStreamWriter(_networkStream);
                await foreach (var message in _pendingMessages.Reader.ReadAllAsync(cancellationToken))
                    await writer.WriteMessageAsync(message.MessageContent, cancellationToken);
            }, cancellationToken);
        }

    }
}
