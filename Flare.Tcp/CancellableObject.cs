using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Flare.Tcp {
    public class CancellableObject : IDisposable {

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        protected internal CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        protected CancellationToken GetLinkedCancellationToken(CancellationToken cancellationToken) {
            if (!cancellationToken.CanBeCanceled)
                return CancellationToken;
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken).Token;
        }

        [MemberNotNull(nameof(_cancellationTokenSource))]
        protected void ResetCancellationToken() {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public virtual void Dispose() {
            _cancellationTokenSource?.Dispose();
        }
    }
}
