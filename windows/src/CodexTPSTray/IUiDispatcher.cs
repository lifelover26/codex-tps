using System;

namespace CodexTPSTray;

public interface IUiDispatcher
{
    void Invoke(Action action);
    void BeginInvoke(Action action);
    bool CheckAccess();
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly System.Windows.Threading.Dispatcher _dispatcher;

    public WpfUiDispatcher()
        : this(System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher)
    {
    }

    public WpfUiDispatcher(System.Windows.Threading.Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Invoke(Action action)
    {
        if (_dispatcher.HasShutdownFinished)
            return;

        try
        {
            if (_dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _dispatcher.Invoke(action);
            }
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void BeginInvoke(Action action)
    {
        if (_dispatcher.HasShutdownFinished)
            return;

        try
        {
            _dispatcher.BeginInvoke(action);
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public bool CheckAccess()
    {
        return _dispatcher.CheckAccess();
    }
}