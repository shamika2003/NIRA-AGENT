using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;
using NIRAAgent.Presentation;

namespace NIRAAgent.AI.Cognition;

/// <summary>
/// Optional zero-LLM completion evidence for a single, successful, read-only
/// calculator result. This is NOT a mathematical word-problem classifier:
/// the model chooses the formula; the trusted calculator proves its value.
/// If this small certificate cannot be established, normal independent
/// objective review remains available with no fixed call quota.
/// </summary>
public static class NIRAVerifiedNumericCompletionPolicy
{
    private static readonly Regex Number = new(
        @"(?<![\p{L}\p{N}_])[-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool HasVerifiedTerminalResult(
        NIRACognitionDecision decision,
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results,
        bool hasPendingOwnedTask,
        bool hasOtherWorkOrEvidence,
        bool priorReviewObjection)
    {
        return Evaluate(decision, requests, results, hasPendingOwnedTask,
            hasOtherWorkOrEvidence, priorReviewObjection).Eligible;
    }

    // Generic, non-sensitive eligibility report for run diagnostics.
    // No raw prompts, tool arguments, or personally identifiable data.
    public static (bool Eligible, string Reason) Evaluate(
        NIRACognitionDecision decision,
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results,
        bool hasPendingOwnedTask,
        bool hasOtherWorkOrEvidence,
        bool priorReviewObjection)
    {
        if (priorReviewObjection) return (false, "PriorReviewerObjection");
        if (hasPendingOwnedTask) return (false, "PendingOwnedTask");
        if (hasOtherWorkOrEvidence) return (false, "OtherWorkOrCapability");
        if (requests.Count != 1 || results.Count != 1)
            return (false, "MultipleOrMissingReceipts");
        if (decision.State != NIRACognitionState.Complete || !decision.EmitReply ||
            !decision.ReplyReady || !decision.CharacterReady)
            return (false, "TerminalDraftNotReady");
        if (decision.DisplayBlocks.Count(block =>
            string.Equals(block.Type, "metric", StringComparison.OrdinalIgnoreCase)) != 1)
            return (false, "NotExactlyOneMetric");
        bool eligible = VerifyShapeAndReceipt(decision, requests, results);
        return (eligible, eligible ? "VerifiedSingleMetric" : "AdditionalClaimsOrReceiptMismatch");
    }

    private static bool VerifyShapeAndReceipt(
        NIRACognitionDecision decision,
        IReadOnlyDictionary<string, NIRACapabilityRequest> requests,
        IReadOnlyDictionary<string, NIRACapabilityResult> results)
    {
        if (results.Count != 1 || requests.Count != 1 ||
            decision.State != NIRACognitionState.Complete || !decision.EmitReply ||
            !decision.ReplyReady || !decision.CharacterReady ||
            decision.ReplyPresentation != NIRAReplyPresentationMode.Natural ||
            string.IsNullOrWhiteSpace(decision.Reply) ||
            decision.ReviewExperience || decision.ReviewCommitment ||
            !string.IsNullOrWhiteSpace(decision.NovelExperienceEvidence) ||
            decision.RuntimeContractDiagnostics.Count != 0 ||
            decision.ContextRequests.Count != 0 || decision.CapabilityIds.Count != 0 ||
            decision.CapabilityRequests.Count != 0 || decision.ControlRequests.Count != 0 ||
            decision.MemorySearches.Count != 0 || decision.ConversationSearches.Count != 0 ||
            decision.GoalProposals.Count != 0 || decision.BranchProposals.Count != 0 ||
            decision.BranchWorkProposals.Count != 0 || decision.DynamicToolProposals.Count != 0 ||
            decision.DynamicToolInvocations.Count != 0 || decision.CommitmentProposals.Count != 0 ||
            decision.VisualPresentations.Count != 0 ||
            !string.IsNullOrWhiteSpace(decision.ProgressUpdate))
            return false;

        KeyValuePair<string, NIRACapabilityResult> observed = results.Single();
        if (!requests.TryGetValue(observed.Key, out NIRACapabilityRequest? request))
            return false;
        NIRACapabilityResult receipt = observed.Value;
        if (request.CapabilityId != NIRACapabilityIds.MathCalculate ||
            receipt.CapabilityId != NIRACapabilityIds.MathCalculate ||
            !receipt.Succeeded || receipt.Risk != NIRACapabilityRisk.Observe ||
            receipt.ChangedSystemState || receipt.OutcomeUncertain ||
            request.Arguments.ValueKind != JsonValueKind.Object ||
            !request.Arguments.TryGetProperty("expression", out JsonElement submitted) ||
            submitted.ValueKind != JsonValueKind.String)
            return false;

        // A single primary metric is an auditable numeric answer. Additional
        // independent metrics or structured conclusions still require review.
        NIRARichBlock[] metrics = decision.DisplayBlocks
            .Where(block => string.Equals(block.Type, "metric", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (metrics.Length != 1 ||
            decision.DisplayBlocks.Any(block => block.Type is "table" or "chart" or "file" or "code"))
            return false;

        try
        {
            // This fast path skips an independent LLM reviewer. Therefore the
            // receipt must be replayable from the EXACT registered request, not
            // merely a plausible JSON object with a matching displayed number.
            // math.calculate is a pure, bounded read-only decimal evaluator:
            // replay is local, does not access the PC or invoke an LLM, and
            // protects against altered expressions, steps, or numeric values.
            if (!string.Equals(observed.Key, request.BuildSignature(), StringComparison.Ordinal))
                return false;
            NIRACapabilityHandlerResult replay = new NIRAMathCalculationCapabilityHandler()
                .ExecuteAsync(request).GetAwaiter().GetResult();
            if (!replay.Succeeded || replay.ChangedSystemState ||
                !string.Equals(receipt.Output, replay.Output, StringComparison.Ordinal))
                return false;

            using JsonDocument doc = JsonDocument.Parse(receipt.Output);
            JsonElement root = doc.RootElement;
            if (root.GetProperty("engine").GetString() != "bounded-decimal-expression-v1")
                return false;
            // Named steps are an internal precision mechanism, not additional
            // independent requested answers. Check their provenance against the
            // submitted step contract before certifying a SINGLE displayed metric.
            JsonElement outputSteps = root.GetProperty("steps");
            if (outputSteps.ValueKind != JsonValueKind.Array)
                return false;
            int submittedSteps = request.Arguments.TryGetProperty("steps", out JsonElement requestSteps) &&
                requestSteps.ValueKind == JsonValueKind.Array ? requestSteps.GetArrayLength() : 0;
            if (outputSteps.GetArrayLength() != submittedSteps)
                return false;
            for (int index = 0; index < submittedSteps; index++)
            {
                JsonElement submittedStep = requestSteps[index];
                JsonElement evaluatedStep = outputSteps[index];
                if (submittedStep.GetProperty("name").GetString() != evaluatedStep.GetProperty("name").GetString() ||
                    submittedStep.GetProperty("expression").GetString() != evaluatedStep.GetProperty("expression").GetString())
                    return false;
            }
            JsonElement final = root.GetProperty("final");
            if (submitted.GetString() != final.GetProperty("expression").GetString())
                return false;
            if (!decimal.TryParse(final.GetProperty("value").GetString(),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ||
                !decimal.TryParse(final.GetProperty("displayValue").GetString(),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out decimal displayed))
                return false;
            int places = root.GetProperty("displayDecimals").GetInt32();
            if (places < 0 || places > 12 ||
                decimal.Round(value, places, MidpointRounding.AwayFromZero) != displayed)
                return false;

            MatchCollection tokens = Number.Matches(metrics[0].Text ?? string.Empty);
            // Refuse to certify a block containing several figures, ambiguous
            // unit conversions, or a number other than the tool's actual result.
            if (tokens.Count != 1 ||
                !decimal.TryParse(tokens[0].Value.Replace(",", ""),
                    NumberStyles.Number, CultureInfo.InvariantCulture,
                    out decimal reported))
                return false;
            return reported == value || reported == displayed;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or
                                   KeyNotFoundException or FormatException or OverflowException or
                                   ArgumentException)
        {
            return false;
        }
    }
}
