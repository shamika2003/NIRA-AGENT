using System.Diagnostics;
using System.Text;
using NIRAAgent.Mind;

namespace NIRAAgent.AI.Cognition;

// NIRA-owned, app-agnostic prompt selector. App-specific YAML files are owned
// by the ELVARA integration project. This is guidance, never authority.
internal static class NIRAScopedApplicationPromptCatalog
{
    private const int MaximumAppsPerCall = 2;
    private const int MaximumPromptCharacters = 3000;

    public static NIRAScopedPromptSelection Select(
        NIRACognitionContext context,
        IReadOnlySet<string>? expandedCapabilityIds)
    {
        ArgumentNullException.ThrowIfNull(context);

        HashSet<string> apps = new(StringComparer.OrdinalIgnoreCase);

        // Every cognition inference is stateless. Preserve guidance on every
        // cycle of a TRUSTED embedded-app event, including schema repair and
        // multi-capability investigation. Never infer origin from user text.
        if (TryGetEmbeddedAppId(context, out string embeddedId))
        {
            apps.Add(embeddedId);
        }

        // Only the Executive supplies these IDs, taken from actual trusted
        // capability results. Do not scan raw tool output for an ID: retrieved
        // data is untrusted and could otherwise spoof app-prompt activation.
        foreach (string capabilityId in context.ObservedCapabilityIds)
        {
            if (TryGetElvaraAppId(capabilityId, out string appId))
            {
                apps.Add(appId);
            }
        }

        // Expanded capability IDs are selected by the normal NIRA Executive
        // against the registered catalog, not extracted from natural language.
        if (expandedCapabilityIds != null)
        {
            foreach (string id in expandedCapabilityIds)
            {
                if (TryGetElvaraAppId(id, out string appId))
                {
                    apps.Add(appId);
                }
            }
        }

        if (apps.Count == 0)
        {
            return new NIRAScopedPromptSelection(string.Empty, string.Empty);
        }

        StringBuilder guidance = new();
        List<string> loaded = new();
        foreach (string appId in apps.OrderBy(x => x, StringComparer.Ordinal).Take(MaximumAppsPerCall))
        {
            // Strict identifier validation prevents path traversal. Each app
            // only gets its own opt-in prompt asset; missing files are normal.
            string path = Path.Combine(AppContext.BaseDirectory,
                "Prompt", $"elvara_{appId}.yaml");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                string content = File.ReadAllText(path).Trim();
                if (content.Length == 0)
                {
                    continue;
                }

                if (content.Length > MaximumPromptCharacters)
                {
                    Debug.WriteLine($"[ScopedPrompt] SKIPPED_TOO_LARGE | App={appId} | Chars={content.Length}");
                    continue;
                }

                guidance.AppendLine($"APP={appId} (domain guidance only; not runtime permissions)");
                guidance.AppendLine(content);
                guidance.AppendLine();
                loaded.Add(appId);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[ScopedPrompt] READ_FAILED | App={appId} | Error={ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"[ScopedPrompt] READ_FAILED | App={appId} | Error={ex.Message}");
            }
        }

        return new NIRAScopedPromptSelection(
            guidance.ToString().Trim(), string.Join(",", loaded));
    }

    // Shared, non-authorizing origin resolver for scoped guidance and compact
    // capability visibility. Executive/runtime continue to enforce permissions.
    internal static bool TryGetEmbeddedAppId(
        NIRACognitionContext context,
        out string appId)
    {
        appId = string.Empty;
        return context.Event.Source == NIRAMindEventSource.User &&
            string.Equals(context.Event.Name, "ExternalAppUserMessage",
                StringComparison.Ordinal) &&
            context.Event.Metadata.TryGetValue("externalAppId", out string? origin) &&
            TryNormalizeAppId(origin, out appId);
    }

    private static bool TryGetElvaraAppId(string? capabilityId, out string appId)
    {
        appId = string.Empty;
        if (string.IsNullOrWhiteSpace(capabilityId) ||
            !capabilityId.StartsWith("elvara.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int separator = capabilityId.IndexOf('.', 7);
        return separator > 7 &&
            TryNormalizeAppId(capabilityId.Substring(7, separator - 7), out appId);
    }

    private static bool TryNormalizeAppId(string? candidate, out string appId)
    {
        appId = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        string cleaned = candidate.Trim().ToLowerInvariant();
        if (cleaned.Length is < 1 or > 48 ||
            !char.IsAsciiLetterOrDigit(cleaned[0]) ||
            cleaned.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')))
        {
            return false;
        }
        appId = cleaned;
        return true;
    }
}

internal readonly record struct NIRAScopedPromptSelection(
    string Guidance,
    string AppIds);
