namespace MemoizR;

public enum CacheState
{
    CacheClean = 0,
    CacheCheck = 1,
    CacheDirty = 2
}

internal static class Globals
{
    /** current capture context for identifying @reactive sources (other reactive elements) and cleanups
    * - active while evaluating a reactive function body  */
    internal static dynamic? CurrentReaction = null;  // ToDo Find a way to get rid of dynamic type
    internal static IMemoHandlR[] CurrentGets = Array.Empty<IMemoHandlR>();
    internal static int CurrentGetsIndex = 0;
}

internal interface IMemoizR : IMemoHandlR
{
    void UpdateIfNecessary();

    CacheState State { get; set; }

    void Stale(CacheState state);
}

internal interface IMemoHandlR
{
    IMemoizR[] Observers { get; set; }
}

public class MemoHandlR<T> : IMemoHandlR
{
    internal IMemoHandlR[] sources = Array.Empty<IMemoHandlR>(); // sources in reference order, not deduplicated (up links)
    internal IMemoizR[] Observers { get; set; } = Array.Empty<IMemoizR>(); // nodes that have us as sources (down links)

    IMemoizR[] IMemoHandlR.Observers { get => Observers; set => Observers = value; }

    protected Func<T?, T?, bool> equals;
    protected Func<T?> fn = () => default;
    protected T? value = default;
    protected string? label;

    internal MemoHandlR(Func<T?, T?, bool>? equals)
    {
        this.equals = equals ?? ((a, b) => Object.Equals(a, b));
    }
}

public class MemoSetR<T> : MemoHandlR<T>
{
    public MemoSetR(T value, string label = "Label", Func<T?, T?, bool>? equals = null) : base(equals)
    {
        this.value = value;
        this.label = label;
    }

    public void Set(T value)
    {
        if (!equals(this.value, value))
        {
            if (Observers.Length > 0)
            {
                for (int i = 0; i < Observers.Length; i++)
                {
                    var observer = Observers[i];
                    observer.Stale(CacheState.CacheDirty);
                }
            }
            this.value = value;
        }
    }

    public T? Get()
    {
        if (Globals.CurrentReaction != null)
        {
            if ((Globals.CurrentGets == null || !(Globals.CurrentGets.Length > 0)) &&
              (Globals.CurrentReaction.sources != null && Globals.CurrentReaction.sources.Length > 0) &&
              Globals.CurrentReaction.sources[Globals.CurrentGetsIndex].Equals(this)
            )
            {
                Globals.CurrentGetsIndex++;
            }
            else
            {
                if (!Globals.CurrentGets!.Any()) Globals.CurrentGets = new[] { this };
                else Globals.CurrentGets = Globals.CurrentGets!.Union(new[] { this }).ToArray();
            }
        }

        return value;
    }
}


public class MemoizR<T> : MemoHandlR<T>, IMemoizR
{
    private CacheState State { get; set; } = CacheState.CacheClean;
    
    CacheState IMemoizR.State { get => State; set => State = value; }

    public MemoizR(Func<T> fn, string label = "Label", Func<T?, T?, bool>? equals = null) : base(equals)
    {
        this.fn = fn;
        this.State = CacheState.CacheDirty;
        this.label = label;
    }

    public T? Get()
    {
        if (Globals.CurrentReaction != null)
        {
            if ((Globals.CurrentGets == null || !(Globals.CurrentGets.Length > 0)) &&
              (Globals.CurrentReaction.sources != null && Globals.CurrentReaction.sources.Length > 0) &&
              Globals.CurrentReaction.sources[Globals.CurrentGetsIndex].Equals(this)
            )
            {
                Globals.CurrentGetsIndex++;
            }
            else
            {
                if (!Globals.CurrentGets!.Any()) Globals.CurrentGets = new[] { this };
                else Globals.CurrentGets = Globals.CurrentGets!.Union(new[] { this }).ToArray();
            }
        }

        UpdateIfNecessary();
        return value;
    }

    /** update() if dirty, or a parent turns out to be dirty. */
    internal void UpdateIfNecessary()
    {
        // If we are potentially dirty, see if we have a parent who has actually changed value
        if (State == CacheState.CacheCheck)
        {
            foreach (var source in sources)
            {
                (source as IMemoizR)?.UpdateIfNecessary(); // updateIfNecessary() can change state
                if (State == CacheState.CacheDirty)
                {
                    // Stop the loop here so we won't trigger updates on other parents unnecessarily
                    // If our computation changes to no longer use some sources, we don't
                    // want to update() a source we used last time, but now don't use.
                    break;
                }
            }
        }

        // If we were already dirty or marked dirty by the step above, update.
        if (State == CacheState.CacheDirty)
        {
            Update();
        }

        // By now, we're clean
        State = CacheState.CacheClean;
    }

    /** run the computation fn, updating the cached value */
    private void Update()
    {
        var oldValue = value;

        /* Evalute the reactive function body, dynamically capturing any other reactives used */
        var prevReaction = Globals.CurrentReaction;
        var prevGets = Globals.CurrentGets;
        var prevIndex = Globals.CurrentGetsIndex;

        Globals.CurrentReaction = this;
        Globals.CurrentGets = Array.Empty<MemoHandlR<object>>();
        Globals.CurrentGetsIndex = 0;

        try
        {
            value = fn();

            // if the sources have changed, update source & observer links
            if (Globals.CurrentGets.Length > 0)
            {
                // remove all old sources' .observers links to us
                RemoveParentObservers(Globals.CurrentGetsIndex);
                // update source up links
                if (sources.Any() && Globals.CurrentGetsIndex > 0)
                {
                    sources = sources.Take(Globals.CurrentGetsIndex).Union(Globals.CurrentGets).ToArray();
                }
                else
                {
                    sources = Globals.CurrentGets;
                }

                for (var i = Globals.CurrentGetsIndex; i < sources.Length; i++)
                {
                    // Add ourselves to the end of the parent .observers array
                    var source = sources[i];
                    if (!source.Observers.Any())
                    {
                        source.Observers = (new[] { this }).ToArray();
                    }
                    else
                    {
                        source.Observers = source.Observers.Union((new[] { this })).ToArray();
                    }
                }
            }
            else if (sources.Any() && Globals.CurrentGetsIndex < sources.Length)
            {
                // remove all old sources' .observers links to us
                RemoveParentObservers(Globals.CurrentGetsIndex);
                sources = sources.Take(Globals.CurrentGetsIndex).ToArray();
            }
        }
        catch (Exception e)
        {
            var m = e.Message;
        }
        finally
        {
            Globals.CurrentGets = prevGets;
            Globals.CurrentReaction = prevReaction;
            Globals.CurrentGetsIndex = prevIndex;
        }

        // handles diamond depenendencies if we're the parent of a diamond.
        if (!equals(oldValue, value) && Observers.Length > 0)
        {
            // We've changed value, so mark our children as dirty so they'll reevaluate
            for (int i = 0; i < Observers.Length; i++)
            {
                var observer = Observers[i];
                observer.State = CacheState.CacheDirty;
            }
        }

        // We've rerun with the latest values from all of our sources.
        // This means that we no longer need to update until a signal changes
        State = CacheState.CacheClean;
    }

    private void RemoveParentObservers(int index)
    {
        if (!sources.Any()) return;
        for (var i = index; i < sources.Length; i++)
        {
            var source = sources[i]; // We don't actually delete sources here because we're replacing the entire array soon
            var swap = Array.FindIndex(source.Observers, (v) => v.Equals(this));
            source.Observers![swap] = source.Observers![source.Observers!.Length - 1];
            source.Observers = source.Observers.SkipLast(1).ToArray();
        }
    }

    void IMemoizR.UpdateIfNecessary()
    {
        UpdateIfNecessary();
    }

    internal void Stale(CacheState state)
    {
        if (State < state)
        {
            State = state;

            if (Observers.Length > 0)
            {
                for (int i = 0; i < Observers.Length; i++)
                {
                    Observers[i].Stale(CacheState.CacheCheck);
                }
            }
        }
    }

    void IMemoizR.Stale(CacheState state)
    {
        Stale(state);
    }
}