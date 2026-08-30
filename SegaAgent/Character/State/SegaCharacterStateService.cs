/*
 * filename: SegaCharacterStateService.cs
 */

namespace SegaAgent.Character.State;

public sealed class SegaCharacterStateService
{
    private readonly object _sync =
        new();


    private SegaCharacterSnapshot
        _current;


    public event Action<
        SegaCharacterSnapshot>?
        StateChanged;


    public SegaCharacterStateService(
        SegaCharacterStateStore store)
    {
        ArgumentNullException.ThrowIfNull(
            store);


        _current =
            store
                .Load()
                .Normalize();
    }


    public SegaCharacterSnapshot Current
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
        SegaRelationshipState relationship)
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
        SegaMoodState mood)
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
        SegaSituationState situation)
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
            SegaRelationshipState,
            SegaRelationshipState>
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
            SegaMoodState,
            SegaMoodState>
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
            SegaSituationState,
            SegaSituationState>
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
            SegaCharacterSnapshot,
            SegaCharacterSnapshot>
            mutation)
    {
        ArgumentNullException.ThrowIfNull(
            mutation);


        SegaCharacterSnapshot before;

        SegaCharacterSnapshot after;


        lock (_sync)
        {
            before =
                _current;


            SegaCharacterSnapshot changed =
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
            SegaMoodState.Default);
    }


    private void Publish(
        SegaCharacterSnapshot snapshot)
    {
        Action<SegaCharacterSnapshot>?
            handlers =
                StateChanged;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<SegaCharacterSnapshot>
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