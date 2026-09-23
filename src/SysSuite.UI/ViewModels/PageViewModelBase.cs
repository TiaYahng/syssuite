using System.Windows.Input;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 页面 ViewModel 统一基类（UI-SPEC §3）。
///
/// 契约成员：IsBusy / BusyText / HasError / ErrorText / DiagnosticId /
/// RefreshCommand / CancelCommand。所有页面 ViewModel 都必须继承本类，
/// 业务逻辑不得写回 code-behind。
/// </summary>
public abstract class PageViewModelBase : ObservableObject, IDisposable
{
    private bool isBusy;
    private string busyText = "就绪";
    private string? errorText;
    private string? diagnosticId;

    /// <summary>是否有耗时操作在执行。为 true 时页面应禁用同类主命令。</summary>
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>供绑定层取反使用的便利属性，省掉 XAML 里的取反转换器。</summary>
    public bool IsIdle => !isBusy;

    /// <summary>状态栏文本。既承载进度描述，也是无错误时的结果文案。</summary>
    public string BusyText
    {
        get => busyText;
        protected set => SetProperty(ref busyText, value);
    }

    public bool HasError => !string.IsNullOrEmpty(errorText);

    /// <summary>最近一次失败的可读原因。</summary>
    public string? ErrorText
    {
        get => errorText;
        private set
        {
            if (SetProperty(ref errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>
    /// 诊断 ID。所有异步错误都可通过它关联到 %AppData%\SysSuite\logs 下的日志，
    /// 让用户能"复制诊断 ID"而不是只截一张图。
    /// </summary>
    public string? DiagnosticId
    {
        get => diagnosticId;
        private set => SetProperty(ref diagnosticId, value);
    }

    /// <summary>主刷新命令。默认按 <see cref="IsBusy"/> 门控，子类覆写 <see cref="RefreshAsync"/>。</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>取消命令。默认在空闲时不可用。</summary>
    public ICommand CancelCommand { get; }

    protected PageViewModelBase()
    {
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !IsBusy);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy);
    }

    /// <summary>
    /// 设置状态文本。刻意与错误通道分开：错误一旦写入就会一直保留，
    /// 直到下一次成功操作用 <see cref="ClearError"/> 清掉，避免"出错后提示一闪而过"。
    /// </summary>
    protected void SetStatus(string text)
    {
        BusyText = text;
        ClearError();
    }

    /// <summary>记录一次失败：写状态栏、写错误态，并生成可复制的诊断 ID。</summary>
    protected void SetError(string message, Exception? exception = null)
    {
        BusyText = string.IsNullOrWhiteSpace(message) ? "操作失败。" : message;
        ErrorText = BusyText;
        DiagnosticId = CreateDiagnosticId(exception);
    }

    protected void ClearError()
    {
        ErrorText = null;
        DiagnosticId = null;
    }

    /// <summary>
    /// 运行一段带忙碌门控的操作。异常在此统一转成错误态，不向 UI 层抛。
    /// 返回是否正常跑完（被取消也返回 false，但不会记为错误）。
    /// </summary>
    protected async Task<bool> RunBusyAsync(Func<Task> operation, string busyText, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        BusyText = busyText;
        try
        {
            await operation().ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            BusyText = "已取消。";
            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetError(exception.Message, exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>子类实现真正的刷新动作。基类只负责门控与错误收敛。</summary>
    protected virtual Task RefreshAsync() => Task.CompletedTask;

    /// <summary>子类覆写以取消进行中的操作。</summary>
    protected virtual void Cancel()
    {
    }

    /// <summary>页面卸载时调用，取消进行中的工作。</summary>
    public virtual void Dispose()
    {
        Cancel();
        GC.SuppressFinalize(this);
    }

    private static string CreateDiagnosticId(Exception? exception)
    {
        // 用异常类型 + 时间戳拼一个短 ID；日志里以同前缀记录，用户报上来即可定位。
        var stamp = DateTime.Now.ToString("yyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var kind = exception?.GetType().Name ?? "Error";
        return System.FormattableString.Invariant($"D-{stamp}-{kind}");
    }
}
