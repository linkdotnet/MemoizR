using System.Diagnostics;
using Nito.Collections;

namespace MemoizR.StructuredAsyncLock;

/// <summary>
/// A collection of cancelable <see cref="TaskCompletionSource{T}"/> instances. Implementations must assume the caller is holding a lock.
/// </summary>
/// <typeparam name="T">The type of the results. If this isn't needed, use <see cref="Object"/>.</typeparam>
internal interface IAsyncWaitQueue<T>
{
    /// <summary>
    /// Gets whether the queue is empty.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Creates a new entry and queues it to this wait queue. The returned task must support both synchronous and asynchronous waits.
    /// </summary>
    /// <returns>The queued task.</returns>
    Task<T> Enqueue(int lockScope);

    /// <summary>
    /// Removes a single entry in the wait queue and completes it. This method may only be called if <see cref="IsEmpty"/> is <c>false</c>. The task continuations for the completed task must be executed asynchronously.
    /// </summary>
    /// <param name="result">The result used to complete the wait queue entry. If this isn't needed, use <c>default(T)</c>.</param>
    int Dequeue(T? result = default);

    /// <summary>
    /// Removes all entries in the wait queue and completes them. The task continuations for the completed tasks must be executed asynchronously.
    /// </summary>
    /// <param name="result">The result used to complete the wait queue entries. If this isn't needed, use <c>default(T)</c>.</param>
    void DequeueAll(T? result = default);

    /// <summary>
    /// Attempts to remove an entry from the wait queue and cancels it. The task continuations for the completed task must be executed asynchronously.
    /// </summary>
    /// <param name="task">The task to cancel.</param>
    /// <param name="cancellationToken">The cancellation token to use to cancel the task.</param>
    bool TryCancel(Task task, CancellationToken cancellationToken);

    /// <summary>
    /// Removes all entries from the wait queue and cancels them. The task continuations for the completed tasks must be executed asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to use to cancel the tasks.</param>
    void CancelAll(CancellationToken cancellationToken);
}

/// <summary>
/// Provides extension methods for wait queues.
/// </summary>
internal static class AsyncWaitQueueExtensions
{
    /// <summary>
    /// Creates a new entry and queues it to this wait queue. If the cancellation token is already canceled, this method immediately returns a canceled task without modifying the wait queue.
    /// </summary>
    /// <param name="this">The wait queue.</param>
    /// <param name="mutex">A synchronization object taken while cancelling the entry.</param>
    /// <param name="token">The token used to cancel the wait.</param>
    /// <returns>The queued task.</returns>
    public static Task<T> Enqueue<T>(this IAsyncWaitQueue<T> @this, object mutex, CancellationToken token, int lockScope)
    {
        if (token.IsCancellationRequested)
            return Task.FromCanceled<T>(token);

        var ret = @this.Enqueue(lockScope);
        if (!token.CanBeCanceled)
            return ret;

        var registration = token.Register(() =>
        {
            lock (mutex)
                @this.TryCancel(ret, token);
        }, useSynchronizationContext: false);
        ret.ContinueWith(_ => registration.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return ret;
    }
}

/// <summary>
/// The default wait queue implementation, which uses a double-ended queue.
/// </summary>
/// <typeparam name="T">The type of the results. If this isn't needed, use <see cref="Object"/>.</typeparam>
[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(DefaultAsyncWaitQueue<>.DebugView))]
internal sealed class DefaultAsyncWaitQueue<T> : IAsyncWaitQueue<T>
{
    private readonly Deque<(TaskCompletionSource<T>, int)> _queue = new Deque<(TaskCompletionSource<T>, int)>();

    private int Count
    {
        get { return _queue.Count; }
    }

    bool IAsyncWaitQueue<T>.IsEmpty
    {
        get { return Count == 0; }
    }

    Task<T> IAsyncWaitQueue<T>.Enqueue(int lockScope)
    {
        var tcs = TaskCompletionSourceExtensions.CreateAsyncTaskSource<T>();
        _queue.AddToBack((tcs, lockScope));
        return tcs.Task;
    }

    int IAsyncWaitQueue<T>.Dequeue(T? result)
    {
        var res = _queue.RemoveFromFront();
        
        res.Item1.TrySetResult(result!);
        
        return res.Item2;
    }

    void IAsyncWaitQueue<T>.DequeueAll(T? result)
    {
        foreach (var source in _queue)
            source.Item1.TrySetResult(result!);
        _queue.Clear();
    }

    bool IAsyncWaitQueue<T>.TryCancel(Task task, CancellationToken cancellationToken)
    {
        for (int i = 0; i != _queue.Count; ++i)
        {
            if (_queue[i].Item1.Task == task)
            {
                _queue[i].Item1.TrySetCanceled(cancellationToken);
                _queue.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    void IAsyncWaitQueue<T>.CancelAll(CancellationToken cancellationToken)
    {
        foreach (var source in _queue)
            source.Item1.TrySetCanceled(cancellationToken);
        _queue.Clear();
    }

    [DebuggerNonUserCode]
    internal sealed class DebugView
    {
        private readonly DefaultAsyncWaitQueue<T> _queue;

        public DebugView(DefaultAsyncWaitQueue<T> queue)
        {
            _queue = queue;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Task<T>[] Tasks
        {
            get
            {
                var result = new List<Task<T>>(_queue._queue.Count);
                foreach (var entry in _queue._queue)
                    result.Add(entry.Item1.Task);
                return result.ToArray();
            }
        }
    }
}