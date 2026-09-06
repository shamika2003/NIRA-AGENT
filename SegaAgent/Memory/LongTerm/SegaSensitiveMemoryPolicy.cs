/*
 * filename: SegaSensitiveMemoryPolicy.cs
 */

using System.Text.RegularExpressions;

namespace SegaAgent.Memory.LongTerm;


// =============================================================
// SENSITIVE DURABLE-MEMORY POLICY
//
// Durable memory is not a credential vault.
//
// The policy is intentionally narrow: it blocks values that look
// like actual secrets/tokens/private keys rather than blocking a
// conversation merely because it mentions words such as
// "password" or "API key".
// =============================================================

public static class SegaSensitiveMemoryPolicy
{
    private static readonly Regex PrivateKeyRegex =
        new(
            @"-----BEGIN(?: [A-Z0-9]+)? PRIVATE KEY-----",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant |
            RegexOptions.Compiled);


    private static readonly Regex JwtRegex =
        new(
            @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b",
            RegexOptions.CultureInvariant |
            RegexOptions.Compiled);


    private static readonly Regex ServiceTokenRegex =
        new(
            @"\b(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16})\b",
            RegexOptions.CultureInvariant |
            RegexOptions.Compiled);


    private static readonly Regex CredentialAssignmentRegex =
        new(
            """(?im)\b(?:password|passwd|pwd|api[_ -]?key|secret|access[_ -]?token|refresh[_ -]?token|bearer)\b\s*[:=]\s*["']?(?<value>[^\s,;"']{8,})""",
            RegexOptions.CultureInvariant |
            RegexOptions.Compiled);


    private static readonly HashSet<string> NonSecretPlaceholders =
        new(
            new[]
            {
                "unknown",
                "redacted",
                "hidden",
                "unavailable",
                "not-set",
                "not_set",
                "notset",
                "example",
                "placeholder",
                "none",
                "null"
            },
            StringComparer.OrdinalIgnoreCase);


    public static bool ShouldBlockDurableStorage(
        SegaMemoryCandidate candidate,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);


        if (ContainsSensitiveSecret(
                candidate.Content))
        {
            reason =
                "Candidate content appears to contain credential or private-key material.";


            return true;
        }


        if (ContainsSensitiveSecret(
                candidate.Provenance.SourceExcerpt))
        {
            reason =
                "Candidate provenance excerpt appears to contain credential or private-key material.";


            return true;
        }


        reason =
            string.Empty;


        return false;
    }


    public static bool ContainsSensitiveSecret(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }


        if (
            PrivateKeyRegex.IsMatch(
                value)
            ||
            JwtRegex.IsMatch(
                value)
            ||
            ServiceTokenRegex.IsMatch(
                value))
        {
            return true;
        }


        MatchCollection assignments =
            CredentialAssignmentRegex.Matches(
                value);


        foreach (Match assignment
                 in assignments)
        {
            string secret =
                assignment.Groups[
                        "value"]
                    .Value
                    .Trim();


            if (NonSecretPlaceholders.Contains(
                    secret))
            {
                continue;
            }


            return true;
        }


        return false;
    }
}
