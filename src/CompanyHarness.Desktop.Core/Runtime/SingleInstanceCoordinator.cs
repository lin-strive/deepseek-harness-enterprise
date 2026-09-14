namespace CompanyHarness.Desktop.Core.Runtime;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _instanceMutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _listenerCancellation = new();
    private Task? _listenerTask;
    private bool _disposed;

    public SingleInstanceCoordinator(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"{instanceName}.Activate");
        _instanceMutex = new Mutex(
            initiallyOwned: false,
            instanceName,
            out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void SignalPrimaryInstance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimaryInstance)
        {
            _activationEvent.Set();
        }
    }

    public void StartListening(Action activationRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (!IsPrimaryInstance)
        {
            throw new InvalidOperationException("Only the primary instance can listen for activation requests.");
        }

        if (_listenerTask is not null)
        {
            throw new InvalidOperationException("The activation listener has already started.");
        }

        _listenerTask = Task.Run(() => Listen(activationRequested));
    }

    private void Listen(Action activationRequested)
    {
        var handles = new WaitHandle[]
        {
            _activationEvent,
            _listenerCancellation.Token.WaitHandle,
        };

        while (!_listenerCancellation.IsCancellationRequested)
        {
            var signaledHandle = WaitHandle.WaitAny(handles);
            if (signaledHandle != 0 || _listenerCancellation.IsCancellationRequested)
            {
                return;
            }

            activationRequested();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenerCancellation.Cancel();
        _activationEvent.Set();
        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }

        _listenerCancellation.Dispose();
        _activationEvent.Dispose();
        _instanceMutex.Dispose();
    }
}
