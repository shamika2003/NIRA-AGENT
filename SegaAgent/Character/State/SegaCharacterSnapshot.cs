/*
 * filename: SegaCharacterSnapshot.cs
 */

namespace SegaAgent.Character.State;

public readonly record struct SegaCharacterSnapshot(
    SegaRelationshipState Relationship,
    SegaMoodState Mood,
    SegaSituationState Situation,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public static SegaCharacterSnapshot Initial =>
        new(
            SegaRelationshipState.Default,
            SegaMoodState.Default,
            SegaSituationState.Default,
            Version: 1,
            UpdatedAt:
                DateTimeOffset.UtcNow);


    public SegaCharacterSnapshot Normalize()
    {
        return this with
        {
            Relationship =
                Relationship.Normalize(),

            Mood =
                Mood.Normalize(),

            Situation =
                Situation.Normalize(),

            Version =
                Math.Max(
                    1,
                    Version),

            UpdatedAt =
                UpdatedAt ==
                default
                    ? DateTimeOffset.UtcNow
                    : UpdatedAt
        };
    }
}