using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FlyerMonkey.Reviewer.Windows.Services;

public sealed record RecentOperation(string Name, string State, string Summary, DateTimeOffset Completed)
{
    public override string ToString() => $"{Completed.ToLocalTime():HH:mm:ss} {State}: {Name} - {Summary}";
}
public sealed class OperationProgress(Func<DateTimeOffset>? clock = null) : INotifyPropertyChanged
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private long version;
    private DateTimeOffset started, completed;
    public string Name { get; private set; } = "Ready";
    public string State { get; private set; } = "Ready";
    public string Summary { get; private set; } = "";
    public bool IsRunning => State == "Running";
    public string Timing => IsRunning ? $"{(now() - started).Duration():mm\\:ss}" : completed == default ? "" : $"{completed.ToLocalTime():HH:mm:ss} ({(completed-started).Duration():mm\\:ss})";
    public ObservableCollection<RecentOperation> Recent { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() { foreach(var p in new[]{nameof(Name),nameof(State),nameof(Summary),nameof(IsRunning),nameof(Timing)}) PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(p)); }
    public void RefreshElapsed() { if(IsRunning) PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Timing))); }
    public Ticket Start(string name, string summary = "Working...")
    {
        version++; Name=name; Summary=summary; State="Running"; started=now(); completed=default; Changed(); return new Ticket(this,version);
    }
    private void Report(long id,string text) { if(id==version && IsRunning) { Summary=text; Changed(); } }
    private void Finish(long id,string state,string summary)
    {
        if(id!=version || !IsRunning) return;
        completed=now(); State=state;Summary=summary;Recent.Insert(0,new(Name,state,summary,completed));while(Recent.Count>8)Recent.RemoveAt(8);Changed();
    }
    public sealed class Ticket(OperationProgress owner,long id) : IDisposable
    {
        public void Report(string text)=>owner.Report(id,text);
        public void Success(string text)=>owner.Finish(id,"Success",text);
        public void Fail(string text)=>owner.Finish(id,"Failure",text);
        public void Cancel(string text)=>owner.Finish(id,"Cancelled",text);
        public void Dispose()=>owner.Finish(id,"Cancelled","Not started / no further changes.");
    }
}

public sealed class OperationSession(OperationProgress.Ticket progress, Action release) : IDisposable
{
    private Action? _release = release;
    public void Report(string text) => progress.Report(text);
    public void Success(string text) => progress.Success(text);
    public void Fail(string text) => progress.Fail(text);
    public void Cancel(string text) => progress.Cancel(text);
    public void Dispose() { Interlocked.Exchange(ref _release, null)?.Invoke(); progress.Dispose(); }
}
