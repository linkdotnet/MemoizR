namespace MemoizR.StructuredConcurrency;

public sealed class ConcurrentRace<T> : SignalHandlR, IMemoizR
{
    private CacheState State { get; set; } = CacheState.CacheDirty;
    private IReadOnlyCollection<Func<CancellationTokenSource, Task<T>>> fns;
    private CancellationTokenSource? cancellationTokenSource;
    private T? value = default;

    CacheState IMemoizR.State { get => State; set => State = value; }

    internal ConcurrentRace(IReadOnlyCollection<Func<CancellationTokenSource, Task<T>>> fns, Context context, string label = "Label") : base(context)
    {
        this.fns = fns;
        this.Label = label;
    }

    public void Cancel()
    {
        cancellationTokenSource?.Cancel();
    }

    public Task<T?> Get()
    {
        return Get(new CancellationTokenSource());
    }

    public async Task<T?> Get(CancellationTokenSource cancellationTokenSource)
    {
        Cancel();
        this.cancellationTokenSource = cancellationTokenSource;
        // Only one thread should evaluate the graph at a time. otherwise the context could get messed up.
        // This should lead to perf gains because memoization can be utilized more efficiently.
        using (await Context.ContextLock.UpgradeableLockAsync())
        {
            await Update();
        }

        Thread.MemoryBarrier();
        return value;
    }

    /** run the computation fn, updating the cached value */
    private async Task Update()
    {
        if (State == CacheState.Evaluating) throw new InvalidOperationException("Cyclic behavior detected");

        /* Evaluate the reactive function body, dynamically capturing any other reactives used */
        var prevReaction = Context.CurrentReaction;
        var prevGets = Context.CurrentGets;
        var prevIndex = Context.CurrentGetsIndex;

        Context.CurrentReaction = this;
        Context.CurrentGets = [];
        Context.CurrentGetsIndex = 0;

        try
        {
            State = CacheState.Evaluating;
            value = await new StructuredRaceJob<T>(fns, cancellationTokenSource!).Run();
            State = CacheState.CacheClean;

            // if the sources have changed, update source & observer links
            if (Context.CurrentGets.Length > 0)
            {
                for (var i = Context.CurrentGetsIndex; i < Sources.Length; i++)
                {
                    // Add ourselves to the end of the parent .observers array
                    var source = Sources[i];
                    source.Observers = !source.Observers.Any()
                        ? [new WeakReference<IMemoizR>(this)]
                        : [.. source.Observers, new WeakReference<IMemoizR>(this)];
                }
            }
        }
        catch (TaskCanceledException)
        {
            State = CacheState.CacheCheck;
        }
        finally
        {
            Context.CurrentGets = prevGets;
            Context.CurrentReaction = prevReaction;
            Context.CurrentGetsIndex = prevIndex;
        }

        // handles diamond depenendencies if we're the parent of a diamond.
        if (Observers.Length > 0)
        {
            // We've changed value, so mark our children as dirty so they'll reevaluate
            foreach (var observer in Observers)
            {
                if (observer.TryGetTarget(out var o))
                {
                    o.State = CacheState.CacheDirty;
                }
            }
        }
    }

    async Task IMemoizR.UpdateIfNecessary()
    {
        using (await Context.ContextLock.UpgradeableLockAsync())
        {
            await Update();
        }
    }

    ValueTask IMemoizR.Stale(CacheState state)
    {
        return ValueTask.CompletedTask;
    }

    ~ConcurrentRace()
    {
        Cancel();
    }
}
