/*
 * filename: NIRALearnedSkillService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using NIRAAgent.Tools;

namespace NIRAAgent.Skills;

public sealed class NIRALearnedSkillService
{
    private const double MinimumCreateConfidence = 0.78;
    private const double MinimumReviseConfidence = 0.72;
    private const int MinimumSuccessfulExecutionsForLearning = 2;
    private const double MinimumVersionReliabilityForLearning = 0.60;

    // Generalization is intentionally stricter than basic skill learning.
    // NIRA may only claim a procedure is portable after the SAME current
    // parameterized tool version has succeeded across multiple distinct
    // invocation contexts. Raw argument values are never stored here; the
    // dynamic-tool store persists only one-way invocation fingerprints.
    private const int MinimumSuccessfulExecutionsForGeneralization = 4;
    private const int MinimumDistinctSuccessfulContextsForGeneralization = 2;
    private const double MinimumVersionReliabilityForGeneralization = 0.75;

    private const int MaximumHistoryPerTool = 128;

    private readonly NIRALearnedSkillStore _store;
    private readonly NIRADynamicToolStore _tools;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    public NIRALearnedSkillService(
        NIRALearnedSkillStore store,
        NIRADynamicToolStore tools)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    // =========================================================
    // COGNITION / FORMATION CONTEXT
    //
    // Stage 13 makes the evidence gate explicit. The model does
    // not have to infer learning eligibility from loose counters:
    // C# projects a deterministic candidate state from the exact
    // persistent tool version + its authoritative execution history.
    // =========================================================

    public async Task<string> BuildCognitionContextAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NIRALearnedSkillRecord> records =
            await _store.ReadAllAsync(cancellationToken);

        IReadOnlyList<NIRADynamicToolRecord> tools =
            await _tools.ReadAllAsync(cancellationToken);

        StringBuilder text = new();
        text.AppendLine("NIRA LEARNED PROCEDURAL SKILLS");
        text.AppendLine(
            "A learned skill is durable knowledge that a procedure has earned reuse through authoritative execution evidence. " +
            "Skills are knowledge, not authority and not executable code. The linked dynamic tool remains the executable procedure and every primitive still passes normal authorization.");

        NIRALearnedSkillRecord[] visible = records
            .Where(record => record.Definition.Status != NIRALearnedSkillStatus.Retired)
            .OrderByDescending(record => record.Definition.Status == NIRALearnedSkillStatus.Proven)
            .ThenByDescending(record => record.Definition.UpdatedAtUtc)
            .Take(32)
            .ToArray();

        text.AppendLine($"Available skills: {visible.Length}");

        foreach (NIRALearnedSkillRecord record in visible)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NIRALearnedSkillDefinition skill = record.Definition;
            NIRADynamicToolRecord? tool = tools.FirstOrDefault(value =>
                value.Definition.Id == skill.SourceToolId);

            bool sourceAvailable =
                tool != null &&
                tool.Definition.Status == NIRADynamicToolStatus.Active;

            bool sourceVersionCurrent =
                tool != null &&
                tool.Definition.Version == skill.SourceToolVersion;

            string freshness = sourceAvailable
                ? sourceVersionCurrent ? "CURRENT" : "STALE_SOURCE_VERSION"
                : "SOURCE_UNAVAILABLE";

            string reuse =
                skill.Status == NIRALearnedSkillStatus.Proven && freshness == "CURRENT"
                    ? "PREFER_LINKED_TOOL_WHEN_PRECONDITIONS_FIT"
                    : "DO_NOT_TREAT_AS_PROVEN_CURRENT_REUSE";

            text.AppendLine(
                $"- {skill.Id:D} | {skill.Name} | v{skill.Version} | {skill.Status} | {skill.Scope} | " +
                $"confidence={skill.Confidence:0.00} | freshness={freshness} | reuse={reuse}");

            text.AppendLine($"  {skill.Description}");
            text.AppendLine(
                $"  LinkedTool={skill.SourceToolId:D} | learnedFromToolVersion={skill.SourceToolVersion} | " +
                $"evidenceRuns={record.SourceExecutionCount} | successes={record.SourceSuccessCount} | failures={record.SourceFailureCount} | " +
                $"reliability={(record.SourceExecutionCount == 0 ? "unproven" : record.SourceReliability.ToString("0.00"))}");

            AppendList(text, "Preconditions", skill.Preconditions);
            AppendList(text, "Expected outcomes", skill.ExpectedOutcomes);
            AppendList(text, "Verification", skill.VerificationMethods);

            if (skill.FailurePatternEvidence.Count > 0)
            {
                text.AppendLine("  Grounded failure patterns:");
                foreach (NIRALearnedSkillFailurePatternEvidence failure in skill.FailurePatternEvidence.Take(8))
                {
                    text.AppendLine(
                        $"    - v{failure.SourceToolVersion} | {Bound(failure.Pattern, 500)} | " +
                        $"EvidenceQuote=\"{Bound(failure.EvidenceQuote, 500)}\"");
                }
            }

            string[] legacyPatterns = skill.KnownFailurePatterns
                .Where(pattern => !skill.FailurePatternEvidence.Any(evidence =>
                    string.Equals(evidence.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
                .Take(6)
                .ToArray();

            if (legacyPatterns.Length > 0)
            {
                text.AppendLine("  Legacy failure notes (older unstructured skill data; do not treat as newly runtime-grounded):");
                foreach (string pattern in legacyPatterns)
                    text.AppendLine($"    - {Bound(pattern, 500)}");
            }

            if (!string.IsNullOrWhiteSpace(record.LastEvidenceSummary))
                text.AppendLine($"  Last evidence: {Bound(record.LastEvidenceSummary, 800)}");

            IReadOnlyList<NIRALearnedSkillEvidenceRecord> skillHistory =
                await _store.ReadEvidenceHistoryAsync(skill.Id, 3, cancellationToken);

            if (skillHistory.Count > 0)
            {
                text.AppendLine("  Recent skill evidence:");
                foreach (NIRALearnedSkillEvidenceRecord evidence in skillHistory)
                {
                    string detail = evidence.Succeeded
                        ? evidence.Summary
                        : string.IsNullOrWhiteSpace(evidence.FailureReason)
                            ? evidence.Summary
                            : evidence.FailureReason;
                    text.AppendLine(
                        $"    - {evidence.ObservedAtUtc:O} | toolV={evidence.SourceToolVersion} | " +
                        $"{(evidence.Succeeded ? "SUCCESS" : "FAILURE")} | {Bound(detail, 650)}");
                }
            }

            IReadOnlyList<NIRALearnedSkillReuseRecord> reuseHistory =
                await _store.ReadReuseHistoryAsync(skill.Id, 8, cancellationToken);

            if (reuseHistory.Count > 0)
            {
                int successfulReuses = reuseHistory.Count(value => value.Succeeded);
                text.AppendLine(
                    $"  Explicit learned-skill reuse: recent={reuseHistory.Count} | successes={successfulReuses} | " +
                    $"last={reuseHistory[0].ReusedAtUtc:O}");

                foreach (NIRALearnedSkillReuseRecord reuseRecord in reuseHistory.Take(3))
                {
                    string detail = reuseRecord.Succeeded
                        ? reuseRecord.Summary
                        : string.IsNullOrWhiteSpace(reuseRecord.FailureReason)
                            ? reuseRecord.Summary
                            : reuseRecord.FailureReason;
                    text.AppendLine(
                        $"    - {reuseRecord.ReusedAtUtc:O} | toolV={reuseRecord.SourceToolVersion} | " +
                        $"{(reuseRecord.Succeeded ? "SUCCESS" : "FAILURE")} | {Bound(detail, 650)}");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("PROCEDURAL LEARNING CANDIDATES (AUTHORITATIVE RUNTIME EVIDENCE GATE)");
        text.AppendLine(
            "CandidateState is computed by C# from the exact persistent source-tool version and execution history. " +
            "Create/Revise proposals still require semantic reasoning, but the runtime will reject proposals that do not satisfy this evidence gate.");

        NIRADynamicToolRecord[] persistentTools = tools
            .Where(tool => tool.Definition.Persistence == NIRADynamicToolPersistence.Persistent)
            .OrderByDescending(tool => tool.Definition.UpdatedAtUtc)
            .Take(24)
            .ToArray();

        if (persistentTools.Length == 0)
        {
            text.AppendLine("No persistent dynamic tools are currently available for procedural learning.");
        }

        foreach (NIRADynamicToolRecord tool in persistentTools)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NIRALearnedSkillRecord? linked = records
                .Where(record =>
                    record.Definition.SourceToolId == tool.Definition.Id &&
                    record.Definition.Status != NIRALearnedSkillStatus.Retired)
                .OrderByDescending(record => record.Definition.UpdatedAtUtc)
                .FirstOrDefault();

            SourceVersionEvidence currentEvidence =
                await ReadVersionEvidenceAsync(
                    tool,
                    tool.Definition.Version,
                    cancellationToken);

            NIRALearnedSkillCandidateState candidateState =
                ResolveCandidateState(tool, linked, currentEvidence);

            GeneralizationEvidence generalization =
                BuildGeneralizationEvidence(tool, currentEvidence);

            text.AppendLine(
                $"- Tool={tool.Definition.Id:D} | Name={tool.Definition.Name} | CurrentToolVersion={tool.Definition.Version} | " +
                $"ToolStatus={tool.Definition.Status} | CandidateState={candidateState}");

            text.AppendLine(
                $"  CurrentVersionEvidence: runs={currentEvidence.ExecutionCount} | successes={currentEvidence.SuccessCount} | " +
                $"failures={currentEvidence.FailureCount} | reliability={FormatReliability(currentEvidence)} | " +
                $"evidenceConfidence={ComputeEvidenceConfidence(currentEvidence):0.00}");

            text.AppendLine(
                $"  GeneralizationState={ResolveGeneralizationState(linked, generalization)} | " +
                $"parameterizedProcedure={generalization.ParameterizedProcedure} | " +
                $"successfulContexts={generalization.DistinctSuccessfulContexts} | " +
                $"fingerprintedSuccesses={generalization.FingerprintedSuccessCount} | " +
                $"requiredSuccesses={MinimumSuccessfulExecutionsForGeneralization} | " +
                $"requiredDistinctContexts={MinimumDistinctSuccessfulContextsForGeneralization}");

            if (linked != null)
            {
                text.AppendLine(
                    $"  ExistingSkill={linked.Definition.Id:D} | SkillStatus={linked.Definition.Status} | " +
                    $"SkillVersion={linked.Definition.Version} | LearnedFromToolVersion={linked.Definition.SourceToolVersion}");
            }
            else
            {
                text.AppendLine("  ExistingSkill=-");
            }

            NIRADynamicToolExecutionHistoryRecord[] failures = currentEvidence.History
                .Where(value => !value.Succeeded)
                .OrderByDescending(value => value.RecordedAtUtc)
                .Take(3)
                .ToArray();

            if (failures.Length > 0)
            {
                text.AppendLine("  RecentFailureEvidence (exact text may ground failurePatterns[].evidenceQuote):");
                foreach (NIRADynamicToolExecutionHistoryRecord failure in failures)
                {
                    string detail = string.IsNullOrWhiteSpace(failure.FailureReason)
                        ? failure.Summary
                        : failure.FailureReason;
                    text.AppendLine(
                        $"    - v{failure.ToolVersion} | {failure.RecordedAtUtc:O} | FailureEvidence=\"{Bound(detail, 850)}\"");
                }
            }
        }

        return text.ToString().Trim();
    }

    // =========================================================
    // EXPLICIT PROVEN-SKILL REUSE PROVENANCE
    //
    // A learned skill never grants authority. sourceSkillId is only
    // provenance proving that cognition intentionally selected a
    // current Proven procedure. The linked dynamic tool still owns
    // executable structure and its primitives still pass Stage 10.
    // =========================================================

    public async Task<NIRALearnedSkillRecord> ValidateReuseAsync(
        string sourceSkillId,
        NIRADynamicToolDefinition sourceTool,
        CancellationToken cancellationToken = default)
    {
        if (!TryGuid(sourceSkillId, out Guid skillId))
            throw new InvalidOperationException(
                "Learned-skill reuse requires an exact authoritative skill GUID.");

        NIRALearnedSkillRecord? record =
            await _store.ReadByIdAsync(skillId, cancellationToken);

        if (record == null)
            throw new InvalidOperationException(
                "The learned skill cited for this dynamic-tool invocation does not exist.");

        NIRALearnedSkillDefinition skill = record.Definition;

        if (skill.Status != NIRALearnedSkillStatus.Proven)
            throw new InvalidOperationException(
                $"Learned skill '{skill.Name}' is {skill.Status}, not current Proven reusable knowledge.");

        if (sourceTool.Persistence != NIRADynamicToolPersistence.Persistent)
            throw new InvalidOperationException(
                "A durable learned skill can only reuse its persistent source dynamic tool.");

        if (sourceTool.Status != NIRADynamicToolStatus.Active)
            throw new InvalidOperationException(
                $"Learned skill '{skill.Name}' cannot be reused because its source tool is {sourceTool.Status}.");

        NIRADynamicToolRecord? currentTool =
            await _tools.ReadByIdAsync(sourceTool.Id, cancellationToken);

        if (currentTool == null ||
            currentTool.Definition.Status != NIRADynamicToolStatus.Active ||
            currentTool.Definition.Version != sourceTool.Version)
        {
            throw new InvalidOperationException(
                "The source dynamic tool changed or became unavailable before learned-skill reuse could be validated.");
        }

        if (skill.SourceToolId != sourceTool.Id)
            throw new InvalidOperationException(
                "The cited learned skill does not own the requested dynamic tool.");

        if (skill.SourceToolVersion != sourceTool.Version)
            throw new InvalidOperationException(
                $"Learned skill '{skill.Name}' is stale: learned tool version {skill.SourceToolVersion}, current tool version {sourceTool.Version}.");

        return record;
    }

    public async Task RecordReuseAsync(
        NIRALearnedSkillRecord skill,
        NIRADynamicToolDefinition sourceTool,
        NIRADynamicToolExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(sourceTool);
        ArgumentNullException.ThrowIfNull(result);

        // Reuse provenance describes the validated dispatch decision, not the
        // skill's status after the execution result is learned. A failed run
        // may legitimately downgrade Proven -> Emerging in the evidence pass;
        // that must not erase the fact that this execution was selected through
        // the skill while it was valid at dispatch time.
        if (skill.Definition.SourceToolId != sourceTool.Id ||
            skill.Definition.SourceToolVersion != sourceTool.Version)
        {
            throw new InvalidOperationException(
                "Learned-skill reuse provenance does not match the executed source tool/version.");
        }

        await _store.RecordReuseAsync(
            new NIRALearnedSkillReuseRecord
            {
                ReuseId = Guid.NewGuid(),
                SkillId = skill.Definition.Id,
                SourceToolId = sourceTool.Id,
                SourceToolVersion = sourceTool.Version,
                Succeeded = result.Succeeded,
                Summary = result.Summary,
                FailureReason = result.FailureReason,
                ReusedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);

        Debug.WriteLine(
            $"[LearnedSkill] REUSE | Skill={skill.Definition.Name} | SkillId={skill.Definition.Id:D} | " +
            $"Tool={sourceTool.Name} | ToolId={sourceTool.Id:D} | ToolVersion={sourceTool.Version} | " +
            $"Success={result.Succeeded}");
    }

    // =========================================================
    // SOURCE-TOOL LIFECYCLE RECONCILIATION
    //
    // Retirement of an executable source invalidates future reuse,
    // but the procedural knowledge remains valuable history. Therefore
    // linked active skills are deprecated rather than deleted/retired.
    // Disabled tools are handled by freshness/reuse validation because
    // disablement can be temporary.
    // =========================================================

    public async Task ReconcileSourceToolLifecycleAsync(
        NIRADynamicToolDefinition sourceTool,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTool);

        if (sourceTool.Persistence != NIRADynamicToolPersistence.Persistent ||
            sourceTool.Status != NIRADynamicToolStatus.Retired)
        {
            return;
        }

        IReadOnlyList<NIRALearnedSkillRecord> linked =
            await _store.ReadBySourceToolIdAsync(sourceTool.Id, cancellationToken);

        foreach (NIRALearnedSkillRecord record in linked)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.Definition.Status is
                NIRALearnedSkillStatus.Deprecated or
                NIRALearnedSkillStatus.Retired)
            {
                continue;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            NIRALearnedSkillDefinition updated = record.Definition with
            {
                Status = NIRALearnedSkillStatus.Deprecated,
                Version = record.Definition.Version + 1,
                LearningReason =
                    $"Deprecated automatically because source dynamic tool '{sourceTool.Name}' ({sourceTool.Id:D}) was retired. " +
                    $"Previous learning reason: {record.Definition.LearningReason}",
                UpdatedAtUtc = now
            };

            await _store.UpsertAsync(
                record with { Definition = updated.Normalize() },
                cancellationToken);

            Debug.WriteLine(
                $"[LearnedSkill] DEPRECATED | Skill={updated.Name} | SkillId={updated.Id:D} | " +
                $"Reason=SourceToolRetired | ToolId={sourceTool.Id:D}");
        }
    }

    public async Task ReconcileAllSourceToolLifecyclesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NIRADynamicToolRecord> tools =
            await _tools.ReadAllAsync(cancellationToken);

        foreach (NIRADynamicToolRecord tool in tools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReconcileSourceToolLifecycleAsync(
                tool.Definition,
                cancellationToken);
        }
    }

    // =========================================================
    // FORMATION MUTATION
    // =========================================================

    public async Task<IReadOnlyList<NIRALearnedSkillMutationResult>> ApplyFormationProposalsAsync(
        IReadOnlyList<NIRALearnedSkillFormationProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        List<NIRALearnedSkillMutationResult> results = new();
        if (proposals == null) return results;

        HashSet<string> signatures = new(StringComparer.OrdinalIgnoreCase);

        foreach (NIRALearnedSkillFormationProposal raw in proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NIRALearnedSkillFormationProposal proposal;
            try
            {
                proposal = raw.Normalize();
            }
            catch (Exception ex)
            {
                results.Add(Rejected(ex.Message));
                continue;
            }

            if (!signatures.Add(proposal.BuildSignature()))
                continue;

            await _mutationLock.WaitAsync(cancellationToken);
            try
            {
                results.Add(await ApplyOneAsync(proposal, cancellationToken));
            }
            catch (Exception ex)
            {
                results.Add(Rejected(ex.Message));
            }
            finally
            {
                _mutationLock.Release();
            }
        }

        return results;
    }

    // =========================================================
    // CONTINUOUS AUTHORITATIVE EXECUTION EVIDENCE
    //
    // Existing skills learn from every linked persistent-tool run.
    // A newer tool version does NOT silently rewrite the skill's
    // source version. Its evidence is recorded, then the candidate
    // gate can become RevisionEligible for a semantic formation pass.
    // =========================================================

    public async Task RecordLinkedToolExecutionAsync(
        NIRADynamicToolDefinition sourceTool,
        NIRADynamicToolExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        if (sourceTool.Persistence != NIRADynamicToolPersistence.Persistent)
            return;

        IReadOnlyList<NIRALearnedSkillRecord> linked =
            await _store.ReadBySourceToolIdAsync(sourceTool.Id, cancellationToken);

        if (linked.Count == 0)
            return;

        NIRADynamicToolRecord? currentTool =
            await _tools.ReadByIdAsync(sourceTool.Id, cancellationToken);

        if (currentTool == null)
            return;

        foreach (NIRALearnedSkillRecord record in linked)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.Definition.Status == NIRALearnedSkillStatus.Retired)
                continue;

            NIRALearnedSkillDefinition definition = record.Definition;

            SourceVersionEvidence learnedVersionEvidence =
                await ReadVersionEvidenceAsync(
                    currentTool,
                    definition.SourceToolVersion,
                    cancellationToken);

            bool sourceVersionCurrent =
                definition.SourceToolVersion == currentTool.Definition.Version;

            NIRALearnedSkillStatus status = definition.Status;
            if (sourceVersionCurrent &&
                status is not (NIRALearnedSkillStatus.Deprecated or NIRALearnedSkillStatus.Retired))
            {
                status = IsProven(learnedVersionEvidence)
                    ? NIRALearnedSkillStatus.Proven
                    : NIRALearnedSkillStatus.Emerging;
            }

            double evidenceConfidence =
                ComputeEvidenceConfidence(learnedVersionEvidence);

            double confidence = learnedVersionEvidence.ExecutionCount == 0
                ? definition.Confidence
                : Math.Clamp(
                    (0.30 * definition.Confidence) +
                    (0.70 * evidenceConfidence),
                    0.0,
                    1.0);

            NIRALearnedSkillDefinition updatedDefinition = definition with
            {
                Status = status,
                Confidence = confidence,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            NIRALearnedSkillRecord updated = record with
            {
                Definition = updatedDefinition,
                SourceExecutionCount = learnedVersionEvidence.ExecutionCount > 0
                    ? learnedVersionEvidence.ExecutionCount
                    : record.SourceExecutionCount,
                SourceSuccessCount = learnedVersionEvidence.ExecutionCount > 0
                    ? learnedVersionEvidence.SuccessCount
                    : record.SourceSuccessCount,
                SourceFailureCount = learnedVersionEvidence.ExecutionCount > 0
                    ? learnedVersionEvidence.FailureCount
                    : record.SourceFailureCount,
                LastEvidenceSummary = result.Summary,
                LastObservedAtUtc = DateTimeOffset.UtcNow
            };

            await _store.RecordEvidenceAsync(
                updated,
                result.ToolVersion,
                result.Succeeded,
                result.Summary,
                result.FailureReason,
                cancellationToken);

            Debug.WriteLine(
                $"[LearnedSkill] EVIDENCE | Skill={definition.Name} | SourceTool={sourceTool.Name} | " +
                $"ExecutedToolVersion={result.ToolVersion} | LearnedToolVersion={definition.SourceToolVersion} | " +
                $"Stale={!sourceVersionCurrent} | Success={result.Succeeded} | " +
                $"Confidence={updatedDefinition.Confidence:0.00} | Status={updatedDefinition.Status}");
        }
    }

    // =========================================================
    // PROVEN-REUSE GUARD
    //
    // Foreground cognition should prefer a current Proven skill.
    // This runtime guard additionally prevents creation of a new
    // dynamic tool whose normalized executable procedure is exactly
    // equivalent to one already owned by a current Proven skill.
    // This is structural, not phrase/name matching.
    // =========================================================

    public async Task<NIRALearnedSkillRecord?> FindEquivalentCurrentProvenSkillAsync(
        NIRADynamicToolDefinition candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        string candidateFingerprint =
            BuildProcedureFingerprint(candidate);

        IReadOnlyList<NIRALearnedSkillRecord> skills =
            await _store.ReadAllAsync(cancellationToken);

        foreach (NIRALearnedSkillRecord skill in skills)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (skill.Definition.Status != NIRALearnedSkillStatus.Proven)
                continue;

            NIRADynamicToolRecord? source =
                await _tools.ReadByIdAsync(
                    skill.Definition.SourceToolId,
                    cancellationToken);

            if (source == null ||
                source.Definition.Status != NIRADynamicToolStatus.Active ||
                source.Definition.Version != skill.Definition.SourceToolVersion)
            {
                continue;
            }

            if (string.Equals(
                    candidateFingerprint,
                    BuildProcedureFingerprint(source.Definition),
                    StringComparison.Ordinal))
            {
                return skill;
            }
        }

        return null;
    }

    private async Task<NIRALearnedSkillMutationResult> ApplyOneAsync(
        NIRALearnedSkillFormationProposal proposal,
        CancellationToken cancellationToken)
    {
        return proposal.Action switch
        {
            NIRALearnedSkillProposalAction.Create =>
                await CreateAsync(proposal, cancellationToken),

            NIRALearnedSkillProposalAction.Revise =>
                await ReviseAsync(proposal, cancellationToken),

            NIRALearnedSkillProposalAction.Deprecate =>
                await SetStatusAsync(
                    proposal,
                    NIRALearnedSkillStatus.Deprecated,
                    NIRALearnedSkillApplyAction.Deprecated,
                    cancellationToken),

            NIRALearnedSkillProposalAction.Retire =>
                await SetStatusAsync(
                    proposal,
                    NIRALearnedSkillStatus.Retired,
                    NIRALearnedSkillApplyAction.Retired,
                    cancellationToken),

            _ => Rejected("Unsupported learned skill proposal action.")
        };
    }

    private async Task<NIRALearnedSkillMutationResult> CreateAsync(
        NIRALearnedSkillFormationProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumCreateConfidence)
            return Rejected("Learned skill create confidence is below the authoritative threshold.");

        if (!TryGuid(proposal.SourceToolId, out Guid sourceToolId))
            return Rejected("Learned skill creation requires an exact persistent source tool GUID.");

        NIRADynamicToolRecord? tool =
            await _tools.ReadByIdAsync(sourceToolId, cancellationToken);

        if (tool == null)
            return Rejected("The proposed source dynamic tool does not exist.");

        if (tool.Definition.Persistence != NIRADynamicToolPersistence.Persistent)
            return Rejected("Only persistent dynamic tools can become durable learned skills.");

        if (tool.Definition.Status != NIRADynamicToolStatus.Active)
            return Rejected("A durable learned skill can only be created from an active persistent source tool.");

        if (proposal.SourceToolVersion != tool.Definition.Version)
            return Rejected("Learned skill creation must cite the current authoritative source tool version.");

        SourceVersionEvidence evidence =
            await ReadVersionEvidenceAsync(
                tool,
                tool.Definition.Version,
                cancellationToken);

        if (!HasLearningEvidence(evidence))
        {
            return Rejected(
                $"The current source-tool version has insufficient repeated evidence. " +
                $"Required: at least {MinimumSuccessfulExecutionsForLearning} successes and reliability >= {MinimumVersionReliabilityForLearning:0.00}. " +
                $"Observed: runs={evidence.ExecutionCount}, successes={evidence.SuccessCount}, reliability={FormatReliability(evidence)}.");
        }

        if (proposal.Scope == NIRALearnedSkillScope.Generalized)
        {
            GeneralizationEvidence generalization = BuildGeneralizationEvidence(tool, evidence);
            if (!HasGeneralizationEvidence(generalization))
                return Rejected(DescribeInsufficientGeneralizationEvidence(generalization));
        }

        if (proposal.ExpectedOutcomes.Count == 0 ||
            proposal.VerificationMethods.Count == 0)
        {
            return Rejected("A learned skill requires expected outcomes and at least one verification method.");
        }

        IReadOnlyList<NIRALearnedSkillRecord> existingForTool =
            await _store.ReadBySourceToolIdAsync(sourceToolId, cancellationToken);

        if (existingForTool.Any(record =>
                record.Definition.Status != NIRALearnedSkillStatus.Retired))
        {
            return Rejected(
                "An active learned skill already exists for this source tool; revise that exact skill instead of creating a parallel duplicate.");
        }

        GroundedFailurePatterns groundedFailures;
        try
        {
            groundedFailures = await ResolveGroundedFailurePatternsAsync(
                proposal,
                sourceToolId,
                tool.Definition.Version,
                existing: null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Rejected(ex.Message);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        double confidence =
            Math.Min(
                proposal.Confidence,
                ComputeEvidenceConfidence(evidence));

        NIRALearnedSkillDefinition definition =
            new NIRALearnedSkillDefinition
            {
                Id = Guid.NewGuid(),
                Name = proposal.Name,
                Description = proposal.Description,
                Version = 1,
                Scope = proposal.Scope,
                Status = IsProven(evidence)
                    ? NIRALearnedSkillStatus.Proven
                    : NIRALearnedSkillStatus.Emerging,
                SourceToolId = sourceToolId,
                SourceToolVersion = tool.Definition.Version,
                Preconditions = proposal.Preconditions,
                ExpectedOutcomes = proposal.ExpectedOutcomes,
                VerificationMethods = proposal.VerificationMethods,
                KnownFailurePatterns = groundedFailures.PatternNames,
                FailurePatternEvidence = groundedFailures.Evidence,
                Confidence = confidence,
                LearningReason = proposal.Reason,
                LastSemanticReviewAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }
            .Normalize();

        NIRALearnedSkillRecord record = new()
        {
            Definition = definition,
            SourceExecutionCount = evidence.ExecutionCount,
            SourceSuccessCount = evidence.SuccessCount,
            SourceFailureCount = evidence.FailureCount,
            LastEvidenceSummary = tool.LastResult,
            LastObservedAtUtc = tool.LastExecutedAtUtc
        };

        await _store.UpsertAsync(record, cancellationToken);

        Debug.WriteLine(
            $"[LearnedSkill] CREATED | Id={definition.Id:D} | Name={definition.Name} | " +
            $"SourceTool={sourceToolId:D} | ToolVersion={definition.SourceToolVersion} | " +
            $"Status={definition.Status} | Confidence={definition.Confidence:0.00}");

        return new NIRALearnedSkillMutationResult
        {
            Action = NIRALearnedSkillApplyAction.Created,
            Skill = definition,
            Reason = "Repeated authoritative evidence from the current source-tool version was sufficient to persist a learned procedural skill."
        };
    }

    private async Task<NIRALearnedSkillMutationResult> ReviseAsync(
        NIRALearnedSkillFormationProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumReviseConfidence)
            return Rejected("Learned skill revision confidence is below the authoritative threshold.");

        if (!TryGuid(proposal.SkillId, out Guid skillId))
            return Rejected("Learned skill revision requires an exact existing skill GUID.");

        NIRALearnedSkillRecord? existing =
            await _store.ReadByIdAsync(skillId, cancellationToken);

        if (existing == null)
            return Rejected("The learned skill to revise does not exist.");

        if (existing.Definition.Status == NIRALearnedSkillStatus.Retired)
            return Rejected("A retired learned skill cannot be revised.");

        Guid sourceToolId = existing.Definition.SourceToolId;

        if (TryGuid(proposal.SourceToolId, out Guid requestedToolId) &&
            requestedToolId != sourceToolId)
        {
            return Rejected(
                "Stage 13 revisions preserve source-tool lineage. Revise the existing source tool/skill lineage rather than silently relinking a learned skill to a different tool.");
        }

        NIRADynamicToolRecord? tool =
            await _tools.ReadByIdAsync(sourceToolId, cancellationToken);

        if (tool == null ||
            tool.Definition.Persistence != NIRADynamicToolPersistence.Persistent)
        {
            return Rejected("The learned skill revision requires its existing persistent source tool.");
        }

        if (tool.Definition.Status != NIRADynamicToolStatus.Active)
            return Rejected("The learned skill revision requires an active source tool.");

        int targetToolVersion =
            proposal.SourceToolVersion == 0
                ? tool.Definition.Version
                : proposal.SourceToolVersion;

        if (targetToolVersion != tool.Definition.Version)
            return Rejected("Learned skill revision must cite the current authoritative source tool version.");

        SourceVersionEvidence evidence =
            await ReadVersionEvidenceAsync(
                tool,
                targetToolVersion,
                cancellationToken);

        bool sourceVersionChanged =
            existing.Definition.SourceToolVersion != targetToolVersion;

        if (sourceVersionChanged && !HasLearningEvidence(evidence))
        {
            return Rejected(
                $"The newer source-tool version has not yet earned skill reconciliation. " +
                $"Observed v{targetToolVersion}: runs={evidence.ExecutionCount}, successes={evidence.SuccessCount}, reliability={FormatReliability(evidence)}.");
        }

        if (proposal.Scope == NIRALearnedSkillScope.Generalized)
        {
            GeneralizationEvidence generalization = BuildGeneralizationEvidence(tool, evidence);
            if (!HasGeneralizationEvidence(generalization))
                return Rejected(DescribeInsufficientGeneralizationEvidence(generalization));
        }

        GroundedFailurePatterns groundedFailures;
        try
        {
            groundedFailures = await ResolveGroundedFailurePatternsAsync(
                proposal,
                sourceToolId,
                targetToolVersion,
                existing,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Rejected(ex.Message);
        }

        double confidence = evidence.ExecutionCount == 0
            ? existing.Definition.Confidence
            : Math.Min(
                proposal.Confidence,
                ComputeEvidenceConfidence(evidence));

        NIRALearnedSkillDefinition revised = existing.Definition with
        {
            Name = string.IsNullOrWhiteSpace(proposal.Name)
                ? existing.Definition.Name
                : proposal.Name,

            Description = string.IsNullOrWhiteSpace(proposal.Description)
                ? existing.Definition.Description
                : proposal.Description,

            Scope = proposal.Scope,

            Status = IsProven(evidence)
                ? NIRALearnedSkillStatus.Proven
                : NIRALearnedSkillStatus.Emerging,

            SourceToolId = sourceToolId,
            SourceToolVersion = targetToolVersion,

            Preconditions = proposal.Preconditions.Count == 0
                ? existing.Definition.Preconditions
                : proposal.Preconditions,

            ExpectedOutcomes = proposal.ExpectedOutcomes.Count == 0
                ? existing.Definition.ExpectedOutcomes
                : proposal.ExpectedOutcomes,

            VerificationMethods = proposal.VerificationMethods.Count == 0
                ? existing.Definition.VerificationMethods
                : proposal.VerificationMethods,

            KnownFailurePatterns = groundedFailures.PatternNames,
            FailurePatternEvidence = groundedFailures.Evidence,
            Confidence = confidence,
            LearningReason = proposal.Reason,
            LastSemanticReviewAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        revised = revised.Normalize();

        if (!HasSemanticRevision(existing.Definition, revised))
        {
            NIRALearnedSkillDefinition reviewed = existing.Definition with
            {
                LastSemanticReviewAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await _store.UpsertAsync(
                existing with { Definition = reviewed },
                cancellationToken);

            return new NIRALearnedSkillMutationResult
            {
                Action = NIRALearnedSkillApplyAction.NoChange,
                Skill = reviewed,
                Reason = "Current execution evidence was semantically reviewed; no procedural knowledge or source-version change was warranted."
            };
        }

        revised = revised with
        {
            Version = existing.Definition.Version + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        NIRALearnedSkillRecord record = existing with
        {
            Definition = revised,
            SourceExecutionCount = evidence.ExecutionCount,
            SourceSuccessCount = evidence.SuccessCount,
            SourceFailureCount = evidence.FailureCount,
            LastEvidenceSummary = tool.LastResult,
            LastObservedAtUtc = tool.LastExecutedAtUtc
        };

        await _store.UpsertAsync(record, cancellationToken);

        Debug.WriteLine(
            $"[LearnedSkill] REVISED | Id={revised.Id:D} | Name={revised.Name} | SkillVersion={revised.Version} | " +
            $"SourceToolVersion={revised.SourceToolVersion} | Status={revised.Status} | Confidence={revised.Confidence:0.00}");

        return new NIRALearnedSkillMutationResult
        {
            Action = NIRALearnedSkillApplyAction.Revised,
            Skill = revised,
            Reason = sourceVersionChanged
                ? "The existing learned skill was reconciled to a newer source-tool version only after that version earned repeated authoritative evidence."
                : "The learned skill was revised from grounded current-version execution evidence."
        };
    }

    private async Task<NIRALearnedSkillMutationResult> SetStatusAsync(
        NIRALearnedSkillFormationProposal proposal,
        NIRALearnedSkillStatus status,
        NIRALearnedSkillApplyAction action,
        CancellationToken cancellationToken)
    {
        if (!TryGuid(proposal.SkillId, out Guid skillId))
            return Rejected("Learned skill status change requires an exact existing skill GUID.");

        NIRALearnedSkillRecord? existing =
            await _store.ReadByIdAsync(skillId, cancellationToken);

        if (existing == null)
            return Rejected("The learned skill does not exist.");

        if (existing.Definition.Status == status)
        {
            return new NIRALearnedSkillMutationResult
            {
                Action = NIRALearnedSkillApplyAction.NoChange,
                Skill = existing.Definition,
                Reason = $"Learned skill is already {status}."
            };
        }

        if (existing.Definition.Status == NIRALearnedSkillStatus.Retired)
            return Rejected("Retirement is terminal.");

        NIRALearnedSkillDefinition updated = existing.Definition with
        {
            Status = status,
            Version = existing.Definition.Version + 1,
            LearningReason = string.IsNullOrWhiteSpace(proposal.Reason)
                ? existing.Definition.LearningReason
                : proposal.Reason,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await _store.UpsertAsync(
            existing with { Definition = updated },
            cancellationToken);

        return new NIRALearnedSkillMutationResult
        {
            Action = action,
            Skill = updated,
            Reason = $"Learned skill status changed to {status}."
        };
    }

    // =========================================================
    // FAILURE-PATTERN GROUNDING
    // =========================================================

    private async Task<GroundedFailurePatterns> ResolveGroundedFailurePatternsAsync(
        NIRALearnedSkillFormationProposal proposal,
        Guid sourceToolId,
        int currentSourceVersion,
        NIRALearnedSkillRecord? existing,
        CancellationToken cancellationToken)
    {
        List<string> patternNames = existing?.Definition.KnownFailurePatterns
            .ToList()
            ?? new List<string>();

        List<NIRALearnedSkillFailurePatternEvidence> evidence =
            existing?.Definition.FailurePatternEvidence
                .ToList()
            ?? new List<NIRALearnedSkillFailurePatternEvidence>();

        string[] legacyNewPatterns = proposal.KnownFailurePatterns
            .Where(pattern =>
                !patternNames.Contains(pattern, StringComparer.OrdinalIgnoreCase) &&
                !proposal.FailurePatterns.Any(item =>
                    string.Equals(item.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (legacyNewPatterns.Length > 0)
        {
            throw new InvalidOperationException(
                "New learned-skill failure patterns require failurePatterns[] with an exact evidenceQuote from authoritative failed execution history. Ungrounded knownFailurePatterns entries are not accepted.");
        }

        if (proposal.FailurePatterns.Count == 0)
        {
            return new GroundedFailurePatterns(
                patternNames.Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray(),
                evidence.Take(16).ToArray());
        }

        IReadOnlyList<NIRADynamicToolExecutionHistoryRecord> history =
            await _tools.ReadExecutionHistoryAsync(
                sourceToolId,
                MaximumHistoryPerTool,
                cancellationToken);

        foreach (NIRALearnedSkillFailurePatternProposal raw in proposal.FailurePatterns)
        {
            NIRALearnedSkillFailurePatternProposal item = raw.Normalize();

            if (item.SourceToolVersion > currentSourceVersion)
            {
                throw new InvalidOperationException(
                    "A learned-skill failure pattern cannot cite a future source-tool version.");
            }

            NIRADynamicToolExecutionHistoryRecord? grounded = history
                .Where(record =>
                    record.ToolVersion == item.SourceToolVersion &&
                    !record.Succeeded)
                .FirstOrDefault(record =>
                    ContainsEvidenceQuote(record.FailureReason, item.EvidenceQuote) ||
                    ContainsEvidenceQuote(record.Summary, item.EvidenceQuote));

            if (grounded == null)
            {
                throw new InvalidOperationException(
                    $"Failure pattern '{Bound(item.Pattern, 180)}' was rejected because its evidenceQuote was not found in authoritative failed execution history for source-tool v{item.SourceToolVersion}.");
            }

            if (!patternNames.Contains(item.Pattern, StringComparer.OrdinalIgnoreCase))
                patternNames.Add(item.Pattern);

            if (!evidence.Any(existingEvidence =>
                    string.Equals(existingEvidence.Pattern, item.Pattern, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existingEvidence.EvidenceQuote, item.EvidenceQuote, StringComparison.OrdinalIgnoreCase) &&
                    existingEvidence.SourceToolVersion == item.SourceToolVersion))
            {
                evidence.Add(
                    new NIRALearnedSkillFailurePatternEvidence
                    {
                        Pattern = item.Pattern,
                        EvidenceQuote = item.EvidenceQuote,
                        SourceToolVersion = item.SourceToolVersion,
                        GroundedAtUtc = grounded.RecordedAtUtc
                    }
                    .Normalize());
            }
        }

        return new GroundedFailurePatterns(
            patternNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToArray(),
            evidence
                .Take(16)
                .ToArray());
    }

    // =========================================================
    // VERSION-SPECIFIC SOURCE EVIDENCE
    // =========================================================

    private async Task<SourceVersionEvidence> ReadVersionEvidenceAsync(
        NIRADynamicToolRecord tool,
        int toolVersion,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NIRADynamicToolExecutionHistoryRecord> allHistory =
            await _tools.ReadExecutionHistoryAsync(
                tool.Definition.Id,
                MaximumHistoryPerTool,
                cancellationToken);

        NIRADynamicToolExecutionHistoryRecord[] versionHistory = allHistory
            .Where(value => value.ToolVersion == toolVersion)
            .OrderByDescending(value => value.RecordedAtUtc)
            .ToArray();

        int executionCount = versionHistory.Length;
        int successCount = versionHistory.Count(value => value.Succeeded);
        int failureCount = executionCount - successCount;

        // Schema-v1 tools may have aggregate counters that predate execution
        // history. For an unrevised v1 tool, those durable counters are still
        // authoritative enough for the basic eligibility gate; failure-pattern
        // grounding nevertheless requires a concrete history row.
        if (toolVersion == 1 &&
            tool.Definition.Version == 1 &&
            tool.ExecutionCount > executionCount)
        {
            executionCount = tool.ExecutionCount;
            successCount = tool.SuccessCount;
            failureCount = tool.FailureCount;
        }

        return new SourceVersionEvidence(
            toolVersion,
            executionCount,
            successCount,
            failureCount,
            versionHistory);
    }

    private static GeneralizationEvidence BuildGeneralizationEvidence(
        NIRADynamicToolRecord tool,
        SourceVersionEvidence evidence)
    {
        bool parameterized = IsMeaningfullyParameterized(tool.Definition);

        NIRADynamicToolExecutionHistoryRecord[] successfulFingerprinted =
            evidence.History
                .Where(value =>
                    value.Succeeded &&
                    !string.IsNullOrWhiteSpace(value.InvocationFingerprint))
                .ToArray();

        int distinctContexts = successfulFingerprinted
            .Select(value => value.InvocationFingerprint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        double reliability = evidence.ExecutionCount <= 0
            ? 0.0
            : Math.Clamp((double)evidence.SuccessCount / evidence.ExecutionCount, 0.0, 1.0);

        return new GeneralizationEvidence(
            parameterized,
            successfulFingerprinted.Length,
            distinctContexts,
            evidence.SuccessCount,
            reliability);
    }

    private static bool IsMeaningfullyParameterized(
        NIRADynamicToolDefinition definition)
    {
        NIRADynamicToolDefinition tool = definition.Normalize();
        if (tool.Parameters.Count == 0)
            return false;

        HashSet<string> parameterNames = tool.Parameters
            .Select(value => value.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (NIRADynamicToolStep step in tool.Steps)
        {
            if (step.Condition.Kind == NIRADynamicConditionKind.ParameterEquals &&
                !string.IsNullOrWhiteSpace(step.Condition.ParameterName) &&
                parameterNames.Contains(step.Condition.ParameterName))
                return true;

            foreach (NIRADynamicToolValueBinding binding in step.Arguments.Values)
            {
                if (binding.Source == NIRADynamicValueSource.Parameter &&
                    !string.IsNullOrWhiteSpace(binding.ParameterName) &&
                    parameterNames.Contains(binding.ParameterName))
                    return true;

                if (binding.Source == NIRADynamicValueSource.Template &&
                    !string.IsNullOrWhiteSpace(binding.Template) &&
                    binding.Template.Contains("${param:", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool HasGeneralizationEvidence(GeneralizationEvidence evidence) =>
        evidence.ParameterizedProcedure &&
        evidence.TotalSuccessCount >= MinimumSuccessfulExecutionsForGeneralization &&
        evidence.FingerprintedSuccessCount >= MinimumDistinctSuccessfulContextsForGeneralization &&
        evidence.DistinctSuccessfulContexts >= MinimumDistinctSuccessfulContextsForGeneralization &&
        evidence.Reliability >= MinimumVersionReliabilityForGeneralization;

    private static string ResolveGeneralizationState(
        NIRALearnedSkillRecord? existing,
        GeneralizationEvidence evidence)
    {
        if (!evidence.ParameterizedProcedure)
            return "NOT_PARAMETERIZED";

        bool eligible = HasGeneralizationEvidence(evidence);

        if (existing?.Definition.Scope == NIRALearnedSkillScope.Generalized)
            return eligible ? "GENERALIZED_CURRENT" : "GENERALIZED_EVIDENCE_STALE";

        return eligible ? "ELIGIBLE" : "WAITING_FOR_CROSS_CONTEXT_EVIDENCE";
    }

    private static string DescribeInsufficientGeneralizationEvidence(
        GeneralizationEvidence evidence)
    {
        return
            "Learned-skill generalization is not yet grounded by cross-context execution evidence. " +
            $"ParameterizedProcedure={evidence.ParameterizedProcedure}; " +
            $"Successes={evidence.TotalSuccessCount}/{MinimumSuccessfulExecutionsForGeneralization}; " +
            $"FingerprintBackedSuccesses={evidence.FingerprintedSuccessCount}; " +
            $"DistinctSuccessfulContexts={evidence.DistinctSuccessfulContexts}/{MinimumDistinctSuccessfulContextsForGeneralization}; " +
            $"Reliability={evidence.Reliability:0.00}/{MinimumVersionReliabilityForGeneralization:0.00}.";
    }

    private static NIRALearnedSkillCandidateState ResolveCandidateState(
        NIRADynamicToolRecord tool,
        NIRALearnedSkillRecord? existing,
        SourceVersionEvidence currentEvidence)
    {
        if (tool.Definition.Status != NIRADynamicToolStatus.Active)
            return NIRALearnedSkillCandidateState.SourceInactive;

        if (existing == null)
        {
            return HasLearningEvidence(currentEvidence)
                ? NIRALearnedSkillCandidateState.CreateEligible
                : NIRALearnedSkillCandidateState.WaitingForEvidence;
        }

        if (existing.Definition.SourceToolVersion != tool.Definition.Version)
        {
            return HasLearningEvidence(currentEvidence)
                ? NIRALearnedSkillCandidateState.RevisionEligible
                : NIRALearnedSkillCandidateState.StaleWaitingForEvidence;
        }

        bool freshFailure = currentEvidence.History.Any(value =>
            !value.Succeeded &&
            value.RecordedAtUtc > existing.Definition.LastSemanticReviewAtUtc);

        return freshFailure
            ? NIRALearnedSkillCandidateState.CurrentFailureReview
            : NIRALearnedSkillCandidateState.Current;
    }

    private static bool HasLearningEvidence(SourceVersionEvidence evidence) =>
        evidence.SuccessCount >= MinimumSuccessfulExecutionsForLearning &&
        evidence.ExecutionCount > 0 &&
        ((double)evidence.SuccessCount / evidence.ExecutionCount) >= MinimumVersionReliabilityForLearning;

    private static bool IsProven(SourceVersionEvidence evidence) =>
        evidence.SuccessCount >= 3 &&
        ComputeEvidenceConfidence(evidence) >= 0.75;

    private static double ComputeEvidenceConfidence(SourceVersionEvidence evidence)
    {
        if (evidence.ExecutionCount <= 0)
            return 0.0;

        double reliability =
            Math.Clamp(
                (double)evidence.SuccessCount / evidence.ExecutionCount,
                0.0,
                1.0);

        double coverage =
            Math.Clamp(
                evidence.SuccessCount / 5.0,
                0.0,
                1.0);

        return Math.Clamp(
            (0.45 * reliability) + (0.55 * coverage),
            0.0,
            1.0);
    }

    private static string FormatReliability(SourceVersionEvidence evidence) =>
        evidence.ExecutionCount <= 0
            ? "unproven"
            : ((double)evidence.SuccessCount / evidence.ExecutionCount).ToString("0.00");

    // =========================================================
    // EXACT PROCEDURE EQUIVALENCE
    // =========================================================

    private static string BuildProcedureFingerprint(
        NIRADynamicToolDefinition definition)
    {
        NIRADynamicToolDefinition tool = definition.Normalize();
        Dictionary<string, int> stepIndexes = tool.Steps
            .Select((step, index) => new { step.Id, Index = index })
            .ToDictionary(value => value.Id, value => value.Index, StringComparer.OrdinalIgnoreCase);

        StringBuilder text = new();
        text.Append("timeout=").Append(tool.TimeoutSeconds)
            .Append("|parallel=").Append(tool.AllowIndependentConcurrency ? '1' : '0');

        foreach (NIRADynamicToolParameterDefinition parameter in tool.Parameters
                     .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
        {
            text.Append("|p:")
                .Append(parameter.Name.ToLowerInvariant()).Append(':')
                .Append(parameter.Type.ToLowerInvariant()).Append(':')
                .Append(parameter.Required ? '1' : '0').Append(':')
                .Append(parameter.DefaultValue.HasValue
                    ? parameter.DefaultValue.Value.GetRawText()
                    : "null");
        }

        for (int index = 0; index < tool.Steps.Count; index++)
        {
            NIRADynamicToolStep step = tool.Steps[index];
            text.Append("|s:").Append(index).Append(':')
                .Append(step.CapabilityId.ToLowerInvariant()).Append(':')
                .Append(step.FailurePolicy);

            foreach (int dependency in step.DependsOnStepIds
                         .Select(value => MapStepIndex(stepIndexes, value))
                         .OrderBy(value => value))
            {
                text.Append(":d").Append(dependency);
            }

            text.Append(":c=").Append(step.Condition.Kind)
                .Append(':').Append(step.Condition.ParameterName?.ToLowerInvariant() ?? "-")
                .Append(':').Append(MapStepIndex(stepIndexes, step.Condition.StepId))
                .Append(':').Append(step.Condition.ExpectedValue.HasValue
                    ? step.Condition.ExpectedValue.Value.GetRawText()
                    : "null");

            foreach ((string argumentName, NIRADynamicToolValueBinding binding) in step.Arguments
                         .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase))
            {
                text.Append(":a=").Append(argumentName.ToLowerInvariant())
                    .Append(':').Append(binding.Source)
                    .Append(':').Append(binding.Literal.HasValue ? binding.Literal.Value.GetRawText() : "null")
                    .Append(':').Append(binding.Template ?? "-")
                    .Append(':').Append(binding.ParameterName?.ToLowerInvariant() ?? "-")
                    .Append(':').Append(MapStepIndex(stepIndexes, binding.StepId));
            }
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash);
    }

    private static int MapStepIndex(
        IReadOnlyDictionary<string, int> stepIndexes,
        string? stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            return -1;

        return stepIndexes.TryGetValue(stepId.Trim(), out int index)
            ? index
            : -2;
    }

    private static bool HasSemanticRevision(
        NIRALearnedSkillDefinition before,
        NIRALearnedSkillDefinition after)
    {
        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal) ||
            !string.Equals(before.Description, after.Description, StringComparison.Ordinal) ||
            before.Scope != after.Scope ||
            before.Status != after.Status ||
            before.SourceToolId != after.SourceToolId ||
            before.SourceToolVersion != after.SourceToolVersion ||
            Math.Abs(before.Confidence - after.Confidence) > 0.001)
        {
            return true;
        }

        return !SequenceEqual(before.Preconditions, after.Preconditions) ||
               !SequenceEqual(before.ExpectedOutcomes, after.ExpectedOutcomes) ||
               !SequenceEqual(before.VerificationMethods, after.VerificationMethods) ||
               !SequenceEqual(before.KnownFailurePatterns, after.KnownFailurePatterns) ||
               !FailureEvidenceEqual(before.FailurePatternEvidence, after.FailurePatternEvidence);
    }

    private static bool SequenceEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);

    private static bool FailureEvidenceEqual(
        IReadOnlyList<NIRALearnedSkillFailurePatternEvidence> left,
        IReadOnlyList<NIRALearnedSkillFailurePatternEvidence> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int index = 0; index < left.Count; index++)
        {
            NIRALearnedSkillFailurePatternEvidence a = left[index];
            NIRALearnedSkillFailurePatternEvidence b = right[index];

            if (!string.Equals(a.Pattern, b.Pattern, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(a.EvidenceQuote, b.EvidenceQuote, StringComparison.OrdinalIgnoreCase) ||
                a.SourceToolVersion != b.SourceToolVersion)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsEvidenceQuote(
        string source,
        string quote)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(quote))
        {
            return false;
        }

        return source.Contains(
            quote.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGuid(string? value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;

    private static NIRALearnedSkillMutationResult Rejected(string reason) =>
        new()
        {
            Action = NIRALearnedSkillApplyAction.Rejected,
            Reason = reason
        };

    private static void AppendList(
        StringBuilder text,
        string label,
        IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return;

        text.AppendLine($"  {label}:");
        foreach (string value in values)
            text.AppendLine($"    - {Bound(value, 700)}");
    }

    private static string Bound(string value, int max)
    {
        string clean = value?.Trim() ?? string.Empty;
        return clean.Length <= max ? clean : clean[..max];
    }

    private sealed record GeneralizationEvidence(
        bool ParameterizedProcedure,
        int FingerprintedSuccessCount,
        int DistinctSuccessfulContexts,
        int TotalSuccessCount,
        double Reliability);

    private sealed record SourceVersionEvidence(
        int ToolVersion,
        int ExecutionCount,
        int SuccessCount,
        int FailureCount,
        IReadOnlyList<NIRADynamicToolExecutionHistoryRecord> History);

    private sealed record GroundedFailurePatterns(
        IReadOnlyList<string> PatternNames,
        IReadOnlyList<NIRALearnedSkillFailurePatternEvidence> Evidence);
}

