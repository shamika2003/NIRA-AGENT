/*
 * filename: NIRACharacterSnapshot.cs
 */

namespace NIRAAgent.Character.State;

public readonly record struct NIRACharacterSnapshot(
    NIRARelationshipState Relationship,
    NIRAMoodState Mood,
    NIRASituationState Situation,
    long Version,
    DateTimeOffset UpdatedAt)
{
    public static NIRACharacterSnapshot Initial =>
        new(
            NIRARelationshipState.Default,
            NIRAMoodState.Default,
            NIRASituationState.Default,
            Version: 1,
            UpdatedAt:
                DateTimeOffset.UtcNow);


    public NIRACharacterSnapshot Normalize()
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
