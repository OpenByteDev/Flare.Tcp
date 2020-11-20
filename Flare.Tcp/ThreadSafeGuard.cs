﻿using System;
using System.Threading;

namespace Flare.Tcp {
    internal class ThreadSafeGuard {
        private const int Inactive = default;
        private const int Active = 1;
        private int _state;

        public ThreadSafeGuard(bool active = false) {
            _state = active ? Active : Inactive;
        }

        public bool GetAndSet() => Interlocked.Exchange(ref _state, Active) == Inactive;
        public bool Get() => _state == Active;
        public void Unset() => _state = Inactive;

        public void SetOrThrow() {
            if (!GetAndSet())
                throw new InvalidOperationException("The guard is already in use.");
        }
        public IDisposable? Use() {
            if (GetAndSet())
                return new ThreadSafeGuardToken(this);
            else
                return null;
        }
        public IDisposable UseOrThrow(Func<Exception> exceptionSupplier) {
            if (GetAndSet())
                return new ThreadSafeGuardToken(this);
            else
                throw exceptionSupplier();
        }
        public IDisposable UseOrThrow() {
            SetOrThrow();
            return new ThreadSafeGuardToken(this);
        }

        internal readonly struct ThreadSafeGuardToken : IDisposable {
            private readonly ThreadSafeGuard _guard;

            public ThreadSafeGuardToken(ThreadSafeGuard threadSafeGuard) {
                _guard = threadSafeGuard;
            }

            public void Dispose() {
                _guard.Unset();
            }
        }
    }
}
