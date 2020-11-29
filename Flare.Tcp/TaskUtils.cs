using System;
using System.Threading;
using System.Threading.Tasks;

namespace Flare.Tcp {
    internal static class TaskUtils {
        public static Task StartLongRunning(Action action, CancellationToken cancellationToken = default) =>
            StartLongRunning(action, default, cancellationToken);
        public static Task StartLongRunning(Action action, TaskCreationOptions creationOptions, CancellationToken cancellationToken = default) =>
            Task.Factory.StartNew(action, cancellationToken, creationOptions | TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

        public static Task StartLongRunning(Func<Task> function, CancellationToken cancellationToken = default) =>
            StartLongRunning(function, default, cancellationToken);
        public static Task StartLongRunning(Func<Task> function, TaskCreationOptions creationOptions = default, CancellationToken cancellationToken = default) =>
            Task.Factory.StartNew(function, cancellationToken, creationOptions | TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
    }
}
