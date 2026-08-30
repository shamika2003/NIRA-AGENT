/*
 * filename: SegaCharacterContextFormatter.cs
 */

using System.Text;

using SegaAgent.Character.History;
using SegaAgent.Character.Interaction;
using SegaAgent.Character.State;
using SegaAgent.Semantic;

namespace SegaAgent.Character;

public static class SegaCharacterContextFormatter
{
    private const int MaximumRecentEvents =
        10;


    private const int MaximumRelatedEvents =
        6;


    public static string Format(
        SegaCharacterSnapshot character,
        SegaAttitudeState attitude,
        SegaInteractionContext? interaction,
        IReadOnlyList<SegaSocialEvent> recentHistory)
    {
        StringBuilder builder =
            new();


        SegaRelationshipState relationship =
            character.Relationship;


        SegaMoodState mood =
            character.Mood;


        SegaSituationState situation =
            character.Situation;


        // =====================================================
        // RELATIONSHIP
        // =====================================================

        builder.AppendLine(
            "CURRENT SEGA CHARACTER STATE");

        builder.AppendLine();


        builder.AppendLine(
            "RELATIONSHIP");

        builder.AppendLine(
            $"Familiarity: {relationship.Familiarity:F2}");

        builder.AppendLine(
            $"Trust: {relationship.Trust:F2}");

        builder.AppendLine(
            $"Warmth: {relationship.Warmth:F2}");

        builder.AppendLine(
            $"Respect: {relationship.Respect:F2}");

        builder.AppendLine(
            $"Attachment: {relationship.Attachment:F2}");

        builder.AppendLine(
            $"Openness: {relationship.Openness:F2}");

        builder.AppendLine(
            $"Playfulness: {relationship.Playfulness:F2}");

        builder.AppendLine(
            $"Friction: {relationship.Friction:F2}");


        // =====================================================
        // MOOD
        // =====================================================

        builder.AppendLine();

        builder.AppendLine(
            "MOOD");

        builder.AppendLine(
            $"Valence: {mood.Valence:F2}");

        builder.AppendLine(
            $"Energy: {mood.Energy:F2}");

        builder.AppendLine(
            $"Irritation: {mood.Irritation:F2}");

        builder.AppendLine(
            $"Amusement: {mood.Amusement:F2}");

        builder.AppendLine(
            $"Curiosity: {mood.Curiosity:F2}");

        builder.AppendLine(
            $"Affection: {mood.Affection:F2}");

        builder.AppendLine(
            $"Concern: {mood.Concern:F2}");


        // =====================================================
        // SITUATION
        // =====================================================

        builder.AppendLine();

        builder.AppendLine(
            "SITUATION");

        builder.AppendLine(
            $"Mode: {situation.Mode}");

        builder.AppendLine(
            $"Intensity: {situation.Intensity:F2}");


        // =====================================================
        // CURRENT ATTITUDE
        // =====================================================

        builder.AppendLine();

        builder.AppendLine(
            "CURRENT ATTITUDE");

        builder.AppendLine(
            $"Warmth: {attitude.Warmth:F2}");

        builder.AppendLine(
            $"Patience: {attitude.Patience:F2}");

        builder.AppendLine(
            $"Playfulness: {attitude.Playfulness:F2}");

        builder.AppendLine(
            $"Engagement: {attitude.Engagement:F2}");

        builder.AppendLine(
            $"Assertiveness: {attitude.Assertiveness:F2}");

        builder.AppendLine(
            $"Emotional distance: " +
            $"{attitude.EmotionalDistance:F2}");

        builder.AppendLine(
            $"Restraint: {attitude.Restraint:F2}");

        builder.AppendLine(
            $"Interaction novelty: {attitude.Novelty:F2}");


        if (interaction !=
            null)
        {
            AppendInteraction(
                builder,
                interaction);
        }


        AppendHistory(
            builder,
            recentHistory);


        return builder.ToString();
    }


    private static void AppendInteraction(
        StringBuilder builder,
        SegaInteractionContext interaction)
    {
        builder.AppendLine();

        builder.AppendLine(
            "CURRENT INTERACTION CONTEXT");

        builder.AppendLine(
            $"Event: {interaction.Event.EventName}");

        builder.AppendLine(
            $"Topic: {interaction.Event.TopicKey}");

        builder.AppendLine(
            $"Recent topic occurrences: " +
            $"{interaction.RecentTopicOccurrences}");

        builder.AppendLine(
            $"Recent user messages: " +
            $"{interaction.RecentUserMessages}");

        builder.AppendLine(
            $"Sega already responded to this structured " +
            $"topic recently: " +
            $"{interaction.SegaAlreadyRespondedRecently}");


        if (
            interaction
                .TimeSincePreviousUserMessage
            is TimeSpan previous)
        {
            builder.AppendLine(
                $"Time since previous user message: " +
                $"{previous.TotalSeconds:F0} seconds");
        }


        if (!interaction.Semantic.Available)
        {
            return;
        }


        builder.AppendLine();

        builder.AppendLine(
            "LOCAL SEMANTIC AWARENESS");

        builder.AppendLine(
            $"Closest recent similarity: " +
            $"{interaction.Semantic.ClosestSimilarity:F3}");

        builder.AppendLine(
            $"Semantic recurrence strength: " +
            $"{interaction.SemanticRecurrence:F3}");


        SegaSemanticMatch[] related =
            interaction
                .Semantic
                .RelatedEvents
                .Take(
                    MaximumRelatedEvents)
                .ToArray();


        if (related.Length ==
            0)
        {
            return;
        }


        builder.AppendLine(
            "Related recent interactions:");


        foreach (
            SegaSemanticMatch match
            in related)
        {
            builder.AppendLine(
                $"- similarity " +
                $"{match.Similarity:F3}, " +
                $"{match.Age.TotalSeconds:F0}s ago: " +
                $"{Clean(match.Event.Content)}");
        }
    }


    private static void AppendHistory(
        StringBuilder builder,
        IReadOnlyList<SegaSocialEvent> history)
    {
        SegaSocialEvent[] recent =
            history
                .TakeLast(
                    MaximumRecentEvents)
                .ToArray();


        if (recent.Length ==
            0)
        {
            return;
        }


        builder.AppendLine();

        builder.AppendLine(
            "RECENT SOCIAL HISTORY");


        foreach (
            SegaSocialEvent socialEvent
            in recent)
        {
            builder.AppendLine(
                $"- #{socialEvent.Sequence} " +
                $"{socialEvent.Source}/" +
                $"{socialEvent.Kind} " +
                $"[{socialEvent.TopicKey}] " +
                $"{Clean(socialEvent.Content)}");
        }
    }


    private static string Clean(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "(no text)";
        }


        string clean =
            string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions
                        .RemoveEmptyEntries));


        const int maximumLength =
            220;


        if (clean.Length <=
            maximumLength)
        {
            return clean;
        }


        return clean[
            ..maximumLength] +
            "...";
    }
}