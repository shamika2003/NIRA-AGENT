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
    private readonly NIRACharacterStateService
        _state;


    private readonly NIRACharacterStateStore
        _store;


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


        // Write-through continuity: once the runtime is up, the currently loaded
        // character snapshot is immediately durable. Every later committed
        // character mutation is persisted synchronously as well.
        SaveSnapshot(
            _state.Current);


        return Task.CompletedTask;
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        if (!_started)
        {
            SaveNow();

            return Task.CompletedTask;
        }


        _started =
            false;


        _state.StateChanged -=
            State_StateChanged;


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


        SaveSnapshot(
            snapshot);
    }


    private void SaveNow()
    {
        if (_disposed)
        {
            return;
        }


        SaveSnapshot(
            _state.Current);
    }


    private void SaveSnapshot(
        NIRACharacterSnapshot snapshot)
    {
        try
        {
            NIRACharacterSnapshot normalized =
                snapshot.Normalize();


            _store.Save(
                normalized);


            Debug.WriteLine(
                $"[CharacterContinuity] SAVED | Version={normalized.Version} | " +
                $"Mood={normalized.Mood.Valence:F3}/{normalized.Mood.Amusement:F3}/" +
                $"{normalized.Mood.Irritation:F3}/{normalized.Mood.Concern:F3} | " +
                $"Affection={normalized.Mood.Affection:F3} | " +
                $"Relationship={normalized.Relationship.Warmth:F3}/" +
                $"{normalized.Relationship.Trust:F3}/" +
                $"{normalized.Relationship.Friction:F3}");
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


        // Flush before setting _disposed so a host disposal path that bypassed
        // StopAsync still preserves the latest committed character snapshot.
        SaveNow();


        _disposed =
            true;


        _state.StateChanged -=
            State_StateChanged;
    }
}
