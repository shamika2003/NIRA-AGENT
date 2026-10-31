using System.Text;
using System.Text.RegularExpressions;

namespace NIRAAgent.AI.Cognition;

// One capability-directory adapter for both cognition stages. Input is a
// registry-generated descriptor snapshot, NOT a handwritten tool whitelist.
// Adding/removing/revising a registered tool changes the live directory only;
// neither public presentation nor internal work needs a topic-specific edit.
internal static class NIRACapabilityPromptDirectory
{

    // Classification is derived from the actual registry-produced descriptor,
    // never from words in the user request, a topic list, or invented signatures.
    // Complex *required* structured inputs must be discovered before use.
    // Registered optional arrays (e.g. a batched query) remain usable directly.
    private sealed record Tool(string Id, string Header, IReadOnlyList<string> Parameters);

    // Read-only relevance ordering for the FIRST model round. This is advisory
    // exposure of already registered contracts, not a hardcoded list of tools,
    // a routing decision, or a capability execution/permission shortcut.
    // Newly registered tool families can participate without changing this code.
    private static HashSet<string> Terms(string? value)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return terms;
        foreach (Match match in Regex.Matches(value, "[A-Za-z][A-Za-z0-9]*"))
        {
            string word = match.Value.ToLowerInvariant();
            if (word.Length < 4 || NoiseWords.Contains(word)) continue;
            // A bounded prefix groups natural inflections (calculate/calculator,
            // source/sources) without an external model or topic-specific map.
            terms.Add(word.Length > 6 ? word[..6] : word);
        }
        return terms;
    }

    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "string", "array", "object", "integer", "boolean", "required",
        "false", "true", "default", "observe", "modify", "optional",
        "registered", "capability", "request", "requests", "value",
        "values", "name", "names", "tool", "tools", "with", "that",
        "from", "this", "these", "your", "have", "need", "give",
        "tell", "please", "what", "when", "where", "there", "some"
    };

    private static int Relevance(Tool tool, HashSet<string> requestedTerms)
    {
        if (requestedTerms.Count == 0) return 0;
        int idMatches = Terms(tool.Id).Count(requestedTerms.Contains);
        int descriptionMatches = Terms(tool.Header).Count(requestedTerms.Contains);
        int parameterMatches = Terms(string.Join(" ", tool.Parameters))
            .Count(requestedTerms.Contains);
        return idMatches * 8 + descriptionMatches * 3 + parameterMatches;
    }

    internal static string Build(NIRACognitionContext c)
    {
        var tools = new List<Tool>();
        Tool? current = null;
        foreach (string originalLine in c.CapabilityContext.Split('\n'))
        {
            string line = originalLine.TrimEnd('\r', '\n');
            string clean = line.Trim();
            if (clean.StartsWith("- ", StringComparison.Ordinal) &&
                clean.Contains(" | defaultRisk=", StringComparison.Ordinal))
            {
                if (current is not null) tools.Add(current);
                int cut = clean.IndexOf(" | defaultRisk=", StringComparison.Ordinal);
                string id = clean.Substring(2, cut - 2).Trim();
                current = id.Length is > 2 and <= 120
                    ? new Tool(id, clean, new List<string>()) : null;
                continue;
            }
            if (current is not null && line.StartsWith("  - ", StringComparison.Ordinal))
                ((List<string>)current.Parameters).Add(line);
        }
        if (current is not null) tools.Add(current);

        HashSet<string> requestedTerms = Terms(c.Event?.Content);

        // Select direct contracts by their real registered schema size, not the
        // alphabetical position of their capability ID. Otherwise a directory
        // with many earlier, verbose tools can force a cheap late observation
        // into an unnecessary schema-discovery LLM round.
        //
        // Give each available capability family an opportunity to expose its
        // smallest eligible contract, then spend the remaining finite prompt
        // budget on other inexpensive contracts. This is descriptor-driven:
        // no special tool IDs, topic words, or user-message phrases are used.
        const int directBudget = 27000;
        var candidates = tools.Select(tool =>
        {
            bool requiredStructured = tool.Parameters.Any(p =>
                p.Contains(" | required=True", StringComparison.OrdinalIgnoreCase) &&
                (p.Contains(": array", StringComparison.OrdinalIgnoreCase) ||
                 p.Contains(": object", StringComparison.OrdinalIgnoreCase) ||
                 p.Contains(": json", StringComparison.OrdinalIgnoreCase)));
            string full = tool.Header + "\n" +
                string.Join("\n", tool.Parameters) + "\n";
            string family = tool.Id.Split('.')[0];
            return new
            {
                tool.Id,
                Full = full,
                Family = family,
                Eligible = !requiredStructured && tool.Parameters.Count <= 13,
                Relevance = Relevance(tool, requestedTerms)
            };
        }).ToArray();
        var directIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int directLength = 0;

        // Family seeding prevents small registered operations from becoming
        // discovery-only just because another namespace has many long schemas.
        var eligible = candidates.Where(c => c.Eligible).ToArray();
        var familySeeds = eligible.GroupBy(c => c.Family, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.Relevance)
                .ThenBy(c => c.Full.Length)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase).First())
            .OrderByDescending(c => c.Relevance)
            .ThenBy(c => c.Full.Length)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in familySeeds)
        {
            if (candidate.Full.Length > directBudget - directLength) continue;
            if (directIds.Add(candidate.Id)) directLength += candidate.Full.Length;
        }

        foreach (var candidate in eligible.OrderByDescending(c => c.Relevance)
            .ThenBy(c => c.Full.Length)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (directIds.Contains(candidate.Id) ||
                candidate.Full.Length > directBudget - directLength) continue;
            directIds.Add(candidate.Id);
            directLength += candidate.Full.Length;
        }

        StringBuilder inventory = new();
        StringBuilder direct = new();
        foreach (var candidate in candidates)
        {
            bool isDirect = directIds.Contains(candidate.Id);
            inventory.Append("- ").Append(candidate.Id).Append(" | ")
                .AppendLine(isDirect ? "DIRECT" : "DISCOVER");
        }
        // Put the full descriptor of the most relevant directly callable tool
        // FIRST, rather than hiding it behind many unrelated registrations.
        // The exact same descriptor strings still come from the registry.
        foreach (var candidate in candidates.Where(c => directIds.Contains(c.Id))
            .OrderByDescending(c => c.Relevance)
            .ThenBy(c => c.Full.Length)
            .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
            direct.Append(candidate.Full);

        // The exact same runtime-registered parameter/enum/range/required data
        // is used by the capability preflight; there is no parallel JSON schema.
        StringBuilder result = new();
        string source = c.CapabilityContext;
        int marker = source.IndexOf("AVAILABLE PRIMITIVES", StringComparison.Ordinal);
        if (marker >= 0)
            result.AppendLine(NIRACognitionContextText.Bound(source[..marker], 3100));
        if (c.Cycle <= 1)
        {
            result.AppendLine("FIRST-ROUND DIRECT TOOL CHOICE: If the requested work can " +
                "be performed with an already registered DIRECT tool, propose its " +
                "exact capabilityRequests in THIS first decision; no discovery " +
                "round is needed. Prefer the smallest sufficient observational " +
                "operation and its returned evidence. Do not start an interactive " +
                "session or multi-step navigation merely to retrieve public or " +
                "machine information if an available direct read-only primitive " +
                "can supply it. If a tool is DISCOVER, request its registered " +
                "schema first; do not guess required structured arguments. " +
                "Only real results justify a final answer. Registered tools are " +
                "never auto-executed or granted authorization by this directory.");
            var relevantDirect = candidates.Where(c => directIds.Contains(c.Id) && c.Relevance > 0)
                .OrderByDescending(c => c.Relevance)
                .ThenBy(c => c.Full.Length)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .Take(5).Select(c => c.Id).ToArray();
            if (relevantDirect.Length > 0)
                result.Append("REQUEST-RELEVANT REGISTERED DIRECT IDS (suggestions only): ")
                    .AppendLine(string.Join(", ", relevantDirect));
        }
        result.AppendLine("SHARED TOOL CONTRACT: These registered capabilities are " +
            "available to both the first cognition decision and EVERY internal " +
            "work continuation. If the user explicitly requires an actual " +
            "registered tool result, do not substitute an unaudited mental " +
            "calculation or remembered web/PC data; propose capabilityRequests " +
            "using the exact DIRECT descriptor. Later internal cycles may " +
            "request more registered tools based on previous receipts. " +
            "DISCOVER contracts require real schema discovery first. " +
            "The Executive alone validates, authorizes and runs proposals.");
        result.AppendLine("CAPABILITY IDENTITIES (all currently registered):")
            .Append(inventory);
        result.AppendLine("DIRECT CAPABILITY EXACT REGISTERED ARGUMENT DESCRIPTORS:")
            .Append(direct);
        result.AppendLine("DIRECT: capabilityRequests=[{\"capabilityId\":\"registered.id\"," +
            "\"arguments\":{...},\"reason\":\"why\"}]. Arguments are exact-case " +
            "registered names and correct JSON types, not strings wrapping arrays. " +
            "Absent optional parameters use their registered defaults. Do not " +
            "invent values, schemas, capability IDs or proof of execution.");
        result.AppendLine("DISCOVER: request contextRequests=[\"capabilities\"] " +
            "and capabilityIds=[\"exact.registered.id\"]. Do not fabricate its " +
            "arguments before its full registered schema is returned.");
        result.AppendLine("The model only proposes actions; the Executive validates, " +
            "authorizes and executes. A tool registration is NOT permission.");
        if (tools.Any(t => t.Id.StartsWith("filesystem.", StringComparison.OrdinalIgnoreCase)))
            result.AppendLine("CURRENT-USER KNOWN-FOLDER PATH CONTRACT: When the user " +
                "says my Downloads/Documents/Desktop/Pictures/Music/Videos, provide a " +
                "relative known-folder alias such as Downloads\\report.docx, NOT " +
                "C:\\Users\\<guessed-user>\\Downloads\\report.docx. The registered " +
                "filesystem path normalizer resolves that alias using the current " +
                "Windows user's configured location. Never infer the Windows account " +
                "name from NIRA's user's name or chat history. If a tool reports " +
                "UnverifiedOtherProfileKnownFolder, correct the intended current-user " +
                "path using the alias; do not request Defender changes or retry " +
                "the incorrect absolute path. Explicitly requested OTHER-user " +
                "destinations require observation and ordinary authorization.");
        if (!string.IsNullOrWhiteSpace(c.DynamicToolContext))
            result.AppendLine("DYNAMIC TOOL DIRECTORY (identity only; get contract " +
                "and required permission before invocation):\n" +
                NIRACognitionContextText.Bound(c.DynamicToolContext, 2200));
        return result.ToString();
    }
}