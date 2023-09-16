using MemoizR;

public class ReactiveMemoFactory : MemoFactory
{
    public ReactiveMemoFactory(string? contextKey = null) : base(contextKey) { }

    public Reaction<T> CreateReaction<T>(Func<T> fn, string label = "Label", Func<T?, T?, bool>? equals = null)
    {
        return new Reaction<T>(fn, context, label, equals);
    }

    public MemoReducR<T> CreateMemoReducR<T>(Func<T?, T> fn, string label = "Label", Func<T?, T?, bool>? equals = null)
    {
        return new MemoReducR<T>(fn, context, label, equals);
    }
}