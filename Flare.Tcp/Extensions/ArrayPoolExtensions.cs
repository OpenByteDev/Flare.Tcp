using System.Buffers;
using System.Threading;

namespace Flare.Tcp.Extensions {
    public static class ArrayPoolExtensions {

        public static void ReturnAndSetNull<T>(this ArrayPool<T> arrayPool, ref T[]? rented) {
            var toReturn = Interlocked.Exchange(ref rented, null);
            if (toReturn != null)
                arrayPool.Return(toReturn);
        }

    }
}
