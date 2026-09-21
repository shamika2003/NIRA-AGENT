/*
 * filename: NIRAMemoryFormationService.cs
 */

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using NIRAAgent.AI.Ollama;
using NIRAAgent.Self.Model;
using NIRAAgent.Skills;

namespace NIRAAgent.Memory.LongTerm;


// =============================================================
// UNIFIED POST-EXPERIENCE FORMATION
//
// One reasoning pass reviews a completed experience and may
// propose four different kinds of state work:
//
// 1. durable long-term memories
// 2. bounded NIRA self-preference observations
// 3. commitment lifecycle proposals
// 4. learned procedural-skill proposals grounded in repeated dynamic-tool evidence
//
// None of those proposals own persistence. Their authoritative
// C# subsystems validate and commit them separately.
// =============================================================

public sealed class NIRAMemoryFormationService
{
    private const string MainReasoningModel =
        "gpt-oss:120b-cloud";


    private const int MaximumMemoryProposals =
        3;


    private const int MaximumSelfPreferenceObservations =
        2;


    private const int MaximumCommitmentProposals =
        3;


    private const int MaximumLearnedSkillProposals =
        2;


    private const int MaximumContentLength =
        1200;


    private const int MaximumKeyLength =
        160;


    private const int MaximumSubjectLength =
        240;


    private const int MaximumEvidenceSummaryLength =
        500;


    private readonly OllamaClient
        _ollama;


    private readonly string
        _personality;


    private readonly string
        _formationPrompt;


    private readonly JsonSerializerOptions
        _jsonOptions;


    public NIRAMemoryFormationService(
        OllamaClient ollama)
    {
        _ollama =
            ollama
            ?? throw new ArgumentNullException(
                nameof(ollama));


        _personality =
            LoadPromptFile(
                "nira_personality.yaml");


        _formationPrompt =
            LoadPromptFile(
                "memory_formation.yaml");


        _jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive =
                    true
            };


        _jsonOptions.Converters.Add(
            new JsonStringEnumConverter());
    }


    public async Task<NIRAMemoryFormationResult> FormAsync(
        NIRAMemoryFormationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            context);


        Stopwatch stopwatch =
            Stopwatch.StartNew();


        string raw =
            await _ollama.ChatAsync(
                MainReasoningModel,
                BuildSystemPrompt(),
                BuildUserPrompt(
                    context),
                cancellationToken);


        stopwatch.Stop();


        NIRAMemoryFormationResult result =
            ParseResult(
                raw);


        Debug.WriteLine(
            $"[MemoryFormation] REVIEW | " +
            $"Run={context.RunId} | " +
            $"Time={stopwatch.ElapsedMilliseconds} ms | " +
            $"Memories={result.Proposals.Count} | " +
            $"SelfPreferenceObservations={result.SelfPreferenceObservations.Count} | " +
            $"CommitmentProposals={result.CommitmentProposals.Count} | " +
            $"LearnedSkillProposals={result.LearnedSkillProposals.Count} | " +
            $"Summary='{TrimLog(result.Summary)}'");


        return result;
    }


    private string BuildSystemPrompt()
    {
        return $$"""
            You are NIRA's dedicated post-experience formation
            reasoner.

            NIRA is the whole persistent agent. You are one
            reasoning resource inside that system.

            Do not answer the user.

            Examine one completed cognitive experience and decide
            whether it supports:

            1. durable long-term memory,
            2. one or more observations about NIRA's own developing
               learned preferences, and/or
            3. a grounded NIRA commitment lifecycle change, and/or
            4. durable procedural learning from repeated, authoritative
               dynamic-tool execution evidence.

            ==================================================
            NIRA IDENTITY
            ==================================================

            {{_personality}}

            ==================================================
            LEGACY IMPLEMENTATION TOKENS
            ==================================================

            The persistent agent's current identity is NIRA. Exact schema/enum tokens
            such as NIRAInference, NIRAReply and NIRALearnedPreference are legacy
            implementation names and must still be returned exactly where the contract
            requires them. Do not interpret those token names as a different agent.

            ==================================================
            FORMATION POLICY
            ==================================================

            {{_formationPrompt}}

            ==================================================
            AUTHORITY BOUNDARY
            ==================================================

            You only propose semantic interpretation.

            The application validates evidence identity, event
            provenance, memory kind, self-preference observation
            strength, commitment IDs, commitment evidence quotes,
            learned-skill source tool IDs/versions, source execution
            counts/reliability, verification metadata, allowed lifecycle
            transitions, duplicate application and persistence.

            A selfPreferenceObservation is NOT itself a durable
            NIRA preference. It is one piece of experience evidence.
            The authoritative C# self-preference service decides
            whether repeated evidence eventually becomes part of
            NIRA's durable developed self.

            A commitmentProposal is NOT itself a committed state
            change. The authoritative NIRA self-model service must
            ground it against fresh user/reply evidence and current
            commitment state before anything is stored.

            Never claim a self-preference is established merely
            because you proposed an observation.

            Never claim a commitment exists, completed, cancelled,
            waited, resumed, or became blocked merely because you
            proposed a lifecycle change.

            A learnedSkillProposal is NOT permission to execute anything.
            It is a semantic proposal that NIRA has learned a reusable
            procedure from repeated source-tool evidence. The authoritative
            learned-skill service verifies the exact persistent tool, its
            current version, execution history and minimum evidence before
            storing or revising a skill.

            ==================================================
            OUTPUT
            ==================================================

            Return one JSON object only.
            No Markdown fences.
            No prose outside JSON.

            {
              "summary": "short operational formation summary",
              "proposals": [
                {
                  "evidenceBasis": "UserExplicit|NIRAInference|SharedExperience|SystemDerived",
                  "kind": "UserFact|UserPreference|ProjectKnowledge|SharedExperience|ImportantEvent",
                  "content": "concise durable proposition",
                  "canonicalKey": null,
                  "topicKey": null,
                  "importance": 0.0,
                  "confidence": 0.0,
                  "emotionalWeight": 0.0,
                  "association": {
                    "retrievalDescription": null,
                    "retrievalCues": [],
                    "concepts": [],
                    "entities": []
                  }
                }
              ],
              "selfPreferenceObservations": [
                {
                  "evidenceBasis": "NIRAInference",
                  "key": "stable.lowercase.subject.key",
                  "subject": "human-readable subject of NIRA's possible preference",
                  "topicKey": null,
                  "affinity": 0.0,
                  "evidenceStrength": 0.0,
                  "confidence": 0.0,
                  "evidenceSummary": "short explanation of what fresh experience supports this observation"
                }
              ],
              "commitmentProposals": [
                {
                  "action": "Create|SetPending|SetWaiting|SetBlocked|Complete|Cancel|Reschedule|CancelAllActive",
                  "commitmentId": null,
                  "summary": "concise obligation summary; required for Create",
                  "temporal": null,
                  "evidenceSource": "UserEvent|NIRAReply",
                  "evidenceQuote": "short exact quote copied from that source",
                  "reason": "short semantic reason",
                  "confidence": 0.0
                }
              ],
              "learnedSkillProposals": [
                {
                  "action": "Create|Revise|Deprecate|Retire",
                  "skillId": null,
                  "name": "stable human-readable procedure name",
                  "description": "what reusable procedure NIRA has learned",
                  "scope": "ProjectSpecific|Generalized",
                  "sourceToolId": "exact persistent dynamic-tool GUID",
                  "sourceToolVersion": 1,
                  "preconditions": ["conditions that should be true before reuse"],
                  "expectedOutcomes": ["authoritative outcomes the procedure should produce"],
                  "verificationMethods": ["how NIRA should verify the outcome"],
                  "failurePatterns": [
                    {
                      "pattern": "bounded failure pattern learned from evidence",
                      "evidenceQuote": "short exact quote copied from RecentFailureEvidence",
                      "sourceToolVersion": 1
                    }
                  ],
                  "reason": "why current repeated evidence warrants this skill state",
                  "confidence": 0.0
                }
              ]
            }

            When commitmentProposals[].temporal is not null, it must be:

            {
              "mode": "Exact|FlexibleDay|Window",
              "originalExpression": "short exact/natural time expression from the fresh evidence",
              "timeZoneId": null,
              "targetLocalDate": "yyyy-MM-dd or null",
              "localTime": "HH:mm:ss or null",
              "windowStartLocalTime": "HH:mm:ss or null",
              "windowEndLocalTime": "HH:mm:ss or null",
              "relativeDelaySeconds": null,
              "relativeDays": null,
              "absoluteUtc": null,
              "recurrenceKind": "None|Daily|Weekly|Monthly|Yearly",
              "recurrenceInterval": 1,
              "recurrenceDaysOfWeek": []
            }

            Use temporal only for an actual time/date-bound obligation or a
            Reschedule action. The runtime owns final timezone conversion,
            persistence, wake claims and recurrence advancement.

            proposals is always an array.
            selfPreferenceObservations is always an array.
            commitmentProposals is always an array.
            learnedSkillProposals is always an array.

            Return [] for any collection when nothing qualifies.

            Maximum three durable memory proposals.
            Maximum two self-preference observations.
            Maximum three commitment proposals.
            Maximum two learned-skill proposals.
            """;
    }


    private static string BuildUserPrompt(
        NIRAMemoryFormationContext context)
    {
        return $"""
            RUN
            {context.RunId}

            ==================================================
            AUTHORITATIVE FRESH EVIDENCE
            ==================================================

            This is the fresh experience being reviewed.

            Source: {context.Event.Source}
            Name: {context.Event.Name}
            Topic: {context.Event.TopicKey}
            Timestamp: {context.Event.Timestamp:O}

            {context.Event.Content}

            ==================================================
            AUTHORITATIVE CURRENT NIRA STATE
            ==================================================

            CURRENT CHARACTER / RELATIONSHIP
            ----------------------------------

            {Normalize(
                context.CharacterContext,
                "No character context.")}

            CURRENT DEVELOPED SELF-PREFERENCES
            ----------------------------------

            {Normalize(
                context.SelfPreferenceContext,
                "No current developed self-preference context.")}

            CURRENT SELF-MODEL / COMMITMENTS
            ----------------------------------

            {Normalize(
                context.SelfModelContext,
                "No current self-model/commitment context.")}

            CURRENT AUTHORITATIVE CLOCK / TEMPORAL CONTEXT
            ----------------------------------

            {Normalize(
                context.TemporalContext,
                "No authoritative temporal context.")}

            CURRENT DYNAMIC TOOLS / LEARNED PROCEDURAL SKILLS
            ----------------------------------

            {Normalize(
                context.DynamicToolContext,
                "No current dynamic-tool or learned-skill context.")}

            AUTHORITATIVE DYNAMIC-TOOL EVIDENCE FROM THIS RUN
            ----------------------------------

            {Normalize(
                context.DynamicToolEvidence,
                "No dynamic tool was created, revised or executed in this run.")}

            ==================================================
            NIRA'S FINAL REPLY
            ==================================================

            This is authoritative evidence ONLY of what NIRA
            actually said in this completed interaction.

            It may ground creation of a commitment when NIRA
            explicitly accepted a future unresolved obligation.

            It is NOT authoritative evidence that an external PC
            action succeeded, that a remembered fact is true, or
            that a real-world task was completed merely because
            NIRA said it was.

            {Normalize(
                context.FinalReply,
                "NIRA emitted no visible reply in this run.")}

            ==================================================
            INTERPRETIVE CONTEXT — NOT FRESH MEMORY EVIDENCE
            ==================================================

            RECENT CONVERSATION
            -------------------

            {Normalize(
                context.ConversationContext,
                "No recent conversation.")}

            EXISTING LONG-TERM MEMORY
            -------------------------

            {Normalize(
                context.LongTermMemoryContext,
                "No long-term memory context.")}

            PREVIOUS MEMORY SEARCH RESULTS
            ------------------------------

            {Normalize(
                context.MemorySearchEvidence,
                "No extra memory search evidence.")}

            ==================================================
            FORMATION RULE
            ==================================================

            Durable memory proposals must still be supported by
            the AUTHORITATIVE FRESH EVIDENCE event. Do not use the
            final reply as proof for durable factual memory.

            Self-preference observations are bounded evidence
            observations. They may be proposed when the fresh
            experience, interpreted with NIRA's current
            authoritative state, genuinely suggests that NIRA is
            developing a liking, dislike, preference or aversion.

            Commitment creation is different. A Create proposal
            requires NIRA's FINAL REPLY to contain a clear accepted
            future obligation that remains unresolved after this
            interaction. evidenceSource must be NIRAReply and
            evidenceQuote must be a short exact quote from that
            final reply.

            Commitment lifecycle transitions normally target an exact
            commitmentId already listed in CURRENT SELF-MODEL /
            COMMITMENTS. Ground each transition in a short exact
            quote from either the fresh UserEvent or NIRAReply.

            TEMPORAL COMMITMENTS:
            - When NIRA accepts a future obligation whose timing matters, attach
              one temporal object to the Create proposal.
            - Use the CURRENT AUTHORITATIVE CLOCK and fresh event timestamp to
              interpret relative language.
            - For "in N minutes/hours", prefer Exact + relativeDelaySeconds.
            - For a named exact date/time, use Exact + targetLocalDate + localTime.
            - For date-only language such as "tomorrow" or "next Monday", use
              FlexibleDay. Do NOT invent an exact clock time. relativeDays may be
              used for expressions such as tomorrow/in three days.
            - For explicit dayparts or ranges such as "tomorrow morning" or
              "between 2 and 4", use Window with target date and local start/end.
            - timeZoneId should normally be null so the runtime uses the PC's
              authoritative local timezone. Set it only when the fresh evidence
              clearly names another timezone.
            - recurrenceKind None means one-time. Use Daily/Weekly/Monthly/Yearly
              only when the obligation itself is explicitly recurring.
            - Reschedule changes the schedule of an existing active commitment;
              commitmentId must be the exact existing ID and temporal is required.
            - A user saying "actually remind me in 20 minutes" after an existing
              reminder should normally Reschedule that existing commitment rather
              than creating a duplicate obligation.
            - A one-time TemporalCommitmentDue event may be completed from
              NIRAReply only when NIRA actually delivered the promised reminder or
              conversational act in that final reply.
            - A recurring commitment MUST NOT be completed merely because one
              occurrence was delivered. It remains active until explicitly ended.
            - Never create a second durable-memory record just to represent a
              scheduled reminder; the commitment + temporal schedule is the
              authoritative representation.

            There is one semantic bulk transition: CancelAllActive.
            When the fresh UserEvent asks to clear/remove/cancel/drop
            ALL current commitments as a set, you MUST use
            CancelAllActive instead of enumerating individual IDs.
            Do not leave Waiting or Blocked commitments active simply
            because they are not Pending. For CancelAllActive:
            - evidenceSource must be UserEvent,
            - evidenceQuote must be a short exact quote from the fresh
              user event proving the all-commitments request,
            - commitmentId must be null,
            - summary may be empty,
            - emit ONE CancelAllActive proposal rather than enumerating
              individual Pending/Waiting/Blocked commitments.
            The authoritative runtime will cancel every active commitment
            atomically under the self-model mutation lock.

            Do not mark a real-world commitment Completed merely
            because NIRA's reply claims success. Completion of a
            consequential external task needs authoritative action
            or system evidence in a future event. Conversational
            commitments may complete from conversation evidence
            when the promised conversational act actually occurred.

            Do NOT create a commitment merely because NIRA says:
            - "let me know",
            - "I can help",
            - "I'll explain" and then immediately explains it,
            - "I'll try" without accepting a concrete obligation,
            - a generic offer of future availability,
            - a plan or possibility that NIRA did not actually
              accept as an obligation.

            A commitment is not an executable goal. Do not create
            goals here.

            LEARNED PROCEDURAL SKILLS:
            - A learned skill is durable knowledge that a reusable procedure has
              earned reuse through repeated authoritative execution evidence.
            - It is NOT a transcript, ordinary project-memory sentence, user
              preference, commitment, goal, or executable tool itself.
            - The PROCEDURAL LEARNING CANDIDATES section is an authoritative C#
              evidence gate. Do not infer eligibility from prose when CandidateState
              already states the runtime result.
            - Create only when CandidateState=CreateEligible for that exact persistent
              source tool. sourceToolId and sourceToolVersion must be copied exactly.
            - If a skill already exists for that source tool, never Create a parallel
              skill. When CandidateState=RevisionEligible, Revise the exact ExistingSkill
              GUID to the current source-tool version.
            - StaleWaitingForEvidence means the source tool changed but the new version
              has not yet earned enough successful evidence. Do not reconcile early.
            - CurrentFailureReview means a new failed execution is available for semantic
              review. A failure does not automatically mean the procedure definition is bad.
            - If CurrentFailureReview is semantically reviewed and no durable pattern or
              procedure change is warranted, a Revise proposal for that exact existing skill
              may carry no new semantic fields; the runtime records the review without
              incrementing the skill version.
            - New failure knowledge must use failurePatterns[]. Each evidenceQuote must
              be copied exactly from a RecentFailureEvidence line for the cited
              sourceToolVersion. The runtime rejects invented or paraphrased evidence.
            - Do not emit new knownFailurePatterns strings. Stage 13.2 stores new failure
              patterns only when their structured evidence quote is runtime-grounded.
            - expectedOutcomes and verificationMethods must describe observable,
              testable results; do not call a procedure learned merely because its
              steps sound reasonable.
            - ProjectSpecific is the default when the procedure depends on this
              project, repository, paths, tools, conventions, or environment.
              Generalized requires evidence that the procedure is portable beyond
              one project; do not generalize from one project by imagination.
            - Deprecate when the procedure remains historical knowledge but should
              not be preferred for new work. Retire only when it is obsolete enough
              that NIRA should no longer consider it an active procedural option.
            """;
    }


    private NIRAMemoryFormationResult ParseResult(
        string raw)
    {
        string json =
            StripCodeFence(
                raw);


        NIRAMemoryFormationResult? parsed =
            JsonSerializer.Deserialize<NIRAMemoryFormationResult>(
                json,
                _jsonOptions);


        if (parsed ==
            null)
        {
            return new NIRAMemoryFormationResult();
        }


        List<NIRAMemoryFormationProposal> memoryProposals =
            new();


        foreach (
            NIRAMemoryFormationProposal rawProposal
            in parsed.Proposals
                ?? Array.Empty<NIRAMemoryFormationProposal>())
        {
            if (memoryProposals.Count >=
                MaximumMemoryProposals)
            {
                break;
            }


            try
            {
                NIRAMemoryFormationProposal proposal =
                    rawProposal.Normalize();


                if (proposal.Kind ==
                    NIRAMemoryKind.NIRALearnedPreference)
                {
                    Debug.WriteLine(
                        "[MemoryFormation] REJECTED DIRECT SELF-PREFERENCE MEMORY | " +
                        "Reason='Use selfPreferenceObservations; established state is synchronized later.'");


                    continue;
                }


                if (proposal.Content.Length >
                    MaximumContentLength)
                {
                    continue;
                }


                if (
                    proposal.CanonicalKey?.Length >
                        MaximumKeyLength
                    ||
                    proposal.TopicKey?.Length >
                        MaximumKeyLength)
                {
                    continue;
                }


                memoryProposals.Add(
                    proposal);
            }
            catch
            {
            }
        }


        List<NIRASelfPreferenceFormationProposal> preferenceObservations =
            new();


        foreach (
            NIRASelfPreferenceFormationProposal rawObservation
            in parsed.SelfPreferenceObservations
                ?? Array.Empty<NIRASelfPreferenceFormationProposal>())
        {
            if (preferenceObservations.Count >=
                MaximumSelfPreferenceObservations)
            {
                break;
            }


            try
            {
                NIRASelfPreferenceFormationProposal observation =
                    rawObservation.Normalize();


                if (observation.EvidenceBasis !=
                    NIRAMemoryEvidenceBasis.NIRAInference)
                {
                    continue;
                }


                if (
                    observation.Key.Length >
                        MaximumKeyLength
                    ||
                    observation.TopicKey?.Length >
                        MaximumKeyLength
                    ||
                    observation.Subject.Length >
                        MaximumSubjectLength
                    ||
                    observation.EvidenceSummary?.Length >
                        MaximumEvidenceSummaryLength)
                {
                    continue;
                }


                preferenceObservations.Add(
                    observation);
            }
            catch
            {
            }
        }


        List<NIRACommitmentFormationProposal> commitmentProposals =
            new();


        foreach (
            NIRACommitmentFormationProposal rawProposal
            in parsed.CommitmentProposals
                ?? Array.Empty<NIRACommitmentFormationProposal>())
        {
            if (commitmentProposals.Count >=
                MaximumCommitmentProposals)
            {
                break;
            }


            try
            {
                NIRACommitmentFormationProposal proposal =
                    rawProposal.Normalize();


                if (string.IsNullOrWhiteSpace(
                        proposal.EvidenceQuote))
                {
                    continue;
                }


                if (
                    proposal.Action ==
                        NIRACommitmentProposalAction.Create
                    &&
                    string.IsNullOrWhiteSpace(
                        proposal.Summary))
                {
                    continue;
                }


                if (
                    proposal.Action ==
                        NIRACommitmentProposalAction.Reschedule
                    &&
                    (proposal.Temporal == null ||
                     proposal.Temporal.Mode == NIRAAgent.Temporal.NIRATemporalTimingMode.None))
                {
                    continue;
                }


                commitmentProposals.Add(
                    proposal);
            }
            catch
            {
            }
        }


        List<NIRALearnedSkillFormationProposal> learnedSkillProposals =
            new();


        foreach (
            NIRALearnedSkillFormationProposal rawProposal
            in parsed.LearnedSkillProposals
                ?? Array.Empty<NIRALearnedSkillFormationProposal>())
        {
            if (learnedSkillProposals.Count >=
                MaximumLearnedSkillProposals)
            {
                break;
            }

            try
            {
                NIRALearnedSkillFormationProposal proposal =
                    rawProposal.Normalize();

                if (proposal.Confidence < 0.50)
                {
                    continue;
                }

                learnedSkillProposals.Add(
                    proposal);
            }
            catch
            {
            }
        }


        return parsed with
        {
            Summary =
                string.Join(
                    ' ',
                    (parsed.Summary ?? string.Empty)
                        .Split(
                            (char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries))
                    .Trim(),

            Proposals =
                memoryProposals,

            SelfPreferenceObservations =
                preferenceObservations,

            CommitmentProposals =
                commitmentProposals,

            LearnedSkillProposals =
                learnedSkillProposals
        };
    }


    private static string StripCodeFence(
        string raw)
    {
        string value =
            raw?.Trim()
            ?? string.Empty;


        if (!value.StartsWith(
                "```",
                StringComparison.Ordinal))
        {
            return value;
        }


        int firstLineBreak =
            value.IndexOf(
                '\n');


        if (firstLineBreak >=
            0)
        {
            value =
                value[(firstLineBreak + 1)..];
        }


        int closing =
            value.LastIndexOf(
                "```",
                StringComparison.Ordinal);


        if (closing >=
            0)
        {
            value =
                value[..closing];
        }


        return value.Trim();
    }


    private static string Normalize(
        string? value,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? fallback
            : value.Trim();
    }


    private static string TrimLog(
        string value)
    {
        const int maximumLength =
            140;


        string clean =
            value?.Trim()
            ?? string.Empty;


        return clean.Length <=
            maximumLength
            ? clean
            : clean[..maximumLength] +
                "...";
    }


    private static string LoadPromptFile(
        string fileName)
    {
        string path =
            Path.Combine(
                AppContext.BaseDirectory,
                "Prompt",
                fileName);


        if (!File.Exists(
                path))
        {
            throw new FileNotFoundException(
                $"Required NIRA prompt file was not found: {path}",
                path);
        }


        string content =
            File.ReadAllText(
                path);


        if (string.IsNullOrWhiteSpace(
                content))
        {
            throw new InvalidOperationException(
                $"Required NIRA prompt file is empty: {path}");
        }


        return content.Trim();
    }
}

