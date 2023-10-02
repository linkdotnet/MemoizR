namespace MemoizR;

public abstract class SignalHandlR : IMemoHandlR
{
    internal IMemoHandlR[] Sources { get; set; } = Array.Empty<IMemoHandlR>(); // sources in reference order, not deduplicated (up links)
    internal IMemoizR[] Observers { get; set; } = Array.Empty<IMemoizR>(); // nodes that have us as sources (down links)

    internal Context context;

    IMemoHandlR[] IMemoHandlR.Sources { get => Sources; set => Sources = value; }
    IMemoizR[] IMemoHandlR.Observers { get => Observers; set => Observers = value; }

    protected string? label;

    internal SignalHandlR(Context context)
    {
        this.context = context;
    }
}

public abstract class MemoHandlR<T> : SignalHandlR
{
    protected Func<T?, T?, bool> equals;
    protected T? value = default;

    internal MemoHandlR(Context context, Func<T?, T?, bool>? equals) : base(context)
    {
        this.equals = equals ?? ((a, b) => Object.Equals(a, b));
    }
}
