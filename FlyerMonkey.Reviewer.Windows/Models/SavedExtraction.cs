namespace FlyerMonkey.Reviewer.Windows.Models;

public sealed class SavedExtraction : System.ComponentModel.INotifyPropertyChanged
{
    public long Id { get; set; }
    public string Retailer { get; set; } = "";
    public string FlyerFileName { get; set; } = "";
    public string PageFileName { get; set; } = "";
    public int PageNumber { get; set; }
    public int ProductCount { get; set; }
    public string SavedUtc { get; set; } = "";
    private string _status = "Saved";
    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; NotifyState(); }
    }

    private bool _isCommitActive;
    // Process-local activity only. A reopened uncertain batch never becomes active automatically.
    public bool IsCommitActive
    {
        get => _isCommitActive;
        set { if (_isCommitActive == value) return; _isCommitActive = value; NotifyState(); }
    }

    public bool RecoveryRequired => Status == "Committing" && !IsCommitActive;
    public string StatusDisplay => RecoveryRequired ? "Recovery Required" : Status;
    public string? RecoveryExplanation => RecoveryRequired
        ? "SQL outcome is uncertain. Save and Commit are blocked. Review SQL evidence before any correction; another Reviewer may still be processing this batch."
        : null;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(Status), nameof(IsCommitActive), nameof(RecoveryRequired), nameof(StatusDisplay), nameof(RecoveryExplanation) })
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(property));
    }
}
