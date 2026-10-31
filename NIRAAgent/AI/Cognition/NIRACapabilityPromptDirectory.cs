using System.Text;

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
                Eligible = !requiredStructured && tool.Parameters.Count <= 13
            };
        }).ToArray();
        var directIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int directLength = 0;

        // Family seeding prevents small registered operations from becoming
        // discovery-only just because another namespace has many long schemas.
        var eligible = candidates.Where(c => c.Eligible).ToArray();
        var familySeeds = eligible.GroupBy(c => c.Family, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(c => c.Full.Length).ThenBy(c => c.Id,
                StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(c => c.Full.Length).ThenBy(c => c.Id,
                StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in familySeeds)
        {
            if (candidate.Full.Length > directBudget - directLength) continue;
            if (directIds.Add(candidate.Id)) directLength += candidate.Full.Length;
        }

        foreach (var candidate in eligible.OrderBy(c => c.Full.Length)
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
            if (isDirect) direct.Append(candidate.Full);
        }

        // The exact same runtime-registered parameter/enum/range/required data
        // is used by the capability preflight; there is no parallel JSON schema.
        StringBuilder result = new();
        string source = c.CapabilityContext;
        int marker = source.IndexOf("AVAILABLE PRIMITIVES", StringComparison.Ordinal);
        if (marker >= 0)
            result.AppendLine(NIRACognitionContextText.Bound(source[..marker], 3100));
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
        if (!string.IsNullOrWhiteSpace(c.DynamicToolContext))
            result.AppendLine("DYNAMIC TOOL DIRECTORY (identity only; get contract " +
                "and required permission before invocation):\n" +
                NIRACognitionContextText.Bound(c.DynamicToolContext, 2200));
        return result.ToString();
    }
}