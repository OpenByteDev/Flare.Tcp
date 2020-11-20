using System;
using System.Collections.Generic;
using System.Linq;

namespace Flare.Tcp.Extensions {
    internal static class IEnumerableExtensions {
        public static IEnumerable<(T1, T2)> Cartesian<T1, T2>(this IEnumerable<T1> first, IEnumerable<T2> second) {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            var secondMemoized = second.ToArray();
            return Core();

            IEnumerable<(T1, T2)> Core() {
                foreach (var item1 in first)
                    foreach (var item2 in secondMemoized)
                        yield return (item1, item2);
            }
        }
    }
}
