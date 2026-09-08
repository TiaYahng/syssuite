using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace SysSuite.UI;

public sealed record BackgroundTask(string Id, string Title, double Percent, string State, ICommand CancelCommand);

public sealed class BackgroundTaskManager : INotifyPropertyChanged
{
    public static BackgroundTaskManager Current { get; } = new();

    public ObservableCollection<BackgroundTask> Tasks { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProgressText { get; private set; } = "??";

    public double ProgressPercent { get; private set; }

    public ICommand? CancelCommand { get; private set; }

    public void Start(string id, string title, ICommand cancelCommand)
    {
        Tasks.Add(new BackgroundTask(id, title, 0, "???", cancelCommand));
        ProgressText = title;
        ProgressPercent = 0;
        CancelCommand = cancelCommand;
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(CancelCommand));
    }

    public void Complete(string id)
    {
        var task = Tasks.FirstOrDefault(item => item.Id == id);
        if (task is not null)
        {
            Tasks.Remove(task);
        }

        ProgressText = Tasks.Count == 0 ? "??" : Tasks[^1].Title;
        ProgressPercent = Tasks.Count == 0 ? 100 : Tasks[^1].Percent;
        CancelCommand = Tasks.Count == 0 ? null : Tasks[^1].CancelCommand;
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(CancelCommand));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
