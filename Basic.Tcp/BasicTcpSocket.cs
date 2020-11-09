using System;
using System.Threading;

namespace Basic.Tcp {
    public class BasicTcpSocket : IDisposable {


        protected internal CancellationTokenSource? _cancellationTokenSource;
        protected internal CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        protected CancellationToken GetLinkedCancellationToken(CancellationToken cancellationToken) {
            if (!cancellationToken.CanBeCanceled)
                return CancellationToken;
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken).Token;
        }

        public virtual void Dispose() {
            _cancellationTokenSource?.Dispose();
        }
    }
}
