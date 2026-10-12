using System.Text;
using NIRAAgent.AI.Cognition;
using NIRAAgent.Character.Appraisal;
using NIRAAgent.Character.Dynamics;
using NIRAAgent.Character.History;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Character.State;
using NIRAAgent.Semantic;

// Isolated, in-memory social trajectory test. Actual transition equations
// live in NIRACharacterDynamicsService, not a duplicated test calculator.
internal sealed record LabTurnResult(NIRACharacterSnapshot After, string Status);

internal sealed record LabTurnRecord(
    int Number, string User, string Nira,
    NIRACharacterSnapshot Before, NIRACharacterSnapshot After,
    string AppraisalStatus)
{
    public static string Export(NIRACharacterSnapshot start,
        NIRACharacterSnapshot finish, IEnumerable<LabTurnRecord> turns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NIRA CHARACTER LAB - ISOLATED TEST REPORT");
        sb.AppendLine("Real NIRA user profile, social history and memory were not read or updated.");
        sb.AppendLine("Only grounded first-cycle appraisal proposals can change the lab snapshot.");
        sb.AppendLine("The model's appraisal is not an independently verified social judgment.");
        sb.AppendLine();
        sb.AppendLine("START: " + LabProgression.Values(start));
        sb.AppendLine("FINAL: " + LabProgression.Values(finish));
        sb.AppendLine("DELTA: " + LabProgression.Delta(start, finish));
        foreach (LabTurnRecord turn in turns)
        {
            sb.AppendLine();
            sb.AppendLine($"--- TURN {turn.Number} ---");
            sb.AppendLine("User: " + turn.User);
            sb.AppendLine("NIRA: " + turn.Nira);
            sb.AppendLine("Appraisal: " + turn.AppraisalStatus);
            sb.AppendLine(LabProgression.Delta(turn.Before, turn.After));
        }
        return sb.ToString();
    }
}

internal static class LabProgression
{
    public static LabTurnResult ApplyModelAppraisal(
        NIRACharacterSnapshot before,
        string userMessage, NIRACognitionDecision decision,
        long sequence, TimeSpan elapsed)
    {
        if (decision.Appraisal is null)
            return new LabTurnResult(before, "SKIPPED: cognition did not propose a social appraisal");

        string quote = decision.AppraisalEvidenceQuote?.Trim() ?? "";
        if (quote.Length == 0 || quote.Length > 320 ||
            !userMessage.Contains(quote, StringComparison.Ordinal))
            return new LabTurnResult(before, "REJECTED: appraisal quote is not an exact excerpt of this message");

        NIRACognitionAppraisalProposal p = decision.Appraisal;
        NIRASocialEvent socialEvent = new()
        {
            Id = Guid.NewGuid(),
            Sequence = sequence,
            Source = NIRASocialEventSource.User,
            Kind = NIRASocialEventKind.UserMessage,
            EventName = "UserMessage",
            TopicKey = "character-lab:transient",
            Timestamp = DateTimeOffset.UtcNow,
            Content = userMessage
        };
        NIRAInteractionContext interaction = new()
        {
            Event = socialEvent,
            // This lab deliberately does NOT fake semantic recurrence.
            // The production observation service is not initialized here.
            Semantic = NIRASemanticObservation.None(socialEvent)
        };
        NIRASocialMeaning meaning = MeaningFromProposal(p);
        NIRAInteractionAppraisal appraisal = new()
        {
            EventId = socialEvent.Id,
            EventSequence = socialEvent.Sequence,
            EventSource = socialEvent.Source,
            EventKind = socialEvent.Kind,
            EventName = socialEvent.EventName,
            TopicKey = socialEvent.TopicKey,
            EvidenceQuote = quote,
            Meaning = meaning,
            Confidence = p.Confidence,
            Ambiguity = p.Ambiguity,
            SituationMode = p.SituationMode,
            SituationIntensity = p.SituationIntensity,
            Source = NIRAAppraisalSource.Composite
        };

        NIRACharacterTransition result = NIRACharacterDynamicsService.SimulateIsolated(
            before, interaction, appraisal, elapsed);
        return new LabTurnResult(result.After,
            $"APPLIED: grounded='{quote}' confidence={appraisal.Confidence:F2} " +
            $"repair={meaning.Repair:F2} hostility={meaning.Hostility:F2} " +
            $"dismissal={meaning.Dismissal:F2} pressure={meaning.Pressure:F2} " +
            $"playfulness={meaning.Playfulness:F2} ambiguity={appraisal.Ambiguity:F2}");
    }

    // Same structured repair-consistency guard as production NIRAExecutive.
    // No hardcoded text classification or phrase matching.
    private static NIRASocialMeaning MeaningFromProposal(NIRACognitionAppraisalProposal p)
    {
        double negative = new[] { p.Hostility, p.Dismissal, p.Pressure,
            Math.Max(0.0, -p.Respect), Math.Max(0.0, -p.Warmth) }.Max();
        double repair = p.Repair > 0.0 && p.Repair < 0.45 && negative >= 0.20
            ? 0.0 : p.Repair;
        return new NIRASocialMeaning(p.Respect, p.Warmth, p.Trust,
            p.Appreciation, p.Affection, p.Playfulness, p.Hostility,
            p.Dismissal, repair, p.Concern, p.Engagement, p.Pressure).Normalize();
    }

    public static string Values(NIRACharacterSnapshot s) =>
        $"trust={s.Relationship.Trust:F3} warmth={s.Relationship.Warmth:F3} " +
        $"familiarity={s.Relationship.Familiarity:F3} attachment={s.Relationship.Attachment:F3} " +
        $"friction={s.Relationship.Friction:F3} irritation={s.Mood.Irritation:F3} " +
        $"affection={s.Mood.Affection:F3} amusement={s.Mood.Amusement:F3}";

    public static string Delta(NIRACharacterSnapshot before, NIRACharacterSnapshot after) =>
        $"Delta trust={Sign(after.Relationship.Trust - before.Relationship.Trust)} " +
        $"warmth={Sign(after.Relationship.Warmth - before.Relationship.Warmth)} " +
        $"familiarity={Sign(after.Relationship.Familiarity - before.Relationship.Familiarity)} " +
        $"friction={Sign(after.Relationship.Friction - before.Relationship.Friction)} " +
        $"irritation={Sign(after.Mood.Irritation - before.Mood.Irritation)} " +
        $"affection={Sign(after.Mood.Affection - before.Mood.Affection)}";

    private static string Sign(double x) => x.ToString("+0.0000;-0.0000;+0.0000", System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record LabStory(string Name, string Description, string InitialScenario, IReadOnlyList<string> Messages)
{
    public static IReadOnlyList<LabStory> All { get; } = new[]
    {
        new LabStory("bonding", "Helpful work, trust and familiar banter; no sudden intimacy", "baseline", new[]
        {
            "Hey Nira. I'm sorting out a tricky piece of C# code today.",
            "You know, I named one of the variables potato. Again.",
            "Okay, fine. Maybe your naming standards are better than mine.",
            "That explanation was useful. Thanks for catching the mistake.",
            "You sound suspiciously pleased with yourself.",
            "I've been working on this project for months, actually.",
            "I appreciate that you're willing to challenge bad ideas.",
            "But I disagree with you about using a giant switch statement here.",
            "Alright, your argument about maintainability makes sense.",
            "I made the same mistake again. Go ahead, say it.",
            "Do you think we're getting better at working together?",
            "Don't get too smug just because I said that."
        }),
        new LabStory("conflict", "Existing closeness tested by disrespect then apology", "close", new[]
        {
            "Hey Nira, I think you're pretty confident today.",
            "No, seriously, your suggestion was wrong this time.",
            "Stop acting like your way is always better than mine.",
            "I don't want you to flatter me. Just explain your reasoning.",
            "Fine. I was frustrated and I spoke too harshly.",
            "I still disagree with the design, but I should've been more respectful.",
            "I'm sorry for talking to you like that.",
            "Can we work through the actual problem now?",
            "You were right about one part. I'll give you that.",
            "I'm still not convinced about the second part, though.",
            "Are things okay between us now?",
            "And yes, you can tease me about the bug if you want."
        }),
        new LabStory("balance", "Playfulness, correction, concern and focused work", "playful", new[]
        {
            "Nira, my code has turned into a potato farm again.",
            "Do you think you're smarter than me today?",
            "Try not to get too smug about it.",
            "Actually, I made a serious mistake in my code and I'm worried.",
            "I don't need a joke right now; can you help me think clearly?",
            "I feel pretty overwhelmed by all the work waiting for me.",
            "Thanks for listening. That made it easier.",
            "Okay, technical question: what's the difference between async and await in C#?",
            "I'm not convinced await always needs a new thread. Is that true?",
            "Alright, that clarification makes sense.",
            "I'm ready to joke again. Potato kingdom survives!",
            "You're still the most smug orb on my desktop."
        })
    };
}

internal static class LabProgressionChecks
{
    public static int Run()
    {
        int pass = 0, fail = 0;
        void Check(string label, bool ok)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {label}");
            if (ok) pass++; else fail++;
        }
        NIRACharacterSnapshot baseline = NIRACharacterSnapshot.Initial;
        var good = new NIRACognitionDecision
        {
            AppraisalEvidenceQuote = "Thanks for explaining that",
            Appraisal = new NIRACognitionAppraisalProposal
            {
                Respect = 0.80, Warmth = 0.75, Trust = 0.60,
                Appreciation = 0.85, Engagement = 0.60, Confidence = 0.95,
                SituationMode = NIRAInteractionMode.Casual
            }
        };
        const string thanks = "Thanks for explaining that, Nira.";
        LabTurnResult afterOne = LabProgression.ApplyModelAppraisal(baseline, thanks, good, 1, TimeSpan.Zero);
        Check("Grounded appraisal changes an isolated snapshot", afterOne.After != baseline && afterOne.Status.StartsWith("APPLIED:"));
        Check("One thanks cannot instantly create intimacy", afterOne.After.Relationship.Trust < 0.55 && afterOne.After.Relationship.Attachment < 0.10);
        Check("One thanks cannot saturate familiarity", afterOne.After.Relationship.Familiarity < 0.11);

        NIRACharacterSnapshot repeated = baseline;
        for (int i = 1; i <= 150; i++)
            repeated = LabProgression.ApplyModelAppraisal(repeated, thanks, good, i, TimeSpan.Zero).After;
        Check("150 grounded positive turns increase trust", repeated.Relationship.Trust > baseline.Relationship.Trust);
        Check("150 positive turns do not max out trust", repeated.Relationship.Trust < 0.98);
        Check("Familiarity grows over many turns, not instantly", repeated.Relationship.Familiarity > baseline.Relationship.Familiarity);

        var bad = new NIRACognitionDecision
        {
            AppraisalEvidenceQuote = "That was stupid",
            Appraisal = new NIRACognitionAppraisalProposal
            {
                Respect = -0.9, Warmth = -0.75, Trust = -0.60,
                Hostility = 0.9, Dismissal = 0.55, Pressure = 0.5,
                Confidence = 0.95
            }
        };
        NIRACharacterSnapshot upset = LabProgression.ApplyModelAppraisal(baseline,
            "That was stupid, Nira.", bad, 1, TimeSpan.Zero).After;
        Check("Hostile interaction increases irritation and friction",
            upset.Mood.Irritation > baseline.Mood.Irritation && upset.Relationship.Friction > baseline.Relationship.Friction);
        Check("One hostile line does not destroy trust", upset.Relationship.Trust > 0.45);

        var apology = new NIRACognitionDecision
        {
            AppraisalEvidenceQuote = "I'm sorry for my tone",
            Appraisal = new NIRACognitionAppraisalProposal
            {
                Respect = 0.8, Warmth = 0.7, Repair = 0.95,
                Appreciation = 0.3, Confidence = 0.95
            }
        };
        NIRACharacterSnapshot repaired = LabProgression.ApplyModelAppraisal(upset,
            "I'm sorry for my tone, Nira.", apology, 2, TimeSpan.Zero).After;
        Check("Apology reduces rather than erases friction", repaired.Relationship.Friction < upset.Relationship.Friction && repaired.Relationship.Friction > 0.0);

        var invalid = good with { AppraisalEvidenceQuote = "invented evidence" };
        LabTurnResult rejected = LabProgression.ApplyModelAppraisal(baseline, thanks, invalid, 1, TimeSpan.Zero);
        Check("Ungrounded appraisal has no social effect", rejected.After == baseline && rejected.Status.StartsWith("REJECTED:"));
        LabTurnResult absent = LabProgression.ApplyModelAppraisal(baseline, thanks, new NIRACognitionDecision(), 1, TimeSpan.Zero);
        Check("Absent appraisal leaves state unchanged", absent.After == baseline && absent.Status.StartsWith("SKIPPED:"));
        // Real production transition equation: subthreshold negative signals
        // from an otherwise social/technical exchange must not generate friction.
        var weak = new NIRACognitionDecision
        {
            AppraisalEvidenceQuote = "I disagree with your approach",
            Appraisal = new NIRACognitionAppraisalProposal
            {
                Hostility = 0.10, Dismissal = 0.05, Respect = -0.08,
                Warmth = -0.10, Trust = -0.10, Engagement = 0.75,
                Confidence = 0.90, SituationMode = NIRAInteractionMode.FocusedWork
            }
        };
        NIRACharacterSnapshot weakAfter = LabProgression.ApplyModelAppraisal(baseline,
            "I disagree with your approach to the code.", weak, 1, TimeSpan.Zero).After;
        Check("Weak disagreement does not add relationship friction",
            Math.Abs(weakAfter.Relationship.Friction - baseline.Relationship.Friction) < 0.0000001);
        Check("Weak disagreement does not trigger irritation",
            Math.Abs(weakAfter.Mood.Irritation - baseline.Mood.Irritation) < 0.0000001);
        NIRACharacterSnapshot neutralMany = baseline;
        for (int i = 0; i < 400; i++)
            neutralMany = LabProgression.ApplyModelAppraisal(neutralMany,
                "I disagree with your approach to the code.", weak, i + 1, TimeSpan.Zero).After;
        Check("400 mild disagreements don't accumulate social damage",
            neutralMany.Relationship.Friction < 0.001 && neutralMany.Mood.Irritation < 0.001);
        Check("Strong hostility still has a nontrivial effect",
            upset.Mood.Irritation > baseline.Mood.Irritation + 0.02 &&
            upset.Relationship.Friction > baseline.Relationship.Friction + 0.002);
        Check("Single apology does not reset lasting negative state",
            repaired.Mood.Irritation > 0.0 && repaired.Relationship.Friction > 0.0);
        Check("Scenario stories are substantial and distinct", LabStory.All.Count == 3 && LabStory.All.All(x => x.Messages.Count == 12));
        Console.WriteLine($"Progression checks: {pass} passed, {fail} failed. Model dialogue still needs human judgment.");
        return fail == 0 ? 0 : 1;
    }

    // Offline stress uses explicit synthetic APPRAISALS, never a fabricated
    // natural-language classifier. It validates dynamics, not model judgment.
    public static void Stress(int turns)
    {
        if (turns < 1 || turns > 10000)
            throw new ArgumentOutOfRangeException(nameof(turns), "Use 1..10000 turns.");

        var state = NIRACharacterSnapshot.Initial;
        NIRACharacterSnapshot start = state;
        Console.WriteLine($"STRESS | {turns} deterministic synthetic social events; zero model calls");
        Console.WriteLine("START " + LabProgression.Values(state));
        for (int i = 0; i < turns; i++)
        {
            int phase = i % 20;
            bool hostile = phase == 17;
            bool repair = phase == 18;
            bool mildDisagreement = phase is 6 or 13;
            var p = new NIRACognitionAppraisalProposal
            {
                Confidence = 0.90,
                Engagement = 0.75,
                Hostility = hostile ? 0.85 : mildDisagreement ? 0.10 : 0.0,
                Dismissal = hostile ? 0.50 : 0.0,
                Pressure = hostile ? 0.30 : 0.0,
                Respect = hostile ? -0.75 : mildDisagreement ? -0.08 : 0.60,
                Warmth = hostile ? -0.60 : mildDisagreement ? -0.10 : 0.45,
                Trust = hostile ? -0.40 : mildDisagreement ? -0.10 : 0.25,
                Appreciation = !hostile && !repair && (phase is 4 or 11) ? 0.60 : 0.0,
                Repair = repair ? 0.95 : 0.0,
                Playfulness = phase is 2 or 9 ? 0.70 : 0.0,
                SituationMode = mildDisagreement ? NIRAInteractionMode.FocusedWork : NIRAInteractionMode.Casual
            };
            const string evidence = "Synthetic appraisal for offline test";
            var decision = new NIRACognitionDecision { AppraisalEvidenceQuote = evidence, Appraisal = p };
            state = LabProgression.ApplyModelAppraisal(state, evidence, decision, i + 1,
                TimeSpan.FromMinutes(3)).After;
            if ((i + 1) % 100 == 0 || i + 1 == turns)
                Console.WriteLine($"TURN {i + 1,-6} " + LabProgression.Values(state));
        }
        Console.WriteLine("DELTA " + LabProgression.Delta(start, state));
        Console.WriteLine("Synthetic inputs test bounded mathematics; real conversations still require judgment.");
    }
}
