using System;
using System.Threading;
using System.Threading.Tasks;

namespace Basic.Tcp.Extensions {
    internal static class TaskFactoryExtensions {

        public static Task StartNew(this TaskFactory factory, Action action, CancellationToken cancellationToken, TaskCreationOptions creationOptions) =>
            factory.StartNew(action, cancellationToken, creationOptions, TaskScheduler.Default);

    }
}
