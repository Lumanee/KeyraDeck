using System.Collections.Concurrent;

namespace PhoneDeckPC;

/// <summary>
/// Un fil d'exécution STA dédié : les API du Shell Windows (liste des applis, icônes,
/// lancement) n'aiment pas être appelées depuis les threads du serveur web.
/// </summary>
public sealed class StaWorker
{
    private readonly BlockingCollection<Action> _queue = new();

    public StaWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable()) work();
        })
        { IsBackground = true, Name = "Shell STA" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task;
    }

    public Task Run(Action action) => Run(() => { action(); return true; });
}
