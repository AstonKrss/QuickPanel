using System.Threading;

namespace QuickPanel.Services;

/// <summary>单实例（PRD F33：Global Mutex + EventWaitHandle 双保险）。</summary>
public class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Global\\QuickPanel_SingleInstance_Mutex";
    private const string EventName = "Global\\QuickPanel_Activate_Event";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _cts;

    public bool IsFirstInstance { get; private set; }

    public void Acquire()
    {
        // 先创建事件，保证第二实例总能通知到既有实例
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>第二实例通知既有实例唤出面板。</summary>
    public static void SignalExisting()
    {
        try
        {
            using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            ev.Set();
        }
        catch
        {
            // 通知失败即退出，不阻塞第二实例
        }
    }

    /// <summary>监听唤出事件（后台线程；回调由调用方封送到 UI 线程）。</summary>
    public void StartActivateListener(Action onActivate)
    {
        if (_activateEvent is null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var thread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _activateEvent, token.WaitHandle };
            try
            {
                while (WaitHandle.WaitAny(handles) == 0)
                    onActivate();
            }
            catch (OperationCanceledException)
            {
                // 退出监听
            }
        })
        {
            IsBackground = true,
            Name = "QuickPanel_ActivateListener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _mutex?.ReleaseMutex(); }
        catch { /* 非属主线程释放会抛出，忽略 */ }
        _mutex?.Dispose();
        _activateEvent?.Dispose();
    }
}
