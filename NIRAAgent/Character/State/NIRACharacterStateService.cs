/*
 * filename: NIRACharacterStateService.cs
 */

namespace NIRAAgent.Character.State;

public sealed class NIRACharacterStateService
{
    private readonly object _sync =
        new();


    private NIRACharacterSnapshot
        _current;


    public event Action<
        NIRACharacterSnapshot>?
        StateChanged;


    public NIRACharacterStateService(
        NIRACharacterStateStore store)
    {
        ArgumentNullException.ThrowIfNull(
            store);


        _current =
            store
                .Load()
                .Normalize();
    }


    public NIRACharacterSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }


    public void SetRelationship(
        NIRARelationshipState relationship)
    {
        UpdateCharacter(
            current =>
                current with
                {
                    Relationship =
                        relationship
                });
    }


    public void SetMood(
        NIRAMoodState mood)
    {
        UpdateCharacter(
            current =>
                current with
                {
                    Mood =
                        mood
                });
    }


    public void SetSituation(
        NIRASituationState situation)
    {
        UpdateCharacter(
            current =>
                current with
                {
                    Situation =
                        situation
                });
    }


    public void UpdateRelationship(
        Func<
            NIRARelationshipState,
            NIRARelationshipState>
            mutation)
    {
        ArgumentNullException.ThrowIfNull(
            mutation);


        UpdateCharacter(
            current =>
                current with
                {
                    Relationship =
                        mutation(
                            current.Relationship)
                });
    }


    public void UpdateMood(
        Func<
            NIRAMoodState,
            NIRAMoodState>
            mutation)
    {
        ArgumentNullException.ThrowIfNull(
            mutation);


        UpdateCharacter(
            current =>
                current with
                {
                    Mood =
                        mutation(
                            current.Mood)
                });
    }


    public void UpdateSituation(
        Func<
            NIRASituationState,
            NIRASituationState>
            mutation)
    {
        ArgumentNullException.ThrowIfNull(
            mutation);


        UpdateCharacter(
            current =>
                current with
                {
                    Situation =
                        mutation(
                            current.Situation)
                });
    }


    public void UpdateCharacter(
        Func<
            NIRACharacterSnapshot,
            NIRACharacterSnapshot>
            mutation)
    {
        ArgumentNullException.ThrowIfNull(
            mutation);


        NIRACharacterSnapshot before;

        NIRACharacterSnapshot after;


        lock (_sync)
        {
            before =
                _current;


            NIRACharacterSnapshot changed =
                mutation(
                    before)
                .Normalize();


            if (
                before.Relationship ==
                    changed.Relationship
                &&
                before.Mood ==
                    changed.Mood
                &&
                before.Situation ==
                    changed.Situation)
            {
                return;
            }


            after =
                changed with
                {
                    Version =
                        before.Version +
                        1,

                    UpdatedAt =
                        DateTimeOffset.UtcNow
                };


            _current =
                after;
        }


        Publish(
            after);
    }


    public void ResetMood()
    {
        SetMood(
            NIRAMoodState.Default);
    }


    private void Publish(
        NIRACharacterSnapshot snapshot)
    {
        Action<NIRACharacterSnapshot>?
            handlers =
                StateChanged;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<NIRACharacterSnapshot>
                handler
            in handlers.GetInvocationList())
        {
            try
            {
                handler(
                    snapshot);
            }
            catch
            {
            }
        }
    }
}
