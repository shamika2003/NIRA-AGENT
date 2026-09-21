/*
 * filename: NIRACharacterPersistenceService.cs
 */

using System.Diagnostics;

using Microsoft.Extensions.Hosting;

namespace NIRAAgent.Character.State;

public sealed class NIRACharacterPersistenceService
    : IHostedService,
      IDisposable
{
    private static readonly TimeSpan
        SaveDebounce =
            TimeSpan.FromSeconds(
                2);


    private readonly NIRACharacterStateService
        _state;


    private readonly NIRACharacterStateStore
        _store;


    private readonly Timer _saveTimer;


    private bool _started;

    private bool _disposed;


    public NIRACharacterPersistenceService(
        NIRACharacterStateService state,
        NIRACharacterStateStore store)
    {
        _state =
            state
            ?? throw new ArgumentNullException(
                nameof(state));


        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));


        _saveTimer =
            new Timer(
                SaveTimer_Callback,
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
    }


    public Task StartAsync(
        CancellationToken cancellationToken)
    {
        if (_started)
        {
            return Task.CompletedTask;
        }


        _started =
            true;


        _state.StateChanged +=
            State_StateChanged;


        return Task.CompletedTask;
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return Task.CompletedTask;
        }


        _started =
            false;


        _state.StateChanged -=
            State_StateChanged;


        _saveTimer.Change(
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);


        SaveNow();


        return Task.CompletedTask;
    }


    private void State_StateChanged(
        NIRACharacterSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }


        _saveTimer.Change(
            SaveDebounce,
            Timeout.InfiniteTimeSpan);
    }


    private void SaveTimer_Callback(
        object? state)
    {
        SaveNow();
    }


    private void SaveNow()
    {
        try
        {
            _store.Save(
                _state.Current);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[CharacterPersistence] SAVE ERROR: {ex}");
        }
    }


    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }


        _disposed =
            true;


        _state.StateChanged -=
            State_StateChanged;


        _saveTimer.Dispose();
    }
}
