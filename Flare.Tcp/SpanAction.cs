using System;

namespace Flare.Tcp {
    public delegate void SpanAction<T>(Span<T> span);
}
