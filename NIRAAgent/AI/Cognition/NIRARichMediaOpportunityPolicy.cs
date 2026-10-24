using System.Text.RegularExpressions;
using System.Text.Json;
using NIRAAgent.Capabilities;
using NIRAAgent.Presentation;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// A conservative zero-LLM fallback for a missing *optional* media intent.
/// The cognition model has already chosen an elaborate explanatory layout;
/// this recognizes a concrete, explicitly named and visually describable subject
/// from that SAME decision and the user's own message. No entity list, provider
/// shortcut, hardcoded conversation, media URLs or generated imagery.
/// The normal capability planner/authorizer still decides whether tools can run.
/// </summary>
public static class NIRARichMediaOpportunityPolicy
{
    private static readonly Regex Name = new(@"\b[\p{Lu}][\p{Ll}]{2,}(?:[ -][\p{Lu}][\p{Ll}]{2,}){1,3}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HowSubject = new(@"\bhow\s+(?:did|do|does|have|has)\s+(?<name>[\p{L}]+(?:[ -][\p{L}]+){0,3}?)\s+(?:actually\s+)?(?:evolve|develop|work|move|walk|change|survive|balance|function|operate|become)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // These are visible physical attributes/actions, never a registry of animals,
    // scientists, businesses, vehicles, topics, test prompts or model names.
    private static readonly Regex VisualContext = new(@"\b(?:body|bodies|head|eyes?|legs?|arms?|walk(?:ing)?|moving|motors?|machines?|shape|appearance|look(?:s|ing)?|colors?|wings?|physical|skin|structure|design|anatomy|photograph|visual|pictures?|photos?)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> SentenceOpeners = new(StringComparer.OrdinalIgnoreCase)
        { "How", "What", "Why", "When", "Where", "Can", "Could", "Would", "Does", "Did", "Have", "Tell", "Show", "Hey", "Hello", "Please", "Explain", "Compare" };

    public static NIRACognitionDecision Enrich(NIRACognitionDecision decision, string? userMessage)
    {
        if (decision.ResourceIntent is not null || decision.State != NIRACognitionState.Complete ||
            !decision.EmitReply || decision.ConversationOnly ||
            decision.ReplyPresentation == NIRAReplyPresentationMode.PreserveExact ||
            decision.ControlRequests.Count != 0 || decision.ContextRequests.Count != 0 ||
            decision.CapabilityRequests.Count != 0 || decision.CapabilityIds.Count != 0 ||
            decision.GoalProposals.Count != 0 || decision.BranchProposals.Count != 0 ||
            decision.BranchWorkProposals.Count != 0 || decision.DynamicToolProposals.Count != 0 ||
            decision.DynamicToolInvocations.Count != 0 || decision.ReviewCommitment ||
            string.IsNullOrWhiteSpace(userMessage) || userMessage.Length > 1400 ||
            decision.Reply.Length < 170 ||
            (decision.DisplayBlocks.Count < 2 &&
             !decision.DisplayBlocks.Any(b => b.Type == "timeline" && b.Items.Count >= 3) &&
             !decision.DisplayBlocks.Any(b => b.Type == "tabs" && b.Items.Count >= 2 &&
                 b.Panels.Count == b.Items.Count)))
            return decision;

        // Do not turn source-only links, code examples, financial charts,
        // controls or terse greetings into a speculative image operation.
        if (!decision.DisplayBlocks.Any(block => block.Type is "card" or "timeline" or "table" or "tabs"))
            return decision;

        string combined = decision.Reply + " " + string.Join(" ",
            decision.DisplayBlocks.Select(b => b.Title + " " + b.Text + " " +
                string.Join(" ", b.Items) + " " +
                string.Join(" ", b.Rows.SelectMany(row => row))));
        bool physicalContext = VisualContext.IsMatch(userMessage + " " + combined);
        string subject = "";
        foreach (Match match in Name.Matches(userMessage))
        {
            string candidate = match.Value.Trim();
            string first = candidate.Split(' ', '-')[0];
            if (SentenceOpeners.Contains(first)) continue;
            // The answer must independently discuss the same named subject.
            string last = candidate.Split(' ', '-').Last();
            if (!Regex.IsMatch(combined, @"\b" + Regex.Escape(last) + @"\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
            subject = candidate;
            break;
        }
        if (subject.Length == 0 && physicalContext)
        {
            Match grammatical = HowSubject.Match(userMessage);
            if (grammatical.Success)
            {
                string candidate = grammatical.Groups["name"].Value.Trim();
                string noun = candidate.Split(' ', '-').Last();
                if (noun.Length >= 4 && noun.Length <= 35 &&
                    Regex.IsMatch(combined, @"\b" + Regex.Escape(noun) + @"\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    subject = candidate;
            }
        }
        if (subject.Length == 0 && physicalContext)
        {
            // Model-authored *individual* card titles are useful as a search
            // phrase when it chose multiple separate illustrated examples.
            var cards = decision.DisplayBlocks.Where(b => b.Type == "card" &&
                b.Title.Length is >= 5 and <= 60 && b.Text.Length >= 60).ToArray();
            if (cards.Length >= 2)
            {
                string firstTitle = Regex.Replace(cards[0].Title.Trim(), @"^(?:The|A|An)\s+", "",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!firstTitle.Contains(':') && !firstTitle.Contains('!') &&
                    firstTitle.Length is >= 5 and <= 58 &&
                    Regex.IsMatch(combined, @"\b" + Regex.Escape(firstTitle) + @"\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    subject = firstTitle;
            }
        }
        // Phase 2: a rich explanation about several NAMED physical subjects may
        // merit separate source photos, not several near-duplicates returned by
        // one broad query. Extract the names from NIRA's own already-authored
        // cards or tabs; no entity registry, hardcoded subject or extra LLM.
        // Keep the tool/credit budget bounded and never generate image content.
        var subjects = new List<string>();
        void AddSubject(string value)
        {
            string normalized = Regex.Replace(value.Trim(), @"^(?:The|A|An)\s+", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            if (normalized.Length is < 5 or > 64 ||
                normalized.Contains(':') || normalized.Contains('!') ||
                !Regex.IsMatch(normalized, @"^[\p{L}][\p{L}\p{N} -]*$")) return;
            if (!subjects.Any(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase)))
                subjects.Add(normalized);
        }
        if (subject.Length > 0) AddSubject(subject);
        if (physicalContext)
        {
            foreach (var block in decision.DisplayBlocks)
            {
                if (block.Type == "card" && block.Text.Length >= 60)
                    AddSubject(block.Title);
                if (block.Type == "tabs" && block.Panels.Count == block.Items.Count &&
                    block.Items.Count >= 2)
                    for (int j = 0; j < block.Items.Count; j++)
                        if (block.Panels[j].Length >= 65 &&
                            VisualContext.IsMatch(block.Panels[j]))
                            AddSubject(block.Items[j]);
                // Comparative tables often identify several distinct actual
                // machines/entities in their FIRST column. No subject registry.
                // Require relevant physical context in the user's words and
                // at least two identifiable multiword row labels.
                if (block.Type == "table" && block.Rows.Count is >= 2 and <= 6 &&
                    block.Columns.Count >= 2 && VisualContext.IsMatch(userMessage))
                {
                    var namedRows = block.Rows.Select(row => row.Count > 0
                            ? row[0].Replace('(', ' ').Replace(')', ' ').Trim() : "")
                        .Where(label => Regex.IsMatch(label,
                            @"^[\p{Lu}][\p{L}\p{N} -]{4,58}$") &&
                            label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                        .ToArray();
                    if (namedRows.Length >= 2)
                        foreach (var rowSubject in namedRows) AddSubject(rowSubject);
                }
            }
        }
        if (subjects.Count == 0) return decision;
        if (subjects.Count == 1)
            return decision with {
                ResourceIntent = new NIRAResourceIntent {
                    ImageQuery = subjects[0], ImageCount = 1,
                    Purpose = "An authentic reference photograph supports the physical explanation."
                }
            };

        // One query per distinct named subject; at most three read-only
        // requests, one retrieved image each. The normal Executive checks
        // capability schemas, authority, download safety, and actual pixels.
        // We never treat candidate IDs or metadata as verified photographs.
        var queries = subjects.Take(3).Select(name => new NIRACapabilityRequest {
            CapabilityId = NIRACapabilityIds.ImageSearch,
            Arguments = JsonSerializer.SerializeToElement(new {
                query = name, limit = 8, retrieveBest = true,
                retrieveCount = 1, photographsOnly = true
            }),
            Reason = "Distinct model-authored subject benefits from an authentic reference photograph."
        }).ToArray();
        return decision with {
            State = NIRACognitionState.Continue,
            EmitReply = false, ReplyReady = false, CharacterReady = false,
            ConversationOnly = false, SelfContainedAnswer = false,
            CapabilityRequests = queries
        };
    }
}
