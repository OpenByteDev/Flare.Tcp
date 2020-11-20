using System.Threading.Tasks;

namespace Flare.Tcp.Extensions {
    internal static class TaskExtensions {
        public static void WaitAndUnwrap(this Task task) {
            task.GetAwaiter().GetResult();
        }
        public static TResult WaitAndUnwrap<TResult>(this Task<TResult> task) {
            return task.GetAwaiter().GetResult();
        }
        public static void WaitAndUnwrap(this Task task, bool continueOnCapturedContext = true) {
            task.ConfigureAwait(continueOnCapturedContext).GetAwaiter().GetResult();
        }
        public static TResult WaitAndUnwrap<TResult>(this Task<TResult> task, bool continueOnCapturedContext = true) {
            return task.ConfigureAwait(continueOnCapturedContext).GetAwaiter().GetResult();
        }
    }
}
