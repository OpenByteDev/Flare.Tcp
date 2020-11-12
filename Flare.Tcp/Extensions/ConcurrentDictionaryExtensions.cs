using System.Collections.Concurrent;

namespace Flare.Tcp.Extensions {
    internal static class ConcurrentDictionaryExtensions {

        public static bool TryRemove<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key) where TKey : notnull {
            return dictionary.TryRemove(key, out var _);
        }

    }
}
