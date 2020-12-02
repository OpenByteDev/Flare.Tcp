using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace Flare.Tcp.Test {
    internal static class Utils {
        [ThreadStatic]
        private static readonly Random _random = new();

        public static int GetRandomClientPort() => _random.Next(1024, 49151);
        public static bool IsPortInUse(int port) =>
            IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Any(e => e.LocalEndPoint.Port == port);

        public static async Task WithTimeout(Task task, TimeSpan timeout) {
            var first = await Task.WhenAny(task, Task.Delay(timeout));
            if (first != task)
                throw new TimeoutException("The task timed out.");
        }
        public static Task WithTimeout(ValueTask task, TimeSpan timeout) =>
            WithTimeout(task.AsTask(), timeout);
    }
}
