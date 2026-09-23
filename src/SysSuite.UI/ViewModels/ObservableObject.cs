using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 最小可观察基类。
///
/// 本仓库此前每个 ViewModel 都手写一遍 <see cref="INotifyPropertyChanged"/> 的
/// 字段 + 属性 + 事件触发，8 个空壳类里重复了 8 次；且 setter 一律无条件触发
/// 通知，绑定层会把"值没变的赋值"也当成变化重绘。这里收敛成一处。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 赋新值并在真正发生变化时触发通知。返回是否发生了变更，便于调用方联动刷新派生属性。
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
