using System.ComponentModel;
using System.Windows.Input;

namespace SysSuite.UI.ViewModels;

public sealed class SoftwareHubViewModel : INotifyPropertyChanged
{
    private bool isBusy;
    private string status = "就绪";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy { get => isBusy; private set { isBusy = value; PropertyChanged?.Invoke(this, new(nameof(IsBusy))); } }

    public string Status { get => status; private set { status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }

    public ICommand RefreshCommand { get; } = new RelayCommand(_ => { }, _ => true);

    public ICommand CancelCommand { get; } = new RelayCommand(_ => { }, _ => false);
}
