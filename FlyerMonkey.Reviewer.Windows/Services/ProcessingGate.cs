namespace FlyerMonkey.Reviewer.Windows.Services;

/// <summary>One operation at a time, including programmatic/reentrant invocations.</summary>
public sealed class ProcessingGate
{
    private int _busy;
    public IDisposable? TryEnter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return null;
        return new Lease(() => Interlocked.Exchange(ref _busy, 0));
    }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
    public static async Task<T> CompleteAsync<T>(Task<T> operation, CancellationToken cancellationToken)
    {
        var result = await operation;
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
