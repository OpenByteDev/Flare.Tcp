using System;

namespace Flare.Tcp {
    public delegate void MessageHandler(Span<byte> message);
}
