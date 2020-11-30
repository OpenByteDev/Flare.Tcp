using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Flare.Tcp {
    internal class ThreadSafeGuard {
        private const int Inactive = default;
        private const int Active = 1;
        private int _state;

        public ThreadSafeGuard(bool active = false) {
            _state = active ? Active : Inactive;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetAndSet() => Interlocked.Exchange(ref _state, Active) == Inactive;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Get() => _state == Active;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unset() => _state = Inactive;

        public void SetOrThrow() {
            if (!GetAndSet())
                throw new InvalidOperationException("The guard is already in use.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ThreadSafeGuardToken? Use() {
            if (GetAndSet())
                return new ThreadSafeGuardToken(this);
            else
                return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ThreadSafeGuardToken UseOrThrow() {
            SetOrThrow();
            return new ThreadSafeGuardToken(this);
        }
    }

    internal readonly struct ThreadSafeGuardToken : IDisposable {
        private readonly ThreadSafeGuard _guard;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ThreadSafeGuardToken(ThreadSafeGuard threadSafeGuard) {
            _guard = threadSafeGuard;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() {
            _guard.Unset();
        }
    }
}
