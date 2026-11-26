using System.Text.Json;
using Nira.Core;
using Nira.Infrastructure;

var cases = new (string Name, Func<Task> Test)[] {
    ("m9a12-unbound-plan-preserves-discovery-routing", M9UnboundPlanRoutingRepair),
    ("m9a12-read-only-alias-inactive-and-denial", M9ReadOnlyAliasRoutingRepair),
    ("m9a12-initial-related-research-routing", M9InitialRelatedResearchRepair),
    ("m9a11-image-empty-alternative-preserves-evidence", M9ImageEmptyAlternative),
    ("m9a11-browser-text-only-is-opt-in", M9BrowserTextOnly),
    ("m9a11-lossless-wire-tool-json", M9CompactToolWire),
    ("m9a10-complete-recent-followup-context", M9CompleteFollowupContext),
    ("m9a10-image-candidate-binding-and-retry-avoidance", M9ImageCandidateBinding),
    ("m9a10-image-candidate-routing-and-approval", M9ImageCandidateRouting),
    ("m9a9-document-aliases-verified-and-approved", M9DocumentCompatibility),
    ("m9a9-document-invalid-aliases-and-denial", M9DocumentCompatibilitySafety),
    ("m9a9-cross-family-research-image-routing", M9RelatedResearchRouting),
    ("m9a8-implicit-numeric-label-and-bare-expectation-verified-capture", M9ImplicitNumericSequenceAndCapture),
    ("m9a8-implicit-expectation-literal-and-wrong-value-safety", M9ImplicitNumericSafety),
    ("m9a7-typed-numeric-shorthand-verified-capture", M9TypedNumericSequenceAndCapture),
    ("m9a7-legacy-numeric-expectation-preflight", M9LegacyNumericPreflight),
    ("m9a7-typed-numeric-and-selector-safety", M9TypedNumericSafety),
    ("m9a6-observed-name-sequence-binds-canonical-approval", M9EfficiencyNameBinding),
    ("m9a6-ambiguous-selector-rejected-before-dispatch", M9EfficiencyAmbiguousSelector),
    ("m9a6-editor-occupancy-opt-in", M9EfficiencyEditorOccupancy),
    ("m9a6-immediate-verified-input-no-fixed-character-delay", M9EfficiencyFastInput),
    ("m9a6-duplicate-read-is-not-progress", M9EfficiencyRepeatedRead),
    ("m9a6-wire-serialization-preserves-decoded-content", M9EfficiencyWire),
    ("m9a5-interrupted-task-new-message-isolation", M9InterruptedIsolation),
    ("m9a5-provider-interruption-checkpoint", M9ProviderInterruption),
    ("m9a5-verified-longer-editor-input", M9LongEditorInput),
    ("m9a5-observed-control-method-preflight", M9ObservedMethodPreflight),
    ("m9a4-compact-observation-keeps-exact-durable-evidence", M9StabilityCompact),
    ("m9a4-sequence-corrects-all-invalid-fields-without-dispatch", M9StabilitySequenceFeedback),
    ("m9a4-task-scoped-http-metrics", M9StabilityMetrics),
    ("m9a4-stream-usage-does-not-affect-visible-content", M9StabilityStreamUsage),
    ("m9-procedure-learn-persist-reuse-and-opt-out", M9LearnAndReuse),
    ("m9-procedure-reject-unverified-or-invented-evidence", M9RejectInvalidEvidence),
    ("m9-procedure-never-grants-authorization", M9SkillCannotAuthorize),
    ("m9-procedure-version-deduplication-and-deletion", M9Versioning),
    ("m8d3-memory-source-scope-denies-unrequested-investigation", M8d3ScopeDenied),
    ("m8d3-memory-source-scope-allows-explicit-investigation", M8d3ScopeAllowed),
    ("m8d3-memory-source-scope-invalid-decision-fails-closed", M8d3ScopeInvalid),
    ("m8d3-memory-evolution-preserves-version-and-recall", M8d3VersionedLearning),
    ("m8d3-memory-evolution-complementary-and-duplicate", M8d3ComplementaryLearning),
    ("m8d3-memory-evolution-protects-manual-and-rejected-changes", M8d3ProtectedLearning),
    ("m8d3-memory-evolution-rechecks-stale-snapshot", M8d3StaleLearning),
    ("m8d3-single-clarification-field-is-not-form", M8d3SingleField),
    ("m8d2-stream-real-delivery-before-final-response", M8d2StreamBeforeFinal),
    ("m8d2-stream-content-parts-empty-tool-array", M8d2StreamParts),
    ("m8d2-empty-clarification-fields-single-question", M8d2EmptyFields),
    ("m8d2-memory-associations-and-format-dedup", M8d2Associations),
    ("m8d2-memory-generic-alias-cannot-displace-subject", M8d2GenericAlias),
    ("m8d2-semantic-phrase-partial-domain-match", M8d2PartialDomain),
    ("m8d1-semantic-recall-query-only-and-fresh-task", M8d1SemanticRecall),
    ("m8d1-no-extra-inference-on-opt-out-empty-or-local-hit", M8d1RecallFastPath),
    ("m8d1-reject-invalid-expansion-and-unrelated-anchors", M8d1RecallIsolation),
    ("m8d1-recheck-deletion-and-consent-after-inference", M8d1RecallFreshness),
    ("m8d1-native-decision-is-not-an-executable-tool", M8d1NativeDecision),
    ("m8c3-automatic-learning-durable-and-conflict-safe", M8c3AutomaticLearning),
    ("m8c3-automatic-learning-consent-migration", M8c3AutomaticLearningMigration),
    ("m8c2-automatic-mutation-intent-veto", M8c2IntentVeto),
    ("m8c2-learning-native-decision-and-fresh-recall", M8c2LearningNative),
    ("m8c2-learning-skips-malformed-memory-attempt", M8c2LearningSkippedAttempt),
    ("m8c-learning-compares-approved-notes-only-with-recall-consent", M8cLearningContext),
    ("m8c-learning-format-dedup-and-acceptance-race", M8cLearningDedup),
    ("m8c-explicit-memory-actions-not-relearned", M8cLearningSkipsExplicitActions),
    ("m8c-contextual-aliases-and-generic-recall-isolation", M8cRecallAliases),
    ("m8b-new-note-direct-save-and-fresh-lookup", M8bDirectSaveAndRecall),
    ("m8b-development-memory-management-policy", M8bDevelopmentMemoryManagement),
    ("m8b-memory-related-note-guard", M8bRelatedNoteGuard),
    ("m8b-memory-correction-metadata-and-recall", M8bCorrectionPreservesMetadata),
    ("m8b-memory-topic-and-original-history", M8bTopicAndHistory),
    ("m8-memory-additive-migration", M8MemoryMigration),
    ("m8-memory-ranked-recall-dedup", M8MemoryRankAndDedup),
    ("m8-memory-context-opt-in-isolated", M8MemoryContextIsolation),
    ("m8-memory-correction-cas", M8MemoryCorrection),
    ("m8-memory-tools-evidence-approval", M8MemoryToolEvidence),
    ("m8-memory-learning-consent", M8MemoryLearningConsent),
    ("m8-memory-learning-rejects-untrusted", M8MemoryLearningRejectsUntrusted),
    ("m8-memory-history-scope", M8MemoryHistoryScope),
    ("m8-memory-learning-engine-integration", M8MemoryLearningIntegration),
    ("m8-memory-protected-metadata", M8MemoryProtectedMetadata),
    ("schema-rejects-unexpected-properties", SchemaRejectsUnexpectedProperties),
    ("capture-observed-window-saves-verified-artifact", WindowCaptureApproved),
    ("capture-denial-does-not-read-pixels", WindowCaptureDenied),
    ("capture-unobserved-target-rejected", WindowCaptureUnobserved),
    ("capture-title-mismatch-rejected", WindowCaptureMismatch),
    ("capture-stale-window-no-file", WindowCaptureStale),
    ("capture-protects-vision-transfer", WindowCaptureVisionPolicy),
    ("schema-enum-feedback-lists-valid-values", SchemaEnumFeedback),
    ("capture-chat-media-only-from-verified-receipt", CaptureChatEvidence),
    ("schema-property-feedback-lists-real-fields", SchemaPropertyFeedback),
    ("catalog-stable-with-optional-providers", CatalogStableWithOptionalProviders),
    ("catalog-configurable-priority-and-family", CatalogConfigurablePriorityAndFamily),
    ("generic-recovery-requires-new-observation", GenericRecoveryFreshObservation),
    ("generic-recovery-bounds-equivalent-rejected-actions", GenericRecoveryBoundsReplay),
    ("generic-recovery-preserves-uncertain-effects", GenericRecoveryUnverified),
    ("generic-recovery-alternative-denial-is-respected", GenericRecoveryDenial),
    ("m7c-ordered-controls-verified", OrderedControlsVerified),
    ("m7c-ordered-controls-denied", OrderedControlsDenied),
    ("m7c-ordered-controls-require-observation", OrderedControlsUnobserved),
    ("m7c-ordered-controls-stop-on-stale", OrderedControlsStale),
    ("m7c-ordered-controls-mismatch-blocks-retry", OrderedControlsMismatch),
    ("m9a3-ordered-actions-no-extra-inference", OrderedControlsNoExtraInference),
    ("m9a-ordered-display-numeric-grouping", OrderedDisplayNumericGrouping),
    ("m9a3-bare-numeric-readback-and-completion", OrderedControlsBareNumericCompletion),
    ("m9a3-verified-sequence-then-capture", OrderedControlsThenCapture),
    ("m9a3-local-capability-ranking", M9LocalCapabilityRanking),
    ("m7c-paused-task-explicit-isolation", PausedTaskIsolation),
    ("m7c-nonempty-editor-safe-recovery", NonemptyEditorRecovery),
    ("m7c-verified-input-paced-readback", VerifiedInputPaced),
    ("m7c-verified-input-stops-on-corruption", VerifiedInputCorruption),
    ("m7c-verified-input-stops-on-focus-loss", VerifiedInputFocusLoss),
    ("m7c-verified-input-never-retypes-on-timeout", VerifiedInputTimeout),
    ("m7c-verified-input-unicode-scalar", VerifiedInputUnicode),
    ("m7c-unverified-input-followup-isolated", VerifiedInputFollowup),
    ("m7c-app-detection-positive-path-evidence", M7cDetectionStatus),
    ("m7c-application-launch-denied-without-dispatch", M7cLaunchDenied),
    ("m7c-application-launch-requires-current-observation", M7cLaunchUnobserved),
    ("m7c-application-launch-observed-approved", M7cLaunchApproved),
    ("m7c-application-launch-stale-file-rejected", M7cLaunchStale),
    ("m7c-application-discovery-bounded-schema", M7cDiscoverySchema),
    ("m7c-physical-core-count-observation", M7cPhysicalCores),
    ("m7b-process-inspection-direct-and-optional-filter", M7bProcessInspectionDirect),
    ("m7b-applications-multi-query-and-path-metadata", M7bApplicationsLookup),
    ("m7b-clarification-review-uses-capabilities", M7bClarificationReview),
    ("m7b-clarification-review-preserves-user-choice", M7bRealClarification),
    ("m7b-windows-product-family", M7bWindowsFamily),
    ("m7b-optional-system-inventory", M7bSystemInventory),
    ("m7b-search-domain-scope", M7bDomainSearch),
    ("m7b-browser-reference-recovery", M7bBrowserReferences),
    ("m7a-system-snapshot-readonly-and-bounded", M7aSystemSnapshot),
    ("m7a-system-snapshot-routed-and-authorized", M7aSystemRoute),
    ("m7a-search-bounded-and-filtered", M7aSearchResults),
    ("m7a-search-invalid-empty-and-oversized", M7aSearchFailures),
    ("m7a-search-receipt-reaches-model", M7aSearchRoute),
    ("m7a-response-quality-guidance", M7aQualityGuidance),
    ("m7a-still-image-redundant-timing", M7aStillTiming),
    ("clarification-resumes-original-objective", ClarificationResumes),
    ("authorized-workspace-write-has-observed-receipt", WorkspaceWrite),
    ("out-of-scope-write-cannot-run-without-consent", ExternalWriteDenied),
    ("dispatched-action-restart-requires-reconciliation", InterruptedWrite),
    ("existing-file-is-not-overwritten", NoOverwrite),
    ("sqlite-state-is-optimistically-versioned", OptimisticConcurrency),
    ("invalid-tool-arguments-do-not-touch-disk", InvalidArguments),
    ("multi-step-plan-is-persisted", PlanPersistence),
    ("unverified-action-claim-is-rejected", UnverifiedCompletion),
    ("user-approved-outside-write-is-audited", OutsideWriteApproved),
    ("dpapi-does-not-store-conversation-as-plaintext", ProtectedConversation),
    ("workspace-defaults-to-user-appdata", WorkspaceDefaultsToAppData),
    ("workspace-custom-location-preserves-existing-content", WorkspaceCustomLocation),
    ("workspace-blocked-location-explains-recovery", WorkspaceBlockedLocation),
    ("workspace-rejects-relative-override", WorkspaceRelativeOverride),
    ("ordinary-conversation-completes-without-finish-tool", OrdinaryConversation),
    ("m5b-factual-grounding-and-desktop-style", M5bGroundingAndStyle),
    ("m5b-unobserved-urls-flagged", M5bUnobservedUrls),
    ("m5b-structured-finish-source-notice", M5bStructuredSourceNotice),
    ("m5b-observed-urls-not-flagged", M5bObservedUrls),
    ("m5b1-example-with-real-citation", M5bExampleWithCitation),
    ("m5b1-non-envelope-url-still-flagged", M5bNonEnvelopeUrl),
    ("m5c-identity-and-research-policy", M5cIdentityResearchPolicy),
    ("m6f-dev-permissions-allow-media-protect-mutations", M6fDevelopmentPermissions),
    ("m6f-video-search-filters-and-binds-evidence", M6fVideoSearch),
    ("m6e3-animation-one-request-and-persistent", M6e3Animation),
    ("m6e3-animation-validation-and-denial", M6e3AnimationDenied),
    ("m6e-generated-artwork-approved-once-and-persistent", M6eGenerationSuccess),
    ("m6e-generation-denied-no-provider-call", M6eGenerationDenied),
    ("m6e-malformed-generation-not-saved", M6eGenerationInvalid),
    ("m6e-image-credentials-separated-and-protected", M6eGenerationCredentials),
    ("m6e2-generation-local-readiness-before-approval", M6e2Readiness),
    ("m6e2-selected-generation-recovers-false-refusal", M6e2RoutingRecovery),
    ("m6e2-selected-generation-repeated-refusal-pauses", M6e2RoutingPause),
    ("m6d-gallery-bound-persistent-receipts", M6dGalleryPersistence),
    ("m6d-gallery-upload-original-preserved-and-persistent", M6dGalleryUpload),
    ("m6a-fix-image-preview-binding", M6aFixPreviewBinding),
    ("m6a-fix-image-preview-unobserved-blocked", M6aFixUnobservedPreview),
    ("m6a-fix-image-preview-approval", M6aFixPreviewApproval),
    ("m6-rich3-bare-image-arguments-recover-to-native", M6Rich3BareImageRecovery),
    ("m6-rich3-bare-image-arguments-never-complete", M6Rich3BareImagePause),
    ("m6-rich3-ordinary-url-json-preserved", M6Rich3OrdinaryJson),
    ("m6a-commons-image-metadata", M6aCommonsDiscovery),
    ("m6a-tavily-image-metadata", M6aTavilyDiscovery),
    ("m6a-invalid-and-bounded-provider-data", M6aMalformedAndBounded),
    ("m6a-image-registry-and-network-approval", M6aApprovalAndRegistry),
    ("empty-model-reply-retries-without-polluting-history", EmptyReplyRecovery),
    ("empty-tool-mode-falls-back-to-tool-free-chat", ToolFreeRecovery),
    ("empty-model-replies-pause-without-fabricating", PersistentEmptyReply),
    ("unfinished-plan-does-not-implicitly-complete", PlainReplyWithPendingPlan),
    ("read-tool-followed-by-plain-answer-completes", ReadThenPlainAnswer),
    ("openai-compatible-plain-text-content", WirePlainText),
    ("openai-compatible-text-parts-content", WireContentParts),
    ("provider-tool-only-history-null-content", HttpToolOnlyAssistantNull),
    ("provider-original-tool-call-id-preserved", HttpProviderCallIdPreserved),
    ("provider-duplicate-tool-ids-refused", HttpProviderDuplicateCallIds),
    ("provider-400-context-classified-without-leak", Http400ContextSafe),
    ("m3b2-context-smaller-inference", ContextRecoveryShrinksContext),
    ("m3b2-context-preserves-user-and-system", ContextRecoveryKeepsUserAndPlan),
    ("m3b2-context-tool-call-pairing", ContextRecoveryPairsToolMessages),
    ("m3b2-context-preserves-receipt-ids", ContextRecoveryKeepsEvidenceMetadata),
    ("m3b2-context-inert-tool-data", ContextRecoveryNoDataInstructions),
    ("m3b2-context-original-history-immutable", ContextRecoveryNoMutation),
    ("m3b2-context-relevant-tools", ContextRecoveryToolSubset),
    ("m3b2-context-small-request-untouched", ContextRecoveryRefusesSmallHistory),
    ("m3b2-provider-400-recovers-once", ContextRecoveryHttp400ThenSuccess),
    ("m3b2-provider-400-repeat-fail-stop", ContextRecoveryHttp400TwiceStops),
    ("m3b2-provider-sticky-smaller-context", ContextRecoveryStickyPreference),
    ("m3b2-provider-tool-transcript-no-retry", ContextRecoveryOther400NeverRetries),
    ("m3b3-empty-provider-retries-smaller", EmptyContextRetry),
    ("m3b3-empty-provider-retry-bounded", EmptyContextBounded),
    ("m3b3-empty-provider-sticky", EmptyContextSticky),
    ("m3b3-tool-free-context-reduces", ToolFreeContextReduction),
    ("m3b3-tool-free-empty-recovers", ToolFreeEmptyContextRetry),
    ("m3b3-latest-user-instruction-retained", ContextKeepsLargeLatestUser),
    ("m3b3-small-empty-request-untouched", SmallEmptyContextNoRetry),
    ("m3b3-empty-pause-shows-saved-action", EmptyPauseShowsAction),

    ("provider-400-tool-transcript-classified", Http400ToolTranscriptSafe),
    ("provider-400-unrecognized-body-redacted", Http400UnrecognizedSafe),
    ("model-credential-dpapi-roundtrip-no-plaintext", CredentialEncrypted),
    ("model-credential-scoped-to-provider", CredentialEndpointIsolation),
    ("model-credential-corrupt-record-can-be-replaced", CredentialCorruptedRecord),
    ("provider-401-is-typed-authentication-failure", ProviderUnauthorized),
    ("provider-uses-encrypted-saved-bearer-key", ProviderUsesSavedKey),
    ("credential-retry-does-not-duplicate-user-input", CredentialRetryDoesNotDuplicate),
    ("mutation-plain-final-attaches-host-receipt", MutationPlainReplyNeedsEvidence),
    ("mutation-plain-final-does-not-loop", MutationPlainReplyPauses),
    ("action-cannot-be-finished-as-information", MutationMustFinishAsAction),
    ("failed-tool-call-can-recover-with-new-action", ToolFailureCanRecover),
    ("unfinished-plan-cannot-be-marked-complete", UnfinishedPlanRejectsFinish),
    ("http-tool-protocol-real-multistep-write-read-hash", WireEndToEndToolPipeline),
    ("directory-create-has-observed-receipt", DirectoryCreateObserved),
    ("directory-existing-file-is-preserved", DirectoryExistingPreserved),
    ("directory-outside-workspace-requires-approval", DirectoryOutsideDenied),
    ("edit-preserves-utf8-bom-and-crlf", EditTextPreservesBytes),
    ("edit-stale-hash-rejected", EditTextStaleHash),
    ("edit-ambiguous-match-rejected", EditTextAmbiguous),
    ("edit-outside-workspace-requires-approval", EditTextApprovedOutside),
    ("http-tool-protocol-versioned-edit-readback", WireEditTextPipeline),
    ("process-execution-requires-explicit-approval", ProcessDenied),
    ("process-direct-argv-captures-exit-and-output", ProcessDotnetVersion),
    ("process-working-directory-cannot-escape-workspace", ProcessWorkingDirectoryScope),
    ("process-does-not-inherit-api-key", ProcessSecretsExcluded),
    ("process-kills-timeout-and-records-it", ProcessTimeout),
    ("process-nonzero-exit-still-reports-effects", ProcessNonzeroExit),
    ("process-agent-reports-approved-execution-evidence", ProcessAgentExecution),
    ("process-plain-final-host-attests-stdout-and-exit", ProcessPlainFinalEvidence),
    ("mutation-after-recoverable-failure-attests-only-success", PlainFinalRecoverableFailure),
    ("mutation-plain-final-refuses-unfinished-plan", MutationPlainPendingPlan),
    ("uncertain-operation-cannot-plain-complete", UnknownOperationPlainFinal),
    ("tool-approval-question-routes-to-registered-action", ToolApprovalQuestionRoutesToAction),
    ("outside-write-denial-cannot-be-replaced-by-chat-consent", DeniedActionCannotBeBypassed),
    ("legitimate-missing-detail-still-asks-user", LegitimateMissingDetailQuestion),
    ("repeated-permission-probes-pause-without-user-interruption", RepeatedPermissionProbesPause),
    ("http-permission-question-gate-preserves-tool-transcript", HttpPermissionQuestionGate),
    ("process-inherits-safe-windows-os-identity", ProcessInheritsOsIdentity),
    ("explicit-lowercase-yes-authorizes-only-yes", ExplicitYesVariants),
    ("denied-process-pauses-with-no-repeat", DeniedProcessPausesWithoutReplay),
    ("plain-permission-question-recovers-to-process-tool", PlainPermissionRecoversToProcess),
    ("repeated-plain-permission-questions-pause", RepeatedPlainPermissionPauses),
    ("structured-finish-cannot-ask-tool-permission", StructuredFinishPermissionRejected),
    ("denial-after-write-preserves-partial-evidence", DenialAfterWritePreservesReceipt),
    ("permission-question-after-success-does-not-rerun", ObservedProcessNotRepeatedOnPermissionReply),
    ("tool-free-fallback-permission-prose-cannot-complete", ToolFreePermissionFallbackPauses),
    ("permission-probe-counter-resets-after-observed-tool", PermissionProbeCounterResetsAfterTool),
    ("browser-rejects-http-and-credentials", BrowserRejectsNonHttpsAndCredentials),
    ("browser-rejects-internal-hosts-and-ports", BrowserRejectsInternalHostAndPorts),
    ("browser-dns-address-filter-blocks-private", BrowserPublicDnsAddressGuards),
    ("browser-origin-and-method-routing", BrowserSameHostAndMethodEnforced),
    ("browser-tool-schema-is-strict", BrowserInspectSchemaIsStrict),
    ("browser-approval-denial-never-visits", BrowserDenialPreventsVisit),
    ("browser-approved-page-observation-is-audited", BrowserApprovedObservationIsAudited),
    ("browser-cross-origin-redirect-refused", BrowserRedirectOutsideApprovedOriginRejected),
    ("browser-accepts-public-url-and-dns", BrowserValidPublicAddressAndUrl),
    ("browser-http-wire-tool-transcript", BrowserHttpWireProtocolRoundTrip),
    ("browser-elements-disclose-metadata-not-values", BrowserElementsMetadataOnly),
    ("browser-link-schema-requires-exact-approved-url", BrowserLinkStrictSchema),
    ("browser-link-target-requires-valid-same-host-url", BrowserLinkTargetGuard),
    ("browser-link-denied-before-visiting", BrowserLinkDenialNeverVisits),
    ("browser-link-approved-two-page-observation", BrowserLinkApprovedTwoPageObservation),
    ("browser-link-changed-href-never-visits-destination", BrowserLinkChangedHrefStops),
    ("browser-link-missing-index-never-visits-destination", BrowserLinkMissingIndexStops),
    ("browser-link-source-redirect-blocked", BrowserLinkSourceRedirectStopped),
    ("browser-link-target-redirect-blocked", BrowserLinkDestinationRedirectStopped),
    ("browser-link-http-wire-receipt-reaches-model", BrowserLinkWireReceipt),
    ("browser-follow-permission-question-routes-to-host", BrowserFollowPermissionRoutes),
    ("browser-session-schema-and-destination-guards", SessionSchemasAndDestinations),
    ("browser-session-open-denial-prevents-launch", SessionOpenDenied),
    ("browser-session-open-approved-records-receipt", SessionOpenApproved),
    ("browser-session-inspection-reuses-current-page", SessionInspectUsesExisting),
    ("browser-session-follow-denied-before-navigation", SessionFollowDenied),
    ("browser-session-follow-recorded-after-approval", SessionFollowApproved),
    ("browser-session-link-changed-refused", SessionChangedLinkRefused),
    ("browser-session-cross-host-approval-denied", SessionCrossHostRejected),
    ("browser-session-close-disposes-and-expires", SessionCloseAndExpired),
    ("browser-auto-close-plain-final", BrowserAutoClosePlain),
    ("browser-auto-close-structured-finish", BrowserAutoCloseStructured),
    ("browser-auto-close-needs-no-extra-approval", BrowserAutoCloseApproval),
    ("browser-auto-close-receipt-durable", BrowserAutoCloseDurable),
    ("browser-auto-close-explicit-not-repeated", BrowserAutoCloseManual),
    ("browser-auto-close-missing-tool-pauses", BrowserAutoCloseMissingTool),
    ("browser-auto-close-corrupted-open-pauses", BrowserAutoCloseCorrupted),
    ("browser-auto-close-failed-pauses", BrowserAutoCloseFailed),
    ("browser-auto-close-uncertain-reconciles", BrowserAutoCloseUncertain),
    ("browser-auto-close-no-session-noop", BrowserAutoCloseNoSession),
    ("browser-auto-close-pending-plan-preserved", BrowserAutoClosePlanPending),
    ("browser-auto-close-multiple-sessions", BrowserAutoCloseMultiple),
    ("browser-text-entry-dom-index-visible", TextEntryDomIndexVisible),
    ("browser-text-entry-target-validation", TextEntryTargetValidation),
    ("browser-text-entry-is-dangerous", TextEntryDangerousRisk),
    ("browser-text-entry-denial-prevents-fill", TextEntryDenied),
    ("browser-text-entry-approved-receipt-redacted", TextEntryApprovedAndRedacted),
    ("browser-text-entry-stale-label-refused", TextEntryStaleLabel),
    ("browser-text-entry-password-and-disabled-refused", TextEntryPasswordAndDisabled),
    ("browser-text-entry-close-prevents-retry", TextEntryAfterClose),
    ("browser-text-entry-repeated-approved-fills", TextEntryRepeatedFills),
    ("browser-text-entry-uncertain-effect-reconciled", TextEntryUncertainReconciliation),
    ("browser-button-click-strict-schema", ButtonClickStrictSchema),
    ("browser-button-click-target-guards", ButtonClickTargetGuards),
    ("browser-button-click-dangerous-approval-details", ButtonClickDangerousApproval),
    ("browser-button-click-denial-prevents-action", ButtonClickDenied),
    ("browser-button-click-approved-records-receipt", ButtonClickApproved),
    ("browser-button-click-stale-metadata-refused", ButtonClickStaleMetadata),
    ("browser-button-click-submit-and-disabled-refused", ButtonClickSubmitAndDisabled),
    ("browser-button-click-after-close-refused", ButtonClickClosedSession),
    ("browser-button-click-sequential-actions", ButtonClickSequential),
    ("browser-button-click-uncertain-effect-reconciled", ButtonClickUncertainReconciliation),
    ("browser-multi-state-default-and-verification", MultiStateDefault),
    ("browser-multi-state-uncertain-blocks-retry", MultiStateUncertain),
    ("browser-multi-state-eight-action-bound", MultiStateBound),
    ("browser-multi-approval-every-action", MultiApprovalEachAction),
    ("browser-multi-denied-followup-preserves-first", MultiDeniedFollowup),
    ("browser-multi-two-fields-and-button", MultiTwoFieldsAndButton),
    ("browser-multi-stale-second-control-refused", MultiStaleSecondControl),
    ("browser-multi-network-locked-after-first", MultiNetworkLock),
    ("browser-multi-refuse-submit-after-first", MultiRefuseSubmit),
    ("browser-multi-interruption-blocks-next", MultiUncertainThenRefuse),
    ("browser-multi-close-and-expire", MultiCloseAndExpire),
    ("browser-multi-verified-receipts-separate", MultiReceiptsSeparate),
    ("browser-accessibility-transition-expanded", AccessibleExpandedTransition),
    ("browser-accessibility-transition-nochange", AccessibleUnchangedState),
    ("browser-accessibility-transition-missing-before", AccessibleMissingBefore),
    ("browser-accessibility-transition-identity-changed", AccessibleIdentityChanged),
    ("browser-accessibility-transition-pressed", AccessiblePressedTransition),
    ("browser-accessibility-transition-checked", AccessibleCheckedTransition),
    ("browser-accessibility-transition-disappeared", AccessibleControlDisappeared),
    ("browser-accessibility-records-only-state", AccessibleStateMetadataOnly),
    ("browser-click-receipt-proves-transition", ButtonClickReceiptWithTransition),
    ("browser-click-receipt-not-proven-without-state", ButtonClickReceiptWithoutState),
    ("browser-click-receipt-not-proven-without-change", ButtonClickReceiptWithoutChange),
    ("browser-click-sequential-transitions-independent", ButtonClickSequentialStateEvidence),
    ("browser-semantic-role-mapping", SemanticRoleMapping),
    ("browser-semantic-unique-name", SemanticUniqueNamedControl),
    ("browser-semantic-duplicate-refused", SemanticDuplicateRefused),
    ("browser-semantic-unsafe-controls-refused", SemanticUnsafeTargetsRefused),
    ("browser-semantic-strict-schema", SemanticStrictSchemaAndApprovalDetails),
    ("browser-semantic-action-combinations", SemanticInvalidActionCombinations),
    ("browser-semantic-denial-before-dispatch", SemanticDenialPreventsDispatch),
    ("browser-semantic-approved-click-evidence", SemanticApprovedClickEvidence),
    ("browser-semantic-approved-fill-redacted", SemanticApprovedFillRedactedReceipt),
    ("browser-semantic-changed-name-rejected", SemanticChangedNameRejected),
    ("browser-semantic-uncertain-effect-stops", SemanticUncertainEffectStopsAgent),
    ("browser-semantic-multi-approval", SemanticMultiStepIndependentApproval),
    ("browser-named-link-schema", NamedLinkSchema),
    ("browser-named-link-approval-displays-exact-url", NamedLinkApprovalDetails),
    ("browser-named-link-invalid-targets", NamedLinkInvalidTargets),
    ("browser-named-link-unique-identity", NamedLinkUniqueIdentity),
    ("browser-named-link-duplicate-refused", NamedLinkDuplicateRefused),
    ("browser-named-link-changed-href-refused", NamedLinkChangedHref),
    ("browser-named-link-denied-before-dispatch", NamedLinkDenied),
    ("browser-named-link-approved-audit-receipt", NamedLinkApproved),
    ("browser-named-link-stale-source-refused", NamedLinkStaleSource),
    ("browser-named-link-changed-name-refused", NamedLinkChangedName),
    ("browser-named-link-blocked-after-interaction", NamedLinkAfterInteractionBlocked),
    ("browser-named-link-uncertain-navigation-reconcile", NamedLinkUncertainReconciliation),
    ("browser-select-option-schema-strict", SelectOptionSchema),
    ("browser-select-option-approval-exact", SelectOptionApprovalExact),
    ("browser-select-option-semantic-metadata", SelectOptionMetadata),
    ("browser-select-option-duplicate-control-refused", SelectOptionDuplicateControl),
    ("browser-select-option-duplicate-option-refused", SelectOptionDuplicateOption),
    ("browser-select-option-missing-or-disabled-refused", SelectOptionMissingOrDisabled),
    ("browser-select-option-denied-no-dispatch", SelectOptionDenied),
    ("browser-select-option-approved-receipt", SelectOptionApprovedReceipt),
    ("browser-select-option-changed-name-refused", SelectOptionChangedName),
    ("browser-select-option-after-text-approval-independent", SelectOptionIndependentApprovals),
    ("browser-select-option-network-stays-blocked", SelectOptionNoNavigationAfter),
    ("browser-select-option-uncertain-reconciliation", SelectOptionUncertain),
    ("browser-set-checked-schema-strict", CheckedSchema),
    ("browser-set-checked-radio-uncheck-rejected", CheckedRadioUncheck),
    ("browser-set-checked-metadata-native", CheckedMetadata),
    ("browser-set-checked-duplicate-refused", CheckedDuplicate),
    ("browser-set-checked-disabled-or-stale-refused", CheckedDisabledStale),
    ("browser-set-checked-denial-prevents-action", CheckedDenied),
    ("browser-set-checked-approved-receipt", CheckedApproved),
    ("browser-set-checked-noop-not-dispatched", CheckedNoop),
    ("browser-set-checked-after-select-separate-approvals", CheckedIndependent),
    ("browser-set-checked-network-remains-blocked", CheckedNoNavigation),
    ("browser-set-checked-uncertain-effect-reconciled", CheckedUncertain),
    ("browser-set-checked-radio-check-verified", CheckedRadioApproved),
    ("browser-control-id-schema", ControlIdSchema),
    ("browser-control-id-validator", ControlIdValidator),
    ("browser-control-id-duplicate-name-selection", ControlIdDuplicateName),
    ("browser-control-id-no-id-still-refused", ControlIdNoGuess),
    ("browser-control-id-unknown-rejected", ControlIdUnknown),
    ("browser-control-id-stale-rejected", ControlIdStale),
    ("browser-control-id-disjoint-name-rejected", ControlIdWrongName),
    ("browser-control-id-independent-approvals", ControlIdApproved),
    ("browser-control-id-receipt-identity", ControlIdReceipt),
    ("browser-control-id-radio-selection", ControlIdRadio),
    ("browser-control-id-noop-not-dispatched", ControlIdNoop),
    ("browser-control-id-network-remains-blocked", ControlIdNoNavigation),
    ("browser-control-pages-schema-strict", ControlPagesSchema),
    ("browser-control-pages-bounds", ControlPagesBounds),
    ("browser-control-pages-index-mapping", ControlPagesIndexMapping),
    ("browser-control-pages-approval-description", ControlPagesApprovalDescription),
    ("browser-control-pages-readonly-receipt", ControlPagesReceipt),
    ("browser-control-pages-denial-prevents-read", ControlPagesDenied),
    ("browser-control-pages-invalid-page-rejected", ControlPagesInvalidPage),
    ("browser-control-pages-high-control-visible", ControlPagesLaterControl),
    ("browser-control-pages-no-sensitive-values", ControlPagesNoValues),
    ("browser-control-pages-closed-session-rejected", ControlPagesClosed),
    ("browser-control-pages-read-does-not-block-navigation", ControlPagesReadNoNetworkLock),
    ("browser-control-pages-separate-approvals", ControlPagesSeparateApprovals),
    ("browser-link-pages-schema-strict", LinkPagesSchema),
    ("browser-link-pages-bounded", LinkPagesBounds),
    ("browser-link-pages-approval-target", LinkPagesApproval),
    ("browser-link-pages-readonly-receipt", LinkPagesReceipt),
    ("browser-link-pages-denied", LinkPagesDenied),
    ("browser-link-pages-late-identity", LinkPagesLateIdentity),
    ("browser-link-pages-stale-href-refused", LinkPagesStaleHref),
    ("browser-link-pages-hidden-values-excluded", LinkPagesNoValues),
    ("browser-link-pages-follow-approved", LinkPagesFollowApproved),
    ("browser-link-pages-follow-denied", LinkPagesFollowDenied),
    ("browser-link-pages-after-interaction-blocked", LinkPagesAfterInteraction),
    ("browser-link-pages-independent-approvals", LinkPagesIndependentApprovals),
    ("browser-paged-link-exact-pair", ScopedLinkExactPair),
    ("browser-paged-link-duplicate-pair-refused", ScopedLinkDuplicatePair),
    ("browser-paged-link-stale-pair-refused", ScopedLinkStalePair),
    ("browser-paged-link-other-page-duplicate-allowed", ScopedLinkOtherPageDuplicate),
    ("browser-canonical-default-stays-exact", CanonicalDefaultExact),
    ("browser-canonical-slash-only", CanonicalSlashOnly),
    ("browser-canonical-reject-query-and-file", CanonicalRejectUnsafe),
    ("browser-canonical-schema-strict", CanonicalSchema),
    ("browser-canonical-approval-shows-both-urls", CanonicalApproval),
    ("browser-canonical-approved-receipt", CanonicalApprovedReceipt),
    ("browser-canonical-denial-prevents-navigation", CanonicalDenied),
    ("browser-canonical-network-still-locked", CanonicalAfterInteraction),
    ("browser-visible-text-schema-strict", VisibleTextSchema),
    ("browser-visible-text-page-bounds", VisibleTextBounds),
    ("browser-visible-text-4000-chars", VisibleTextFixedSlice),
    ("browser-visible-text-after-10000", VisibleTextBeyondSnapshot),
    ("browser-visible-text-empty-final", VisibleTextEmptyFinal),
    ("browser-visible-text-hard-limit", VisibleTextHardLimit),
    ("browser-visible-text-approval-target", VisibleTextApprovalTarget),
    ("browser-visible-text-receipt-readonly", VisibleTextReceipt),
    ("browser-visible-text-no-input-values", VisibleTextNoInputValues),
    ("browser-visible-text-denied-no-read", VisibleTextDenied),
    ("browser-visible-text-after-interaction-readonly", VisibleTextAfterInteraction),
    ("browser-visible-text-closed-session-refused", VisibleTextClosed),
    ("model-text-tool-json-blocks-false-completion", TextToolJsonBlocksFalseCompletion),
    ("model-text-tool-json-recovers-host-approved", TextToolJsonRecoversApproved),
    ("model-text-tool-fenced-recovers", TextToolFencedRecovers),
    ("model-text-tool-name-envelope-recovers", TextToolNameEnvelopeRecovers),
    ("model-text-tool-nested-envelope-recovers", TextToolNestedEnvelopeRecovers),
    ("model-text-tool-calls-envelope-recovers", TextToolCallsEnvelopeRecovers),
    ("model-text-tool-repeated-pauses", TextToolRepeatedPauses),
    ("model-text-tool-then-fake-done-pauses", TextToolFakeDonePauses),
    ("model-text-tool-structured-finish-refused", TextToolStructuredFinishRefused),
    ("model-text-tool-recovery-is-transient", TextToolGuidanceTransient),
    ("model-non-tool-json-remains-answer", TextToolOrdinaryJsonAnswer),
    ("model-explanatory-example-remains-answer", TextToolExampleAnswer),
    ("workflow-plan-typed-kinds", WorkflowPlanTyped),
    ("workflow-plan-no-silent-replacement", WorkflowPlanNoReplace),
    ("workflow-plan-status-inspection", WorkflowPlanStatus),
    ("workflow-plan-status-strict-schema", WorkflowPlanStatusSchema),
    ("workflow-action-proven-by-mutation", WorkflowActionValid),
    ("workflow-action-rejects-read-only-proof", WorkflowActionReadRefused),
    ("workflow-action-requires-proof", WorkflowActionNoProof),
    ("workflow-action-rejects-unknown-id", WorkflowActionUnknownProof),
    ("workflow-action-rejects-failed-operation", WorkflowActionFailedProof),
    ("workflow-action-rejects-false-receipt", WorkflowActionFalseReceipt),
    ("workflow-observation-accepts-read", WorkflowObservationRead),
    ("workflow-observation-accepts-mutation", WorkflowObservationChanged),
    ("workflow-observation-requires-proof", WorkflowObservationNoProof),
    ("workflow-analysis-allows-no-proof", WorkflowAnalysisNoProof),
    ("workflow-legacy-checkpoint-compatible", WorkflowLegacyNoProof),
    ("workflow-proof-cannot-complete-two-steps", WorkflowDuplicateProof),
    ("workflow-noncompleted-cannot-claim-proof", WorkflowPendingProof),
    ("workflow-completed-step-cannot-revert", WorkflowLockedRevert),
    ("workflow-completed-step-cannot-replace-proof", WorkflowLockedProof),
    ("workflow-repeat-same-proof-idempotent", WorkflowIdempotent),
    ("workflow-plain-final-checks-proof", WorkflowPlainForged),
    ("workflow-structured-finish-checks-proof", WorkflowStructuredForged),
    ("workflow-end-to-end-file-write-read-verify", WorkflowEndToEnd),
    ("workflow-status-without-plan", WorkflowEmptyStatus),
    ("auto-plan-natural-file-workflow", AutoPlanFileWorkflow),
    ("auto-plan-direct-conversation", AutoPlanDirectConversation),
    ("auto-plan-direct-single-action", AutoPlanSingleAction),
    ("auto-plan-textual-json-not-executed", AutoPlanTextIgnored),
    ("auto-plan-wrong-tool-not-dispatched", AutoPlanWrongTool),
    ("auto-plan-malformed-size-rejected", AutoPlanWrongSize),
    ("auto-plan-invalid-kind-rejected", AutoPlanInvalidKind),
    ("auto-plan-no-repeat-after-question", AutoPlanNoRepeat),
    ("auto-plan-resume-durable-context", AutoPlanResume),
    ("auto-plan-existing-plan-preserved", AutoPlanExistingPlan),
    ("auto-plan-transport-exposes-only-decision", AutoPlanWireIsolation),
    ("auto-plan-direct-no-approval", AutoPlanNoApproval),
    ("m1c-planner-text-json-saves-plan", M1cPlannerJsonPlan),
    ("m1c-planner-fenced-json-accepted", M1cPlannerFencedJson),
    ("m1c-planner-json-direct", M1cPlannerJsonDirect),
    ("m1c-planner-malformed-decision-reported", M1cPlannerMalformed),
    ("m1c-planner-executable-text-rejected", M1cPlannerExecutableText),
    ("m1c-planner-unknown-tool-binding-refused", M1cPlannerUnknownBinding),
    ("m1c-planner-readonly-action-binding-refused", M1cPlannerReadOnlyBinding),
    ("m1c-planner-analysis-tool-binding-refused", M1cPlannerAnalysisBinding),
    ("m1c-planner-native-binding-saved", M1cNativePlanBinding),
    ("m1c-evidence-must-match-expected-tool", M1cEvidenceWrongTool),
    ("m1c-evidence-matching-tool-accepted", M1cEvidenceMatchingTool),
    ("m1c-automatic-premature-finish-recovers", M1cAutoFinishRecovery),
    ("m1c-automatic-premature-finish-bounded", M1cAutoFinishBounded),
    ("m1c-recovery-preserves-successful-mutation", M1cRecoveryNoDuplicateWrite),
    ("m1c-plan-status-includes-diagnostics", M1cPlanStatusDiagnostic),
    ("m1c-plan-tool-rejects-invalid-binding", M1cManualPlanBadBinding),
    ("m1c-direct-then-manual-plan-not-auto-recovered", M1cManualPlanNoAutoRecovery),
    ("m1c-legacy-saved-auto-plan-resumable", M1cLegacyAutoPlanRecovery),
    ("planning-retry-recovers-text-json-plan", PlanningRetryPlan),
    ("planning-retry-recovers-direct-decision", PlanningRetryDirect),
    ("planning-retry-rejects-executable-tool-call", PlanningRetryRejectsTool),
    ("planning-retry-rejects-forged-tool-binding", PlanningRetryRejectsBinding),
    ("planning-retry-invalid-still-allows-execution", PlanningRetryInvalidGraceful),
    ("planning-retry-persists-and-does-not-repeat", PlanningRetryNoRepeat),
    ("planning-retry-native-accepted-does-not-run", PlanningRetryNoFallbackOnValid),
    ("planning-retry-http-tool-free-decision-then-execution", PlanningRetryWireRoundTrip),
    ("desktop-tools-registered-general-purpose", DesktopToolsRegistered),
    ("desktop-window-list-schema-strict", DesktopSchemaStrict),
    ("desktop-list-denial-prevents-inspection", DesktopListDenied),
    ("desktop-list-approved-returns-window-identity", DesktopListApproved),
    ("desktop-inspect-denial-prevents-inspection", DesktopInspectDenied),
    ("desktop-inspect-approved-bounded-metadata", DesktopInspectApproved),
    ("desktop-activate-denied-does-not-dispatch", DesktopActivateDenied),
    ("desktop-activate-approved-changed-state-receipt", DesktopActivateApproved),
    ("desktop-invoke-denied-does-not-dispatch", DesktopInvokeDenied),
    ("desktop-invoke-approved-but-outcome-unverified", DesktopInvokeApproved),
    ("desktop-set-text-denied-no-effect", DesktopSetTextDenied),
    ("desktop-set-text-approved-verified", DesktopSetTextApproved),
    ("desktop-window-stale-rejected", DesktopStaleWindow),
    ("desktop-control-stale-rejected", DesktopStaleControl),
    ("desktop-end-to-end-list-activate-inspect-invoke", DesktopEndToEnd),
    ("desktop-rich-editor-advertises-separate-input", DesktopRichEditorMetadata),
    ("desktop-rich-editor-typing-requires-approval", DesktopTypeTextDenied),
    ("desktop-rich-editor-typing-receipt-verified", DesktopTypeTextApproved),
    ("desktop-rich-editor-input-refuses-overwrite", DesktopTypeTextRequiresEmpty),
    ("desktop-rich-editor-input-rejects-control-characters", DesktopTypeTextControlsRefused),
    ("desktop-unsupported-set-text-pauses-without-loop", DesktopUnsupportedSetTextPauses),
    ("desktop-blocked-retry-does-not-spend-model-rounds", DesktopBlockedRetrySafe),
    ("desktop-set-text-permission-question-routes-to-host", DesktopSetPermissionRoutesHost),
    ("desktop-typed-text-comparison-rejects-same-length-corruption", DesktopExactTextComparison),
    ("desktop-typed-text-unverified-must-pause", DesktopUnverifiedTypingPauses),
    ("desktop-typed-text-unverified-cannot-silent-retry", DesktopUnverifiedTypingRetryBlocked),
    ("desktop-set-text-unverified-must-pause", DesktopUnverifiedSetTextPauses),
    ("desktop-win32-input-marshalling-contract", DesktopWin32InputLayout),
    ("desktop-foreground-already-focused-no-activation", DesktopForegroundAlreadyFocused),
    ("desktop-foreground-request-activation-once", DesktopForegroundRequestsOnce),
    ("desktop-foreground-observation-denied", DesktopForegroundDenial),
    ("desktop-foreground-bounds-check-count", DesktopForegroundInvalidChecks),
    ("desktop-type-approval-no-manual-switch", DesktopTypeToolAutonomousDescriptor),
    ("task-abandon-requires-two-presses", TaskAbandonTwoPresses),
    ("task-abandon-expiry-and-reset", TaskAbandonExpiryAndReset),
    ("task-abandon-does-not-clear-dispatched-action", TaskAbandonUncertainEffects),
    ("task-abandon-marks-safe-task-failed", TaskAbandonSafeTask),
    ("m3a-productivity-tools-registered", ProductivityToolsAreRegistered),
    ("m3a-office-schema-strict", ProductivitySchemaRejectsUnknownArguments),
    ("m3a-xlsx-create-inspect-read", ProductivityCreateReadWorkbook),
    ("m3a-xlsx-paged-real-rows", ProductivityWorkbookPaginates),
    ("m3a-xlsx-literal-formula-safe", ProductivityWorkbookPreservesLiteralFormula),
    ("m3a-xlsx-no-overwrite", ProductivityWorkbookNeverOverwrites),
    ("m3a-xlsx-invalid-cell-is-safe", ProductivityWorkbookRejectsNestedObjects),
    ("m3a-docx-create-inspect-read", ProductivityCreateReadDocument),
    ("m3a-docx-real-table", ProductivityWordCreatesTables),
    ("m3a-docx-no-overwrite", ProductivityDocumentNeverOverwrites),
    ("m3a-missing-parent-safe", ProductivityRejectsMissingParent),
    ("m3a-unsupported-input-safe", ProductivityReadBoundsAndUnsupportedFiles),
    ("m3a-outside-workspace-requires-approval", ProductivityOutsideWorkspaceNeedsApproval),
    ("m3a-read-zero-based-model-bounds-normalized", ProductivityReadNormalizesZero),
    ("m3a-read-oversized-row-column-limits", ProductivityReadClampsLimits),
    ("m3a-read-negative-counts-are-safe", ProductivityReadClampsNegative),
    ("m3a-read-receipt-reports-effective-bounds", ProductivityReadReportsBounds),
    ("m3a-read-excel-ignores-irrelevant-bounds", ProductivityExcelIgnoresUnrelatedBounds),
    ("m3a-read-word-ignores-irrelevant-bounds", ProductivityWordIgnoresUnrelatedBounds),
    ("m3a-read-word-zero-based-index-normalized", ProductivityWordZeroIndex),
    ("m3a-read-omitted-fields-default-safely", ProductivityReadOmittedDefaults),
    ("m3a-read-oversized-requests-page-at-80", ProductivityReadPagesAfterClamp),
    ("m3a-read-invalid-types-rejected-before-access", ProductivityReadSchemaStillStrict),
    ("m3a-read-workbook-then-create-word", ProductivityReadThenCreateWord),
    ("m3a-read-does-not-change-workbook-bytes", ProductivityReadNoFileMutation),
    ("m3b-revision-tools-registered", M3bRevisionToolsRegistered),
    ("m3b-revision-schemas-strict", M3bRevisionToolSchemasStrict),
    ("m3b-workbook-version-retains-original", M3bWorkbookRevisionPreservesSource),
    ("m3b-workbook-leading-zero-identifiers", M3bWorkbookLiteralIdPreserved),
    ("m3b-workbook-stale-sha-denied", M3bWorkbookStaleHashDenied),
    ("m3b-workbook-duplicate-cell-refused", M3bWorkbookDuplicateEditDenied),
    ("m3b-workbook-destination-no-overwrite", M3bWorkbookDestinationNeverOverwritten),
    ("m3b-workbook-unknown-sheet-refused", M3bWorkbookUnknownSheetDenied),
    ("m3b-workbook-external-formula-refused", M3bWorkbookUnsafeFormulaDenied),
    ("m3b-word-replace-paragraph-versioned", M3bWordReplaceParagraphPreservesSource),
    ("m3b-word-convert-paragraph-to-table", M3bWordConvertsParagraphToRealTable),
    ("m3b-word-append-heading-paragraph-table", M3bWordAppendHeadingAndTable),
    ("m3b-word-stale-paragraph-refused", M3bWordStaleParagraphDenied),
    ("m3b-word-version-no-overwrite", M3bWordNoOverwrite),
    ("m3b-revision-workspace-scope-protected", M3bRevisionOutsideWorkspaceDenied),
    ("m3b-revision-tool-host-receipt", M3bRevisionReceiptsReachAgent),
    ("m3b1-reject-formula-disguised-as-text", M3b1RejectFormulaValue),
    ("m3b1-reject-formula-with-leading-whitespace", M3b1RejectWhitespaceFormulaValue),
    ("m3b1-allow-explicit-literal-formula-text", M3b1AllowExplicitLiteralFormulaText),
    ("m3b1-repair-imported-fake-formula-with-actual-formula", M3b1RepairFakeFormula),
    ("m3b1-reject-literal-flag-with-number", M3b1RejectLiteralFlagWithNumber),
    ("m3b1-reject-literal-flag-with-formula", M3b1RejectLiteralFlagWithFormula),
    ("m3b1-reject-expression-text", M3b1RejectExpressionText),
    ("m3b1-read-exposes-formula-type-without-mutation", M3b1ReadFormulaType),
    ("m3b4-resume-6-of-7-real-workbook-report", M3b4ResumeWorkbookReport),
    ("m3b4-live-host-report-no-extra-model-round", M3b4LiveReport),
    ("m3b4-focused-review-links-unused-receipt", M3b4FocusedLink),
    ("m3b4-focused-review-refuses-new-mutation", M3b4FocusedNoMutation),
    ("m3b4-missing-earlier-proof-stays-blocked", M3b4BadProof),
    ("m3b4-uncertain-effect-stays-blocked", M3b4Uncertain),
    ("m3b4-terminal-analysis-explicit-finish", M3b4TerminalAnalysis),
    ("m3b4-provider-error-reports-remaining-step", M3b4ProviderFailure),
    ("m3b4-report-plus-calculation-not-auto-closed", M3b4SubstantiveReport),
    ("m3b4-already-marked-report-still-delivered", M3b4MarkedReport),
    ("m3c-pdf-export-all-pages", M3cExport),
    ("m3c-pdf-export-range", M3cRange),
    ("m3c-pdf-export-no-overwrite", M3cNoOverwrite),
    ("m3c-pdf-invalid-range-no-output", M3cInvalidRange),
    ("m3c-pdf-search-paged", M3cSearch),
    ("m3c-pdf-scope-protected", M3cScope),
    ("m3c-pdf-tools-registered", M3cRegistered),
    ("m3c-pdf-agent-real-receipt", M3cAgent),
    ("m3d-ocr-registered-readonly", M3dRegistered),
    ("m3d-ocr-page-and-language", M3dPage),
    ("m3d-ocr-source-unchanged", M3dUnchanged),
    ("m3d-ocr-blank-not-verified-blank", M3dBlank),
    ("m3d-ocr-unsupported-and-invalid-page", M3dInvalid),
    ("m3d-ocr-language-unavailable", M3dUnavailable),
    ("m3d-ocr-outside-denial", M3dDenied),
    ("m3d-ocr-bounded-result", M3dBounded),
    ("m3d1-real-png-recognition", M3d1RealPng),
    ("m3d1-real-scanned-pdf-recognition", M3d1RealPdf),
    ("m3d1-worker-diagnostic-propagated", M3d1Diagnostic),
    ("m3e-opt-in-registry", M3eRegistry),
    ("m3e-endpoint-validation", M3eEndpoint),
    ("m3e-image-wire-and-source-hash", M3eWire),
    ("m3e-deny-image-transfer", M3eDenial),
    ("m3e-invalid-image-no-transfer", M3eInvalid),
    ("m3e-empty-response-rejected", M3eEmpty),
    ("m3e-http-error-redacted", M3eHttpError),
    ("m3e-agent-durable-observation", M3eAgent),
    ("m3e1-short-wide-catalog", M3e1ShortCatalogReduction),
    ("m3e1-short-http400-recovery", M3e1ShortHttpRecovery),
    ("m3e1-multistep-saved-review", M3e1MultiStepReview),
    ("m3e1-review-no-replay-and-partial", M3e1ReviewNoReplay),
    ("m3e1-missing-proof-remains-incomplete", M3e1MissingProofStaysIncomplete),
    ("m3e2-resume-complete-readonly-plan", M3e2ResumeComplete),
    ("m3e2-live-last-update-delivers", M3e2LiveLastUpdate),
    ("m3e2-complete-plan-missing-proof-blocked", M3e2MissingProof),
    ("m3e2-complete-plan-uncertain-blocked", M3e2Uncertain),
    ("m3e2-complete-plan-denial-blocked", M3e2Denied),
    ("m3e2-incomplete-plan-not-finalized", M3e2Incomplete),
    ("m3e2-complete-plan-new-instruction-not-swallowed", M3e2NewInstruction),
    ("m3f-bounded-live-catalog", M3fBoundedCatalog),
    ("m3f-model-selects-and-executes", M3fSelectExecute),
    ("m3f-hidden-tool-refused", M3fHiddenRefused),
    ("m9a-inactive-schema-recovery", M9InactiveSchemaRecovery),
    ("m3f-selection-not-approval", M3fApproval),
    ("m3f-invalid-selection-refused", M3fInvalid),
    ("m3f-mixed-selection-no-dispatch", M3fMixed),
    ("m3f-selection-loop-bounded", M3fLoop),
    ("m3f-compaction-retains-active-schemas", M3fCompaction),
    ("m4a-adaptive-no-preflight", M4aAdaptiveDirect),
    ("m4a-adaptive-batch-workflow", M4aAdaptiveBatch),
    ("m4a-adaptive-explicit-plan-proof", M4aAdaptivePlanProof),
    ("m4a-adaptive-plan-size", M4aAdaptivePlanSize),
    ("m4a-adaptive-legacy-plan-preserved", M4aAdaptiveLegacy),
    ("m4a-adaptive-context-partial", M4aAdaptivePartial),
    ("m4b3-stream-visible-only", M4b3Visible),
    ("m4b3-stream-fragmented-tool", M4b3Tool),
    ("m4b3-stream-incomplete-rejected", M4b3Incomplete),
    ("m4b3-stream-json-fallback", M4b3JsonFallback),
    ("m4b4-thread-continuity", M4b4ThreadContinuity),
    ("m4b4-thread-isolation", M4b4ThreadIsolation),
    ("m4b4-thread-incomplete-rejected", M4b4IncompleteRejected),
    ("m4c-attachment-persisted", M4cAttachmentPersisted),
    ("m4c-attachment-scope-unchanged", M4cAttachmentScope),
    ("m4c-attachment-reference-bounds", M4cAttachmentBounds),
    ("m4c-artifact-receipts-only", M4cReceiptOnly),
    ("m4c-artifact-changed-file-rejected", M4cChangedFile),
    ("m4d-chat-preferences-preserve-journal", M4dChatPreferences),
    ("m4d-memory-management", M4dMemoryManagement),
    ("m4d-invalid-chat-preferences", M4dInvalidPreferences),
};
int failed = 0;
foreach (var (name, test) in cases)
{
    try { await test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + " : " + ex); }
}
Console.WriteLine($"NIRA KERNEL TESTS: {cases.Length - failed}/{cases.Length} passed");
return failed == 0 ? 0 : 1;

static async Task<(int Captures, int Approvals, ConversationState State, string[] Files)> WindowCaptureCase(
    bool observe, bool approve, bool mismatch = false, bool stale = false)
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var backend = new CaptureTestBackend { Stale = stale };
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, desktopBackend: backend);
    var replies = new List<ModelReply>();
    if (observe) replies.Add(new ModelReply(null, [Call("list", "desktop.windows_list", new { })]));
    replies.Add(new ModelReply(null, [Call("capture", "desktop.window_capture", new {
        windowId = CaptureTestBackend.Item.WindowId,
        title = mismatch ? "Other window" : CaptureTestBackend.Item.Title })]));
    replies.Add(new ModelReply("The requested window operation has been evaluated.", []));
    var approvals = new FakeApprovals(approve);
    Guid id = Guid.NewGuid();
    await new NiraEngine(new FakeModel(replies.ToArray()), registry, setup.Store,
        new WorkspaceAuthority(setup.Workspace, developmentMode: true), enableAdaptivePlanning: true)
        .RunAsync(id, "Capture the application window.", approvals);
    var state = (await setup.Store.LoadAsync(id, default))!;
    var files = Directory.GetFiles(setup.Workspace);
    foreach (var artifact in RecordedFileArtifacts.From(state))
        Assert(await RecordedFileArtifacts.MatchesAsync(artifact, setup.Workspace), "Artifact must match saved bytes.");
    return (backend.CaptureCalls, approvals.Calls, state, files);
}

static async Task WindowCaptureApproved()
{
    var r = await WindowCaptureCase(true, true);
    Assert(r.Captures == 1 && r.Approvals == 1 && r.Files.Length == 1 && r.State.Status == ConversationStatus.Completed,
        "Observed capture requires one approval and publishes a single verified file.");
    var op = r.State.Operations.Single(o => o.ToolName == "desktop.window_capture");
    using var json = JsonDocument.Parse(op.ResultJson!);
    var data = json.RootElement.GetProperty("Data");
    Assert(op.Status == OperationStatus.Succeeded && data.GetProperty("contentVerified").GetBoolean() &&
        !data.GetProperty("visuallyInterpreted").GetBoolean() && !data.GetProperty("uploaded").GetBoolean() &&
        RecordedFileArtifacts.From(r.State).Count == 1, "Capture does not establish visual analysis or upload.");
}

static async Task WindowCaptureDenied()
{
    var r = await WindowCaptureCase(true, false);
    Assert(r.Captures == 0 && r.Approvals == 1 && r.Files.Length == 0, "Denial must precede pixel access.");
}

static async Task WindowCaptureUnobserved()
{
    var r = await WindowCaptureCase(false, true);
    Assert(r.Captures == 0 && r.Approvals == 0 && r.Files.Length == 0, "Unobserved target must never dispatch.");
}

static async Task WindowCaptureMismatch()
{
    var r = await WindowCaptureCase(true, true, mismatch: true);
    Assert(r.Captures == 0 && r.Approvals == 0 && r.Files.Length == 0, "Approval cannot describe a different title.");
}

static async Task WindowCaptureStale()
{
    var r = await WindowCaptureCase(true, true, stale: true);
    Assert(r.Captures == 1 && r.Approvals == 1 && r.Files.Length == 0 &&
        r.State.Operations.Any(o => o.ToolName == "desktop.window_capture" && o.Status == OperationStatus.Failed),
        "Changed native window must not publish an image.");
}

static Task WindowCaptureVisionPolicy()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, desktopBackend: new CaptureTestBackend(), vision: M3eProvider());
    Assert(registry.TryGet("desktop.window_capture", out var capture) && capture is not null, "Capture must be discoverable.");
    Assert(registry.TryGet("vision.analyze", out var vision) && vision is not null, "Vision stays separately configured.");
    var policy = new WorkspaceAuthority(setup.Workspace, developmentMode: true);
    Assert(!policy.IsPreapproved(capture!.Descriptor, "desktop window") && !policy.IsPreapproved(vision!.Descriptor, "private pixels"),
        "Development policy must not authorize capture or external transfer automatically.");
    Assert(!registry.Validate("desktop.window_capture", "{\"windowId\":\"win-demo-001\",\"title\":\"Demo\",\"wholeScreen\":true}", out _, out _),
        "No arbitrary screen or destination parameters.");
    return Task.CompletedTask;
}

static async Task CaptureChatEvidence()
{
    var r = await WindowCaptureCase(true, true);
    var previews = ImageGalleryEntry.ForChat(r.State);
    Assert(previews.Count == 1 && previews[0].IsLocalCapture && !previews[0].IsGenerated &&
        previews[0].Title == CaptureTestBackend.Item.Title && previews[0].Source == "" &&
        ImageGalleryEntry.FromTask(r.State).Count == 0,
        "A verified screenshot belongs to the original chat, not the global photo library.");
    var op = r.State.Operations.Single(o => o.ToolName == "desktop.window_capture");
    op.Status = OperationStatus.Failed;
    Assert(ImageGalleryEntry.ForChat(r.State).Count == 0, "Failed captures cannot become images.");
    op.Status = OperationStatus.Succeeded;
    using var parsed = JsonDocument.Parse(op.ResultJson!);
    var original = parsed.RootElement.GetProperty("Data");
    op.ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Unverified image", new {
        path = original.GetProperty("path").GetString(), sha256 = original.GetProperty("sha256").GetString(), contentVerified = false
    }, changedState: true));
    Assert(ImageGalleryEntry.ForChat(r.State).Count == 0 && RecordedFileArtifacts.From(r.State).Count == 0,
        "Unverified captures must not be rendered or offered as verified files.");
}

static Task SchemaPropertyFeedback()
{
    var schema = JsonToolSchema.Schema("""
        {"type":"object","properties":{"source":{"type":"string"},"mode":{"type":"string"}},"required":["source"],"additionalProperties":false}
        """);
    Assert(!JsonToolSchema.Validate(schema, JsonSerializer.SerializeToElement(new { guess = "x" }), out string missing) &&
        missing.Contains("required properties:", StringComparison.Ordinal) && missing.Contains("source", StringComparison.Ordinal),
        "Missing fields should show required properties.");
    Assert(!JsonToolSchema.Validate(schema, JsonSerializer.SerializeToElement(new { source = "x", guess = "y" }), out string invalid) &&
        invalid.Contains("allowed properties: source, mode", StringComparison.Ordinal), "Unknown fields should list the real fields.");
    return Task.CompletedTask;
}

static Task SchemaEnumFeedback()
{
    var schema = JsonToolSchema.Schema("{\"type\":\"string\",\"enum\":[\"first\",\"second\"]}");
    Assert(!JsonToolSchema.Validate(schema, JsonSerializer.SerializeToElement("other"), out string error) &&
        error.Contains("first", StringComparison.Ordinal) && error.Contains("second", StringComparison.Ordinal),
        "Validation feedback must show actual permitted values without accepting an invalid value.");
    return Task.CompletedTask;
}

static Task M7cDetectionStatus()
{
    Assert(WindowsApplicationInventory.DetectionStatus(0, 1) == "path_command_observed" &&
        WindowsApplicationInventory.DetectionStatus(1, 0) == "registered_observed" &&
        WindowsApplicationInventory.DetectionStatus(1, 1) == "registered_and_path_observed" &&
        WindowsApplicationInventory.DetectionStatus(0, 0) == "not_observed", "Positive command evidence must not become absence.");
    return Task.CompletedTask;
}

static async Task<(AgentRunResult Result, ConversationState State, int Launches, int Approvals)> M7cLaunchCase(bool observed, bool approve)
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var candidate = new ApplicationCandidate(@"C:\Windows\notepad.exe", new string('A', 64), "test observation");
    var backend = new M7cFakeApplicationBackend(candidate);
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, applicationBackend: backend);
    var replies = new List<ModelReply>();
    if (observed) replies.Add(new ModelReply(null, [Call("findapp", "desktop.application_find", new { executable = "notepad" })]));
    replies.Add(new ModelReply(null, [Call("launchapp", "desktop.application_launch", new { executablePath = candidate.ExecutablePath, fingerprint = candidate.Fingerprint })]));
    replies.Add(new ModelReply("The observed operation was handled.", []));
    var model = new FakeModel(replies.ToArray());
    var approvals = new FakeApprovals(approve);
    Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true),
        enableAdaptivePlanning: true).RunAsync(id, "Open the selected application.", approvals);
    var state = (await setup.Store.LoadAsync(id, default))!;
    return (result, state, backend.LaunchCalls, approvals.Calls);
}

static async Task M7cLaunchDenied()
{
    var result = await M7cLaunchCase(true, false);
    Assert(result.Result.Status == ConversationStatus.Paused && result.Launches == 0 && result.Approvals == 1 &&
        result.State.Operations.Any(o => o.ToolName == "desktop.application_launch" && o.Status == OperationStatus.Rejected), "Launch cannot bypass exact host approval.");
}

static async Task M7cLaunchUnobserved()
{
    var result = await M7cLaunchCase(false, true);
    Assert(result.Launches == 0 && result.Approvals == 0 && !result.State.Operations.Any(o => o.ToolName == "desktop.application_launch"), "Unobserved application must never dispatch or request approval.");
}

static async Task M7cLaunchApproved()
{
    var result = await M7cLaunchCase(true, true);
    Assert(result.Launches == 1 && result.Approvals == 1 && result.State.Operations.Any(o => o.ToolName == "desktop.application_launch" && o.Status == OperationStatus.Succeeded), "Exact observed launch must execute once after approval.");
    using var doc = JsonDocument.Parse(result.State.Operations.Single(o => o.ToolName == "desktop.application_launch").ResultJson!);
    Assert(doc.RootElement.GetProperty("Data").GetProperty("processStarted").GetBoolean() &&
        !doc.RootElement.GetProperty("Data").GetProperty("windowVerified").GetBoolean(), "Process launch must not imply a verified window.");
}

static Task M7cLaunchStale()
{
    // No real program is launched: an absent path must fail before process start.
    using var setup = new TestContext();
    var backend = new WindowsApplicationDesktopBackend();
    string path = Path.Combine(setup.Workspace, "missing-" + Guid.NewGuid().ToString("N") + ".exe");
    var receipt = backend.Launch(path, new string('A', 64), default);
    Assert(!receipt.Success && !receipt.ChangedState && receipt.ErrorCode == "APPLICATION_STALE", "Missing launch target must not be executed.");
    return Task.CompletedTask;
}

static Task M7cDiscoverySchema()
{
    Assert(ApplicationFindTool.NormalizeExecutable("example") == "example.exe" && ApplicationFindTool.NormalizeExecutable("example.exe") == "example.exe", "Exact name normalization.");
    foreach (string invalid in new[] { "../example", "example.exe --flag", "*", @"C:\example.exe" })
    {
        bool rejected = false;
        try { _ = ApplicationFindTool.NormalizeExecutable(invalid); } catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Discovery must reject paths, arguments and wildcards.");
    }
    var backend = new M7cFakeApplicationBackend(new ApplicationCandidate(@"C:\example.exe", new string('A', 64), "test"));
    var launch = new ApplicationLaunchTool(backend);
    Assert(launch.Descriptor.Risk == ToolRisk.Dangerous && launch.Descriptor.HasSideEffects &&
        !JsonToolSchema.Validate(launch.Descriptor.Parameters, JsonSerializer.SerializeToElement(new { executablePath = @"C:\example.exe", fingerprint = new string('A', 64), arguments = new[] { "anything" } }), out _), "No command arguments or preapproval.");
    return Task.CompletedTask;
}

static async Task M7cPhysicalCores()
{
    var receipt = await new SystemInspectTool().ExecuteAsync(JsonSerializer.SerializeToElement(new { }), default);
    Assert(receipt.Success && receipt.Data.TryGetProperty("physicalProcessorCores", out var cores) &&
        (cores.ValueKind == JsonValueKind.Null || cores.GetInt32() > 0), "Physical core count is observed or explicitly unavailable, never inferred.");
}

static async Task M7bProcessInspectionDirect()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    var model = new FakeModel([
        new ModelReply(null, [Call("processes", "system.inspect", new { includeProcesses = true, includeApplications = false, applicationNameContains = "" })]),
        new ModelReply("Here are the observed processes ranked by memory.", [])]);
    var approvals = new FakeApprovals(false);
    Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true),
        enableCapabilityRouting: true, enableAdaptivePlanning: true).RunAsync(id, "Which applications are using the most RAM?", approvals);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && approvals.Calls == 0 && model.ToolSnapshots.Count == 2 &&
        model.ToolSnapshots[0].Any(t => t.Name == "system.inspect") && state.Operations.Count == 1 && state.Operations[0].Status == OperationStatus.Succeeded,
        "Process inspection must be available in the initial catalog, without an irrelevant filter failure or approval.");
    using var receipt = JsonDocument.Parse(state.Operations[0].ResultJson!);
    var data = receipt.RootElement.GetProperty("Data");
    Assert(data.GetProperty("processes").ValueKind == JsonValueKind.Object && data.GetProperty("applications").ValueKind == JsonValueKind.Null &&
        !data.GetProperty("storageRequested").GetBoolean(), "Only the requested optional sections should be observed.");
}

static async Task M7bApplicationsLookup()
{
    using var setup = new TestContext();
    var tool = new SystemInspectTool();
    string[] queries = ["Java", "Python", "Visual Studio Code", "IntelliJ IDEA", "code", "idea"];
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { includeApplications = true, applicationNameContains = "", applicationQueries = queries }), default);
    Assert(receipt.Success && !receipt.ChangedState && receipt.Data.GetProperty("processes").ValueKind == JsonValueKind.Null, "Application discovery must not require unrelated observations.");
    var matches = receipt.Data.GetProperty("applications").GetProperty("matchesByQuery");
    Assert(matches.EnumerateArray().Select(m => m.GetProperty("query").GetString()).SequenceEqual(queries), "Every requested name retains independent coverage even without a match.");
    foreach (var match in matches.EnumerateArray())
        Assert(match.GetProperty("registeredMatches").GetArrayLength() <= 10 && match.GetProperty("pathCommands").GetArrayLength() <= 4, "Application evidence remains bounded.");
    var unused = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { includeApplications = false, applicationNameContains = "a", applicationQueries = queries, includeProcesses = true }), default);
    Assert(unused.Success && unused.Data.GetProperty("applications").ValueKind == JsonValueKind.Null, "Inactive filters do not force collection.");
    var invalid = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { includeApplications = true, applicationQueries = Enumerable.Repeat("x", 9).ToArray() }), default);
    Assert(!invalid.Success, "Excessive application query arrays must be rejected.");
    // Deliberately not executable. Discovery must rely on file presence only, without starting a process.
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "nira-probe.exe"), "not a program");
    Assert(WindowsApplicationInventory.FindPathCommands("nira-probe", [setup.Workspace], default).SequenceEqual(new[] { "nira-probe.exe" }) &&
        WindowsApplicationInventory.FindPathCommands("../nira-probe", [setup.Workspace], default).Count == 0 &&
        WindowsApplicationInventory.FindPathCommands("nira-probe-absent", [setup.Workspace], default).Count == 0,
        "PATH discovery checks safe exact basenames without executing or traversing arbitrary paths.");
}

static async Task M7bClarificationReview()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var model = new FakeModel([
        new ModelReply(null, [Call("unnecessary-question", "nira_ask_user", new { question = "Please run a command and paste the result." })]),
        new ModelReply(null, [Call("observe-apps", "system.inspect", new { includeApplications = true, applicationQueries = new[] { "Java", "Python", "Visual Studio Code", "IntelliJ IDEA" } })]),
        new ModelReply("Here are the observed applications.", [])]);
    var approvals = new FakeApprovals(false);
    var engine = new NiraEngine(model, NiraStandardTools.Create(setup.Workspace, setup.Store, http), setup.Store,
        new WorkspaceAuthority(setup.Workspace, developmentMode: true), enableCapabilityRouting: true, enableAdaptivePlanning: true);
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Check installed applications", approvals);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && state.PendingQuestion is null && approvals.Calls == 0 &&
        state.Operations.Count == 1 && state.Operations[0].Status == OperationStatus.Succeeded &&
        state.Messages.Any(m => m.Role == "tool" && m.Content?.Contains("REVIEW_CLARIFICATION", StringComparison.Ordinal) == true),
        "An unnecessary clarification gets one opportunity to use available observations instead of blocking the task.");
}

static async Task M7bRealClarification()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("first", "nira_ask_user", new { question = "What is your preferred budget?" })]),
        new ModelReply(null, [Call("reviewed", "nira_ask_user", new { question = "What is your preferred budget?" })])]);
    var result = await setup.Engine(model, routing: true, adaptive: true).RunAsync(Guid.NewGuid(), "Help me choose something appropriate", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser && result.Message.Contains("preferred budget", StringComparison.Ordinal) && model.ToolSnapshots.Count == 2,
        "The bounded clarification review must preserve legitimate questions without forcing unrelated tool use.");
}

static Task M7bWindowsFamily()
{
    Assert(SystemInspectTool.WindowsFamily(new Version(10, 0, 26300), "Client") == "Windows 11" &&
        SystemInspectTool.WindowsFamily(new Version(10, 0, 19045), "Client") == "Windows 10" &&
        SystemInspectTool.WindowsFamily(new Version(10, 0, 26100), "Server Core") == "Windows Server" &&
        SystemInspectTool.WindowsFamily(new Version(10, 0, 26300), null) is null,
        "NT kernel version alone must never identify Windows product family.");
    return Task.CompletedTask;
}

static async Task M7bSystemInventory()
{
    var tool = new SystemInspectTool();
    var basic = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { }), default);
    Assert(basic.Success && basic.Data.GetProperty("hardware").ValueKind == JsonValueKind.Null &&
        basic.Data.GetProperty("processes").ValueKind == JsonValueKind.Null &&
        basic.Data.GetProperty("applications").ValueKind == JsonValueKind.Null, "Optional inventory must be opt-in.");
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { includeHardware = true,
        includeProcesses = true, includeApplications = true, applicationNameContains = "NIRA_TEST_NO_MATCH_49F31" }), default);
    Assert(receipt.Success && !receipt.ChangedState && receipt.Data.GetProperty("hardware").GetProperty("displayAdapters").GetArrayLength() <= 32 &&
        receipt.Data.GetProperty("applications").GetProperty("entries").GetArrayLength() == 0,
        "Read-only inventory is bounded and app name filter is honored.");
    var processes = receipt.Data.GetProperty("processes");
    if (processes.TryGetProperty("topByWorkingSet", out var top))
    {
        Assert(top.GetArrayLength() <= 30, "Process inventory is bounded.");
        long prior = long.MaxValue;
        foreach (var item in top.EnumerateArray())
        {
            long current = item.GetProperty("workingSetBytes").GetInt64();
            Assert(current <= prior && current >= 0 && !item.TryGetProperty("commandLine", out _), "Working set ordering and privacy.");
            prior = current;
        }
    }
    else Assert(processes.GetProperty("unavailable").GetBoolean(), "Unavailable process inventory must be explicit.");
    var unused = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { applicationNameContains = "Java" }), default);
    Assert(unused.Success && unused.Data.GetProperty("applications").ValueKind == JsonValueKind.Null,
        "Unused optional filter must never force application collection or fail an unrelated observation.");
}

static async Task M7bDomainSearch()
{
    using var handler = new FakeApiHandler("{\"results\":[{\"title\":\"Official\",\"url\":\"https://docs.example.org/release\"},{\"title\":\"Wrong host\",\"url\":\"https://example.org.evil.net/release\"}]}");
    using var http = new HttpClient(handler);
    var tool = new TavilySearchTool(http, () => "secret-test-key");
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { query = "official release", includeDomains = new[] { "example.org" } }), default);
    Assert(receipt.Success && receipt.Data.GetProperty("results").GetArrayLength() == 1 &&
        receipt.Data.GetProperty("results")[0].GetProperty("url").GetString() == "https://docs.example.org/release", "Scope must be enforced locally.");
    using var request = JsonDocument.Parse(handler.RequestBody!);
    Assert(request.RootElement.GetProperty("include_domains")[0].GetString() == "example.org", "Domain scope reaches provider.");
    var invalid = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { query = "test", includeDomains = new[] { "https://example.org/path" } }), default);
    Assert(!invalid.Success && handler.Calls == 1, "Invalid scope must not dispatch.");
}

static async Task M7bBrowserReferences()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    const string known = "0123456789abcdef0123456789abcdef";
    const string unknown = "ffffffffffffffffffffffffffffffff";
    const string source = "https://example.org/release";
    var state = new ConversationState { Id = id, Status = ConversationStatus.Running, Objective = "Explain sources", AutoPlanningAttempted = true,
        Messages = [new ChatEntry("user", "Explain sources")],
        Operations = [new AgentOperation { ToolName = "browser.session_inspect", Status = OperationStatus.Succeeded,
            ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Observed page", new { SessionId = known, Page = new { FinalUrl = source } })) }] };
    await setup.Store.SaveAsync(state, default);
    var model = new FakeModel([new ModelReply("Observed fact【" + known + "】. Other claim【" + unknown + "】.\n\n`【" + unknown + "】`", [])]);
    var result = await setup.Engine(model).RunAsync(id, null, new FakeApprovals(false));
    string publicAnswer = result.Message.Split("Host-recorded execution evidence:", StringSplitOptions.None)[0];
    Assert(result.Status == ConversationStatus.Completed && publicAnswer.Contains("[source](" + source + ")", StringComparison.Ordinal) &&
        publicAnswer.Contains("source reference unverified", StringComparison.Ordinal) && publicAnswer.Contains("`【" + unknown + "】`", StringComparison.Ordinal) &&
        !publicAnswer.Contains("【" + known + "】", StringComparison.Ordinal), "Resolve only actual unique source references and preserve code examples.");
}

static async Task M7aSystemSnapshot()
{
    var tool = new SystemInspectTool();
    var args = JsonSerializer.SerializeToElement(new { includeStorage = true });
    var receipt = await tool.ExecuteAsync(args, default);
    Assert(receipt.Success && !receipt.ChangedState && tool.Descriptor.Risk == ToolRisk.ReadLocal && !tool.Descriptor.HasSideEffects,
        "System inspection must be read-only.");
    var data = receipt.Data;
    Assert(data.GetProperty("logicalProcessorsAvailableToProcess").GetInt32() > 0 &&
        data.GetProperty("osVersion").GetString()!.Length > 0 &&
        data.GetProperty("storageRequested").GetBoolean() && data.GetProperty("fixedDrives").GetArrayLength() <= 26 &&
        !data.TryGetProperty("machineName", out _) && !data.TryGetProperty("userName", out _), "Bounded local snapshot fields.");
    if (data.GetProperty("totalPhysicalMemoryBytes").ValueKind == JsonValueKind.Number)
        Assert(data.GetProperty("totalPhysicalMemoryBytes").GetUInt64() > 0 &&
            data.GetProperty("availablePhysicalMemoryBytes").GetUInt64() <= data.GetProperty("totalPhysicalMemoryBytes").GetUInt64(),
            "Physical memory counters must be consistent.");
    var noStorage = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { }), default);
    Assert(noStorage.Success && !noStorage.Data.GetProperty("storageRequested").GetBoolean() && noStorage.Data.GetProperty("fixedDrives").GetArrayLength() == 0,
        "Storage inspection is optional.");
    Assert(!(await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { command = "whoami" }), default)).Success,
        "System inspection cannot execute a command.");
}

static async Task M7aSystemRoute()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    var tool = registry.Descriptors.Single(t => t.Name == "system.inspect");
    var model = new FakeModel([
        new ModelReply(null, [Call("selectsystem", "nira_select_tools", new { names = new[] { "system.inspect" } })]),
        new ModelReply(null, [Call("systemread", "system.inspect", new { includeStorage = false })]),
        new ModelReply("Here is the observed computer configuration.", [])]);
    var approvals = new FakeApprovals(false);
    var authority = new WorkspaceAuthority(setup.Workspace, developmentMode: true);
    Assert(authority.IsPreapproved(tool, "local computer (read-only system information)") &&
        !new WorkspaceAuthority(setup.Workspace).IsPreapproved(tool, "local computer (read-only system information)"),
        "Development read-only policy must not silently change release approval.");
    Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, authority, enableCapabilityRouting: true, enableAdaptivePlanning: true)
        .RunAsync(id, "Inspect my computer", approvals);
    var state = await setup.Store.LoadAsync(id, default);
    Assert(result.Status == ConversationStatus.Completed && approvals.Calls == 0 && state is not null &&
        state.Operations.Count == 1 && state.Operations[0].ToolName == "system.inspect" && state.Operations[0].Status == OperationStatus.Succeeded &&
        model.PromptSnapshots.Last().Any(m => m.Role == "tool" && m.Content?.Contains("totalPhysicalMemoryBytes", StringComparison.Ordinal) == true),
        "Registered system observation must reach the model as an audited receipt.");
}

static async Task M7aSearchResults()
{
    const string key = "fake-search-secret-123";
    string large = new('x', 18000);
    using var handler = new FakeApiHandler(JsonSerializer.Serialize(new { results = new object[] {
        new { title = "First source", url = "https://example.org/guide#section", content = large + key },
        new { title = "Duplicate", url = "https://example.org/guide", content = "duplicate" },
        new { title = "Private", url = "https://localhost/private", content = "hidden" },
        new { title = "Secret URL", url = "https://example.org/secret?key=" + key, content = "hidden" },
        new { title = 42, url = "https://www.example.net/page", content = key },
        "invalid row",
        new { title = "Third source", url = "https://example.com/other", content = "usable" }
    }}));
    using var http = new HttpClient(handler);
    var tool = new TavilySearchTool(http, () => key);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { query = "current public facts", depth = "advanced", maxResults = 3 }), default);
    var items = receipt.Data.GetProperty("results");
    Assert(receipt.Success && !receipt.ChangedState && items.GetArrayLength() == 3 && handler.Calls == 1 &&
        items[0].GetProperty("url").GetString() == "https://example.org/guide#section" &&
        items[0].GetProperty("excerpt").GetString()!.Length == 1200 &&
        items[1].GetProperty("title").GetString() == "www.example.net" &&
        !receipt.Data.GetRawText().Contains(key, StringComparison.Ordinal), "Search must keep exact safe links and bounded excerpts without leaking credentials.");
    using var request = JsonDocument.Parse(handler.RequestBody!);
    Assert(request.RootElement.GetProperty("search_depth").GetString() == "advanced" && request.RootElement.GetProperty("max_results").GetInt32() == 3,
        "Requested research depth and result count must reach the provider.");
}

static async Task M7aSearchFailures()
{
    var args = JsonSerializer.SerializeToElement(new { query = "public test" });
    using var emptyHandler = new FakeApiHandler("{\"results\":[{\"url\":\"https://localhost/private\"},null]}");
    using var emptyHttp = new HttpClient(emptyHandler);
    var tool = new TavilySearchTool(emptyHttp, () => "secret");
    var invalid = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { query = "test", maxResults = 11 }), default);
    Assert(!invalid.Success && emptyHandler.Calls == 0, "Invalid count must fail before network.");
    bool targetRejected = false;
    try { _ = tool.GetTarget(JsonSerializer.SerializeToElement(new { query = "test", maxResults = long.MaxValue })); }
    catch (ArgumentException) { targetRejected = true; }
    Assert(targetRejected, "Oversized integer must be rejected before approval.");
    var empty = await tool.ExecuteAsync(args, default);
    Assert(!empty.Success && empty.ErrorCode == "SEARCH_NO_USABLE_RESULTS", "No candidate is not a successful search.");
    using var bigHandler = new FakeApiHandler(new string('x', 600000));
    using var bigHttp = new HttpClient(bigHandler);
    Assert((await new TavilySearchTool(bigHttp, () => "secret").ExecuteAsync(args, default)).ErrorCode == "SEARCH_RESPONSE_TOO_LARGE", "Response byte limit must fail closed.");
    using var badHandler = new FakeApiHandler("{\"results\":[");
    using var badHttp = new HttpClient(badHandler);
    Assert((await new TavilySearchTool(badHttp, () => "secret").ExecuteAsync(args, default)).ErrorCode == "SEARCH_RESPONSE_INVALID", "Truncated JSON must not be evidence.");
    Assert((await new TavilySearchTool(emptyHttp, () => null).ExecuteAsync(args, default)).ErrorCode == "SEARCH_KEY_MISSING" && emptyHandler.Calls == 1,
        "Missing credentials must not dispatch.");
}

static async Task M7aSearchRoute()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("{\"results\":[{\"title\":\"Source\",\"url\":\"https://example.org/source\",\"content\":\"Observed excerpt\"}]}");
    using var http = new HttpClient(handler);
    var model = new FakeModel([
        new ModelReply(null, [Call("search", "web.search", new { query = "current public facts" })]),
        new ModelReply("[Source](https://example.org/source)", [])]);
    var approvals = new FakeApprovals(false);
    var result = await new NiraEngine(model, new ToolRegistry([new TavilySearchTool(http, () => "secret")]), setup.Store,
        new WorkspaceAuthority(setup.Workspace, developmentMode: true), enableAdaptivePlanning: true)
        .RunAsync(Guid.NewGuid(), "Find a source", approvals);
    Assert(result.Status == ConversationStatus.Completed && handler.Calls == 1 && approvals.Calls == 0 &&
        !result.Message.Contains("Source check (host)", StringComparison.Ordinal) &&
        model.PromptSnapshots.Last().Any(m => m.Role == "tool" && m.Content?.Contains("Observed excerpt", StringComparison.Ordinal) == true),
        "Real search receipt must ground the answer without extra approvals in development mode.");
}

static async Task M7aQualityGuidance()
{
    using var setup = new TestContext();
    var model = new FakeModel([new ModelReply("A concise distinction.", [])]);
    await setup.Engine(model, routing: true, adaptive: true).RunAsync(Guid.NewGuid(), "Explain a conceptual distinction", new FakeApprovals(false), new M5bDesktopProgress());
    string prompt = model.PromptSnapshots.Single()[0].Content!;
    Assert(prompt.Contains("not both plus repeated summaries", StringComparison.Ordinal) &&
        prompt.Contains("subjective experience", StringComparison.Ordinal) &&
        prompt.Contains("Prefer native diagrams for simple cycles", StringComparison.Ordinal) &&
        prompt.Contains("Close EVERY JSON object", StringComparison.Ordinal) &&
        prompt.Contains("actual hardware", StringComparison.Ordinal) &&
        model.ToolSnapshots.Single().Any(t => t.Name == "web.search"),
        "General answer quality, visual choice and research guidance must reach the same inference.");
}

static Task M7aStillTiming()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var tool = new ImageGenerationTool(new WorkspacePathResolver(setup.Workspace), http, new ImageGenerationProvider(new string('a', 32), new string('T', 32)));
    string still = tool.GetTarget(JsonSerializer.SerializeToElement(new { prompt = "Illustrative diagram", width = 1024, height = 1024 }));
    foreach (int timing in new[] { 0, 100 })
        Assert(still == tool.GetTarget(JsonSerializer.SerializeToElement(new { prompt = "Illustrative diagram", width = 1024, height = 1024, animation = false, frameDurationMs = timing })),
            "Redundant still timing must not change the target or initiate animation.");
    return Task.CompletedTask;
}

// M3E.1: reproduce the live 11-message / 48-tool rejection and preserve
// actual observations through a controls-only multi-step checkpoint review.
static IReadOnlyList<ToolDescriptor> M3e1WideCatalog()
{
    var names = new List<string> { "nira_plan_update", "nira_plan_status", "nira_finish",
        "nira_ask_user", "workspace.info", "filesystem.find", "vision.analyze", "productivity.read" };
    for (int i = 0; i < 40; i++) names.Add("unrelated.tool" + i);
    return names.Select(name => new ToolDescriptor(name, new string('d', 600), ToolRisk.ReadLocal,
        JsonToolSchema.Schema("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"))).ToArray();
}

static IReadOnlyList<ChatEntry> M3e1ShortHistory()
{
    var history = new List<ChatEntry> {
        new("system", "Host safety rules"),
        new("system", "Current plan: tool: productivity.read;"),
        new("user", "Describe the image.")
    };
    foreach (string name in new[] { "workspace.info", "filesystem.find", "vision.analyze", "nira_plan_update" })
    {
        string id = "short-" + history.Count;
        history.Add(new ChatEntry("assistant", ToolCalls: [new ModelToolCall(id, name, "{}") ]));
        history.Add(new ChatEntry("tool", JsonSerializer.Serialize(new { receiptId = "receipt-" + id,
            Success = true, ChangedState = false, Data = new { analysis = "SAVED_VISUAL_OBSERVATION" } }), id));
    }
    return history;
}

static Task M3e1ShortCatalogReduction()
{
    var history = M3e1ShortHistory();
    var tools = M3e1WideCatalog();
    Assert(history.Count == 11 && tools.Count == 48 &&
        ModelContextRecovery.TryReduce(history, tools, out var compact, out var selected) &&
        selected.Count <= 15 && selected.Any(t => t.Name == "vision.analyze") &&
        selected.Any(t => t.Name == "productivity.read") && selected.Any(t => t.Name == "nira_plan_update") &&
        compact.Any(m => m.Role == "tool" && m.Content?.Contains("SAVED_VISUAL_OBSERVATION", StringComparison.Ordinal) == true) &&
        compact.Count(m => m.Role == "tool") == 4,
        "The exact live request shape must reduce schemas while preserving complete short observations and planned tools.");
    return Task.CompletedTask;
}

static async Task M3e1ShortHttpRecovery()
{
    using var handler = new ContextRecoverySequenceHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var reply = await model.CompleteAsync(M3e1ShortHistory(), M3e1WideCatalog(), default);
    Assert(reply.Content == "Recovered normal answer" && handler.RequestBodies.Count == 2 &&
        handler.RequestBodies[1].Length < handler.RequestBodies[0].Length,
        "A context rejection with only 11 messages must retry once with fewer schemas.");
    using var packet = JsonDocument.Parse(handler.RequestBodies[1]);
    Assert(packet.RootElement.GetProperty("tools").GetArrayLength() <= 15, "Recovery tool budget must be bounded.");
    await model.CompleteAsync(M3e1ShortHistory(), M3e1WideCatalog(), default);
    Assert(handler.RequestBodies.Count == 3 && handler.RequestBodies[1] == handler.RequestBodies[2],
        "A known tight provider must retain compact inference even for short histories.");
}

static ConversationState M3e1SavedVision()
{
    const string objective = "Find the image and describe its colors.";
    var found = M3b4Operation("saved-find", "filesystem.find", ToolReceipt.Ok("Found file", new { path = "image.png" }));
    var vision = M3b4Operation("saved-vision", "vision.analyze", ToolReceipt.Ok("Provider returned image analysis",
        new { path = "image.png", analysis = "SAVED_VISUAL_OBSERVATION", independentlyVerified = false }));
    return new ConversationState {
        Objective = objective, Messages = [new ChatEntry("user", objective)], Status = ConversationStatus.Running,
        AutoPlanningAttempted = true, AutomaticPlanningOutcome = "planned_fallback_json",
        Operations = [found, vision],
        Plan = [M3b4Completed("Find image", "observation", found),
            new PlanStep { Description = "Obtain visual analysis", Kind = "observation", ToolName = "vision.analyze", SuccessCriterion = "Provider result" },
            new PlanStep { Description = "Describe observed colors", Kind = "analysis", SuccessCriterion = "Answer based on image analysis" }]
    };
}

static async Task M3e1MultiStepReview()
{
    using var setup = new TestContext();
    var state = M3e1SavedVision();
    await setup.Store.SaveAsync(state, default);
    int calls = 0;
    var model = new FakeModel(messages => {
        if (++calls == 1) throw new AgentException("Model provider HTTP 400 (category=possible_context_limit; messages=11; tools=48; requestBytes=41173).");
        Assert(messages.Count == 3 && messages[^1].Content?.Contains("SAVED_VISUAL_OBSERVATION", StringComparison.Ordinal) == true,
            "Compact review retains actual vision data and receipt identity.");
        return calls == 2
            ? new ModelReply(null, [Call("link-vision", "nira_plan_update", new { index = 1, status = "completed", evidenceId = "saved-vision" })])
            : new ModelReply(null, [Call("finish-vision", "nira_finish", new { outcome = "answer", answer = "SAVED_VISUAL_OBSERVATION", evidence_ids = Array.Empty<string>() })]);
    });
    var approval = new FakeApprovals(false);
    var result = await setup.Engine(model).RunAsync(state.Id, null, approval);
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Completed && calls == 3 && approval.Calls == 0 &&
        saved.Operations.Count == 2 && saved.Plan.All(p => p.Status == "completed") &&
        result.Message.Contains("SAVED_VISUAL_OBSERVATION", StringComparison.Ordinal),
        "Multi-step recovery must finish from saved evidence without a new operation.");
}

static async Task M3e1ReviewNoReplay()
{
    using var setup = new TestContext();
    var state = M3e1SavedVision();
    await setup.Store.SaveAsync(state, default);
    int calls = 0;
    var model = new FakeModel(_ => {
        if (++calls == 1) throw new AgentException("Model provider HTTP 400 (category=possible_context_limit; messages=11; tools=48; requestBytes=41173).");
        if (calls == 2) return new ModelReply(null, [Call("repeat-upload", "vision.analyze", new { path = "image.png", question = "repeat" })]);
        throw new AgentException("Synthetic persistent provider failure");
    });
    var approval = new FakeApprovals(true);
    var result = await setup.Engine(model).RunAsync(state.Id, null, approval);
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Paused && approval.Calls == 0 && saved.Operations.Count == 2 &&
        saved.Plan.Count(p => p.Status == "completed") == 1 &&
        saved.Messages.Any(m => m.Content?.Contains("FOCUSED_REVIEW_TOOL_UNAVAILABLE", StringComparison.Ordinal) == true) &&
        result.Message.Contains("SAVED_VISUAL_OBSERVATION", StringComparison.Ordinal),
        "Persistent rejection must surface saved read evidence without authorizing a repeat or inventing completion.");
}

static async Task M3e1MissingProofStaysIncomplete()
{
    using var setup = new TestContext();
    var state = M3e1SavedVision();
    state.Operations.RemoveAt(1);
    await setup.Store.SaveAsync(state, default);
    int calls = 0;
    var model = new FakeModel(_ => {
        if (++calls == 1) throw new AgentException("Model provider HTTP 400 (category=possible_context_limit; messages=11; tools=48; requestBytes=41173).");
        if (calls == 2) return new ModelReply(null, [Call("fake-proof", "nira_plan_update", new { index = 1, status = "completed", evidenceId = "saved-vision" })]);
        return new ModelReply("Cannot verify the image without a receipt.", []);
    });
    var result = await setup.Engine(model).RunAsync(state.Id, null, new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Paused && saved.Plan[1].Status == "pending" && saved.Operations.Count == 1,
        "A real missing observation is never auto-completed by context recovery.");
}

static ConversationState M3e2CompletedVision()
{
    var state = M3e1SavedVision();
    state.Plan[1].Status = "completed";
    state.Plan[1].EvidenceId = "saved-vision";
    state.Plan[2].Status = "completed";
    state.Status = ConversationStatus.Paused;
    state.RetryBlockCode = "NO_PROGRESS_FOCUSED";
    state.Operations[1].ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Observed image",
        new { path = "image.png", analysis = "SAVED_VISUAL_OBSERVATION\nThree red circles, two blue squares, one green triangle.", independentlyVerified = false }));
    return state;
}

static async Task M3e2ResumeComplete()
{
    using var setup = new TestContext();
    var state = M3e2CompletedVision();
    await setup.Store.SaveAsync(state, default);
    var approval = new FakeApprovals(false);
    var engine = setup.Engine(new FakeModel(_ => throw new InvalidOperationException("No provider call needed")));
    var result = await engine.RunAsync(state.Id, null, approval);
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Completed && saved.RetryBlockCode is null &&
        saved.Plan.All(step => step.Status == "completed") && approval.Calls == 0 && saved.Operations.Count == 2 &&
        result.Message.Contains("SAVED_VISUAL_OBSERVATION\nThree red circles", StringComparison.Ordinal) &&
        result.Message.Contains("not independently verified", StringComparison.Ordinal) &&
        saved.LastAnswer == result.Message, "All-verified stalled read-only plan must deliver readable saved data without inference.");
    var again = await engine.RunAsync(state.Id, null, approval);
    Assert(again.Status == ConversationStatus.Completed && again.Message == result.Message && approval.Calls == 0,
        "Completed checkpoint retry is idempotent.");
}

static async Task M3e2LiveLastUpdate()
{
    using var setup = new TestContext();
    var state = M3e1SavedVision();
    await setup.Store.SaveAsync(state, default);
    int calls = 0;
    var model = new FakeModel(_ => {
        if (++calls == 1) throw new AgentException("Model provider HTTP 400 (category=possible_context_limit; messages=11; tools=48; requestBytes=41173).");
        if (calls == 2) return new ModelReply(null, [Call("link-vision", "nira_plan_update", new { index = 1, status = "completed", evidenceId = "saved-vision" })]);
        if (calls == 3) return new ModelReply(null, [Call("mark-analysis", "nira_plan_update", new { index = 2, status = "completed" })]);
        throw new InvalidOperationException("No extra inference after 3/3");
    });
    var approval = new FakeApprovals(false);
    var result = await setup.Engine(model).RunAsync(state.Id, null, approval);
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Completed && calls == 3 && approval.Calls == 0 &&
        saved.Operations.Count == 2 && saved.Plan.All(step => step.Status == "completed") &&
        result.Message.Contains("SAVED_VISUAL_OBSERVATION", StringComparison.Ordinal),
        "Reproduce 1/3 to 3/3 in recovery, then deliver without another provider call or operation.");
}

static async Task M3e2MissingProof()
{
    using var setup = new TestContext();
    var state = M3e2CompletedVision();
    state.Plan[1].EvidenceId = "missing-vision";
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel(_ => throw new InvalidOperationException("Blocked checkpoint")))
        .RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "3/3 labels alone cannot substitute for valid proof.");
}

static async Task M3e2Uncertain()
{
    using var setup = new TestContext();
    var state = M3e2CompletedVision();
    state.Operations.Add(new AgentOperation { ToolName = "filesystem.write_text", Status = OperationStatus.Dispatched });
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel(_ => throw new InvalidOperationException("No uncertain replay")))
        .RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.NeedsReconciliation, "Uncertain dispatched effects must still require reconciliation.");
}

static async Task M3e2Denied()
{
    using var setup = new TestContext();
    var state = M3e2CompletedVision();
    state.Operations.Add(new AgentOperation { ToolName = "vision.analyze", Status = OperationStatus.Rejected, ErrorCode = "NOT_AUTHORIZED" });
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel(_ => throw new InvalidOperationException("Denied checkpoint")))
        .RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "A later denial cannot be overridden by earlier completed labels.");
}

static async Task M3e2Incomplete()
{
    using var setup = new TestContext();
    var state = M3e2CompletedVision();
    state.Plan[2].Status = "pending";
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel(_ => throw new InvalidOperationException("Blocked checkpoint")))
        .RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "A genuinely pending analysis must not be auto-completed.");
}

static async Task M3e2NewInstruction()
{
    using var setup = new TestContext();
    var state = M3e2CompletedVision();
    await setup.Store.SaveAsync(state, default);
    int calls = 0;
    var result = await setup.Engine(new FakeModel(messages => {
        calls++;
        Assert(messages.Any(m => m.Role == "user" && m.Content == "Explain more about the arrangement."),
            "New instruction reaches reasoning model.");
        return new ModelReply("The arrangement has three rows.", []);
    })).RunAsync(state.Id, "Explain more about the arrangement.", new FakeApprovals(false));
    Assert(calls == 1 && result.Status == ConversationStatus.Completed &&
        result.Message.Contains("The arrangement has three rows.", StringComparison.Ordinal),
        "New instructions are not swallowed by terminal recovery.");
}


static async Task CatalogStableWithOptionalProviders()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    foreach (bool includeImage in new[] { false, true })
    {
        Func<ImageGenerationProvider?>? resolver = includeImage ? () => null : null;
        var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, imageGenerationResolver: resolver);
        var model = new FakeModel([new ModelReply("Ready.", [])]);
        var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
            enableCapabilityRouting: true, enableAdaptivePlanning: true)
            .RunAsync(Guid.NewGuid(), "Hello", new FakeApprovals(false));
        var active = model.ToolSnapshots.Single();
        Assert(result.Status == ConversationStatus.Completed && active.Count <= 14 &&
            active.Any(t => t.Name == "memory.search") && active.Any(t => t.Name == "filesystem.read_text") && active.Any(t => t.Name == "system.inspect") &&
            active.Any(t => t.Name == "web.search") && active.Any(t => t.Name == "image.generate") == includeImage &&
            model.PromptSnapshots.Single().Any(m => m.Content?.Contains("desktop.application_find", StringComparison.Ordinal) == true),
            "Optional providers and newly registered desktop tools must not evict stable starter capabilities or hide discoverable tools.");
    }
}

static async Task CatalogConfigurablePriorityAndFamily()
{
    using var setup = new TestContext();
    string[] defaults = Enumerable.Range(0, 8).Select(i => "starter.entry" + i).ToArray();
    var tools = defaults.Concat(new[] { "custom.lookup", "custom.inspect", "other.read" })
        .Reverse().Select(name => new GenericRecoveryTool(name, false, _ => ToolReceipt.Ok("Observed"))).ToArray();
    var registry = new ToolRegistry(tools, new[] { "optional.absent" }.Concat(defaults));
    var model = new FakeModel([
        new ModelReply(null, [Call("choose", "nira_select_tools", new { names = new[] { "custom.lookup" } })]),
        new ModelReply("No action needed.", [])]);
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
        enableCapabilityRouting: true).RunAsync(Guid.NewGuid(), "Review options", new FakeApprovals(false));
    var initial = model.ToolSnapshots[0].Take(8).Select(t => t.Name);
    var next = model.ToolSnapshots[1].Take(8).Select(t => t.Name);
    Assert(result.Status == ConversationStatus.Completed && initial.SequenceEqual(defaults) &&
        next.SequenceEqual(new[] { "custom.lookup", "custom.inspect" }.Concat(defaults.Take(6))) &&
        model.ToolSnapshots.All(snapshot => snapshot.Count <= 14) && tools.All(t => t.Calls == 0),
        "Starter order is composition metadata independent of registration order; model-selected capability and family get priority without execution or additional schema slots.");
}

static async Task M3fBoundedCatalog()
{
    using var setup = new TestContext();
    var model = new FakeModel(_ => new ModelReply("Hello", []));
    var result = await setup.Engine(model, routing: true).RunAsync(Guid.NewGuid(), "Hello", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && model.ToolSnapshots.Count == 1 &&
        model.ToolSnapshots[0].Count <= 14 && model.ToolSnapshots[0].Any(t => t.Name == "nira_select_tools") &&
        model.PromptSnapshots[0].Any(m => m.Content?.Contains("productivity.ocr_read", StringComparison.Ordinal) == true) &&
        model.PromptSnapshots[0].Any(m => m.Content?.Contains("browser.", StringComparison.Ordinal) == true),
        "Small active catalog with complete discoverable capability directory.");
}

static async Task M3fSelectExecute()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "route.txt"), "route evidence");
    Guid id = Guid.NewGuid();
    var model = new FakeModel(new ModelReply[] {
        new(null, [Call("select-hash", "nira_select_tools", new { names = new[] { "filesystem.sha256" } })]),
        new(null, [Call("hash-file", "filesystem.sha256", new { path = "route.txt" })]),
        new(null, [Call("select-read", "nira_select_tools", new { names = new[] { "filesystem.read_text" } })]),
        new(null, [Call("read-file", "filesystem.read_text", new { path = "route.txt" })]),
        new("File hash and text obtained.", [])
    });
    var approval = new FakeApprovals(false);
    var result = await setup.Engine(model, routing: true).RunAsync(id, "Read and hash route.txt", approval);
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && saved.Operations.Count == 2 && approval.Calls == 0 &&
        saved.Operations.All(op => op.Status == OperationStatus.Succeeded) &&
        !model.ToolSnapshots[0].Any(t => t.Name == "filesystem.sha256") &&
        model.ToolSnapshots[1].Any(t => t.Name == "filesystem.sha256") &&
        model.ToolSnapshots.All(ts => ts.Count <= 14), "Selected capabilities load exact schemas and create only real execution receipts.");
}

static async Task M3fHiddenRefused()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new FakeModel(new ModelReply[] {
        new(null, [Call("hidden", "filesystem.write_text", new { path = "hidden.txt", content = "bad" })]),
        new(null, [Call("select", "nira_select_tools", new { names = new[] { "filesystem.write_text" } })]),
        new(null, [Call("write", "filesystem.write_text", new { path = "visible.txt", content = "good" })]),
        new("Created visible.txt", [])
    });
    var result = await setup.Engine(model, routing: true).RunAsync(id, "Create visible.txt", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && saved.Operations.Count == 1 &&
        !File.Exists(Path.Combine(setup.Workspace, "hidden.txt")) && File.Exists(Path.Combine(setup.Workspace, "visible.txt")) &&
        saved.Messages.Any(m => m.Content?.Contains("CAPABILITY_NOT_ACTIVE", StringComparison.Ordinal) == true),
        "A hidden call is rejected before operation creation; only later active invocation can execute.");
}

static async Task M9InactiveSchemaRecovery()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new FakeModel(new ModelReply[] {
        new(null, [Call("hidden", "filesystem.write_text", new { path = "discarded.txt", content = "not authorized by schema exposure" })]),
        new(null, [Call("fresh", "filesystem.write_text", new { path = "intended.txt", content = "fresh request" })]),
        new("Created intended.txt", []) });
    var result = await setup.Engine(model, routing: true).RunAsync(id, "Create intended.txt", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && saved.Operations.Count == 1 &&
        !model.ToolSnapshots[0].Any(t => t.Name == "filesystem.write_text") && model.ToolSnapshots[1].Any(t => t.Name == "filesystem.write_text") &&
        !File.Exists(Path.Combine(setup.Workspace, "discarded.txt")) && File.Exists(Path.Combine(setup.Workspace, "intended.txt")),
        "Inactive invocation can expose its schema but cannot create receipts or execute its arguments.");
}
static async Task M3fApproval()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    string outside = Path.Combine(setup.Root, "outside.txt");
    var model = new FakeModel(new ModelReply[] {
        new(null, [Call("select", "nira_select_tools", new { names = new[] { "filesystem.write_text" } })]),
        new(null, [Call("write", "filesystem.write_text", new { path = outside, content = "denied" })])
    });
    var approval = new FakeApprovals(false);
    var result = await setup.Engine(model, routing: true).RunAsync(id, "Create file outside workspace", approval);
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && approval.Calls == 1 && !File.Exists(outside) &&
        saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Rejected,
        "Loading a schema cannot authorize an outside write.");
}

static async Task M3fInvalid()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new FakeModel(new ModelReply[] {
        new(null, [Call("invalid", "nira_select_tools", new { names = new[] { "unknown.tool" } })]),
        new(null, [Call("duplicate", "nira_select_tools", new { names = new[] { "filesystem.write_text", "filesystem.write_text" } })]),
        new(null, [Call("large", "nira_select_tools", new { names = Enumerable.Repeat("filesystem.write_text", 9).ToArray() })]),
        new("No operations executed.", [])
    });
    await setup.Engine(model, routing: true).RunAsync(id, "Check selection", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 0 && model.ToolSnapshots.All(ts => !ts.Any(t => t.Name == "filesystem.write_text")) &&
        saved.Messages.Any(m => m.Content?.Contains("CAPABILITY_UNKNOWN", StringComparison.Ordinal) == true) &&
        saved.Messages.Any(m => m.Content?.Contains("CAPABILITY_COUNT", StringComparison.Ordinal) == true),
        "Unknown, duplicate and oversized selection never load extra capabilities.");
}

static async Task M3fMixed()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new FakeModel(new ModelReply[] {
        new(null, [Call("select", "nira_select_tools", new { names = new[] { "filesystem.write_text" } }),
            Call("write", "filesystem.write_text", new { path = "mixed.txt", content = "bad" })]),
        new("No file created.", [])
    });
    await setup.Engine(model, routing: true).RunAsync(id, "Do not mix selection and execution", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 0 && !File.Exists(Path.Combine(setup.Workspace, "mixed.txt")) &&
        saved.Messages.Any(m => m.Content?.Contains("MIXED_CAPABILITY_SELECTION", StringComparison.Ordinal) == true),
        "Mixed selection/execution response must dispatch nothing.");
}

static async Task M3fLoop()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    int calls = 0;
    var model = new FakeModel(_ => new ModelReply(null, [Call("select-" + (++calls), "nira_select_tools",
        new { names = new[] { "filesystem.sha256" } })]));
    var result = await setup.Engine(model, routing: true).RunAsync(id, "Hash a file", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && calls == 6 && saved.Operations.Count == 0,
        "Repeated schema selection never earns progress credit or loops indefinitely.");
}

static Task M3fCompaction()
{
    var tools = M3e1WideCatalog().Take(8).Append(new ToolDescriptor("nira_select_tools", "Select capabilities", ToolRisk.ReadLocal,
        JsonToolSchema.Schema("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"))).ToArray();
    var messages = new List<ChatEntry> { new("system", "Routing directory"), new("user", "Read a document") };
    for (int i = 0; i < 20; i++) messages.Add(new ChatEntry("assistant", new string('x', 2000)));
    Assert(ModelContextRecovery.TryReduce(messages, tools, out _, out var compact) &&
        compact.Select(t => t.Name).SequenceEqual(tools.Select(t => t.Name)),
        "Context reduction must retain all active routed schemas and selector.");
    return Task.CompletedTask;
}


static async Task M4aAdaptiveDirect()
{
    using var setup = new TestContext();
    var execution = new FakeModel(_ => new ModelReply("Hello", []));
    var model = new PlanningTestModel(new ModelReply("invalid", []), execution);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model, routing: true, adaptive: true)
        .RunAsync(id, "Hello", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && model.PlanCalls == 0 && model.FallbackCalls == 0 &&
        execution.ToolSnapshots.Count == 1 && saved.AutomaticPlanningOutcome == "direct_adaptive" &&
        saved.AutoPlanningAttempted && saved.Plan.Count == 0 && saved.Operations.Count == 0,
        "Adaptive mode must eliminate a separate planning round for a fresh objective.");
}

static async Task M4aAdaptiveBatch()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "alpha.txt"), "Alpha");
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "beta.txt"), "Beta");
    Guid id = Guid.NewGuid();
    var execution = new FakeModel(new ModelReply[] {
        new(null, [Call("read-a", "filesystem.read_text", new { path = "alpha.txt" }),
            Call("read-b", "filesystem.read_text", new { path = "beta.txt" })]),
        new(null, [Call("select-write", "nira_select_tools", new { names = new[] { "filesystem.write_text" } })]),
        new(null, [Call("write", "filesystem.write_text", new { path = "merged.txt", content = "Alpha Beta" })]),
        new(null, [Call("verify", "filesystem.read_text", new { path = "merged.txt" })]),
        new("Merged and verified the two files.", [])
    });
    var model = new PlanningTestModel(AutoPlanChoice("plan", ("Alternative one", "unused", "observation"),
        ("Alternative two", "unused", "observation")), execution);
    var approval = new FakeApprovals(false);
    var result = await setup.Engine(model, routing: true, adaptive: true).RunAsync(id,
        "Combine alpha.txt and beta.txt into a new file and read it back", approval);
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && model.PlanCalls == 0 && model.FallbackCalls == 0 &&
        execution.ToolSnapshots.Count == 5 && saved.Plan.Count == 0 && saved.Operations.Count == 4 &&
        saved.Operations.All(op => op.Status == OperationStatus.Succeeded) && approval.Calls == 0 &&
        File.ReadAllText(Path.Combine(setup.Workspace, "merged.txt")) == "Alpha Beta" &&
        File.ReadAllText(Path.Combine(setup.Workspace, "alpha.txt")) == "Alpha" &&
        File.ReadAllText(Path.Combine(setup.Workspace, "beta.txt")) == "Beta",
        "Independent reads can be batched, with verified creation and separate read-back; no speculative plan.");
}

static async Task M4aAdaptivePlanProof()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var execution = new FakeModel(new ModelReply[] {
        new(null, [Call("plan", "nira_plan", new { steps = new[] {
            new { description = "Create note", successCriterion = "File creation receipt", kind = "action", toolName = "filesystem.write_text" },
            new { description = "Read note back", successCriterion = "Read verifies saved text", kind = "observation", toolName = "filesystem.read_text" }
        } })]),
        new("Done without the requested work.", []), new("Still done.", []), new("Still done.", [])
    });
    var model = new PlanningTestModel(new ModelReply("invalid", []), execution);
    var result = await setup.Engine(model, adaptive: true).RunAsync(id, "Create a note and read it back", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && model.PlanCalls == 0 &&
        saved.AutomaticPlanningOutcome == "planned_adaptive" && saved.Plan.Count == 2 &&
        saved.Plan.All(p => p.Status == "pending") && saved.Operations.Count == 0,
        "An explicit adaptive plan still requires verified execution evidence.");
}

static async Task M4aAdaptivePlanSize()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var steps = Enumerable.Range(0, 12).Select(i => new {
        description = "Hypothetical step " + i, successCriterion = "Read result", kind = "observation", toolName = "filesystem.read_text"
    }).ToArray();
    var model = new FakeModel(new ModelReply[] { new(null, [Call("plan", "nira_plan", new { steps })]),
        new("No work was executed.", []) });
    await setup.Engine(model, adaptive: true).RunAsync(id, "Review hypothetical workflow", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Plan.Count == 0 && saved.Operations.Count == 0 &&
        saved.Messages.Any(m => m.Content?.Contains("PLAN_SIZE", StringComparison.Ordinal) == true),
        "Adaptive mode rejects oversized speculative plans rather than committing to them.");
}

static async Task M4aAdaptiveLegacy()
{
    using var setup = new TestContext();
    var state = M3e1SavedVision();
    state.Status = ConversationStatus.Paused;
    await setup.Store.SaveAsync(state, default);
    var model = new PlanningTestModel(new ModelReply("invalid", []), new FakeModel(_ => new ModelReply("Not verified.", [])));
    var result = await setup.Engine(model, adaptive: true).RunAsync(state.Id, null, new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Paused && model.PlanCalls == 0 &&
        saved.Plan.Count == 3 && saved.Plan[1].Status != "completed" && saved.Operations.Count == 2 &&
        saved.AutomaticPlanningOutcome == state.AutomaticPlanningOutcome,
        "Upgrade must not clear or bypass the existing plan or replay operations.");
}

static async Task M4aAdaptivePartial()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "partial.txt"), "RECORDED_PARTIAL_VALUE");
    Guid id = Guid.NewGuid();
    int rounds = 0;
    var execution = new FakeModel(_ => {
        if (++rounds == 1) return new ModelReply(null, [Call("read", "filesystem.read_text", new { path = "partial.txt" })]);
        throw new AgentException("Model provider HTTP 400 (category=possible_context_limit; messages=11; tools=14; requestBytes=41173).");
    });
    var result = await setup.Engine(execution, routing: true, adaptive: true).RunAsync(id,
        "Read the file and explain its contents", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && rounds == 2 && saved.Operations.Count == 1 &&
        result.Message.Contains("RECORDED_PARTIAL_VALUE", StringComparison.Ordinal),
        "Persistent context rejection delivers partial observed data, not invented completion.");
}

static async Task M4b4ThreadContinuity()
{
    using var setup = new TestContext();
    Guid first = Guid.NewGuid();
    var engine = setup.Engine(new FakeModel(_ => new ModelReply("The project is called Quartz.", [])), adaptive: true);
    await engine.RunAsync(first, "Name the project", new FakeApprovals(false));
    Guid second = await setup.Store.CreateFollowupAsync(first, default);
    var child = (await setup.Store.LoadAsync(second, default))!;
    Assert(child.PreviousTaskId == first && child.ChatThreadId == first && child.Messages.Count == 0 &&
        child.Operations.Count == 0 && child.Plan.Count == 0, "New turn must be isolated from old execution state.");
    var followup = setup.Engine(new FakeModel(messages => {
        Assert(messages.Any(m => m.Role == "system" && m.Content?.Contains("Quartz", StringComparison.Ordinal) == true) &&
            messages.Count(m => m.Role == "user") == 1, "Prior answer is available only as historical context.");
        return new ModelReply("Quartz", []);
    }), adaptive: true);
    var result = await followup.RunAsync(second, "What did we name it?", new FakeApprovals(false));
    var chain = await setup.Store.LoadThreadAsync(second, default);
    var history = await setup.Store.ListRecentAsync(default);
    Assert(result.Status == ConversationStatus.Completed && chain.Count == 2 && chain[0].Id == first &&
        chain[1].Id == second && history.Count == 1 && history[0].ThreadTitle == "Name the project",
        "Followups reopen together under the original title.");
}

static async Task M4b4ThreadIsolation()
{
    using var setup = new TestContext();
    var prior = new ConversationState { Status = ConversationStatus.Completed, Objective = "Old task",
        LastAnswer = "Old result", Messages = [new ChatEntry("user", "Old task"),
        new ChatEntry("tool", "PRIVATE_TOOL_DATA", "call")],
        Operations = [new AgentOperation { Status = OperationStatus.Succeeded, ToolName = "filesystem.write_text" }],
        Plan = [new PlanStep { Status = "completed" }] };
    await setup.Store.SaveAsync(prior, default);
    Guid id = await setup.Store.CreateFollowupAsync(prior.Id, default);
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.PreviousChatContext?.Contains("Old result", StringComparison.Ordinal) == true &&
        saved.PreviousChatContext?.Contains("PRIVATE_TOOL_DATA", StringComparison.Ordinal) == false &&
        saved.Operations.Count == 0 && saved.Plan.Count == 0 && saved.RetryBlockCode is null &&
        !saved.AutoPlanningAttempted, "Historical tool data and receipts cannot become current authorization or proof.");
}

static async Task M4b4IncompleteRejected()
{
    using var setup = new TestContext();
    var state = new ConversationState { Status = ConversationStatus.Paused, Objective = "Unfinished" };
    await setup.Store.SaveAsync(state, default);
    bool rejected = false;
    try { await setup.Store.CreateFollowupAsync(state.Id, default); }
    catch (AgentException) { rejected = true; }
    Assert(rejected && (await setup.Store.ListRecentAsync(default)).Count == 1,
        "An unfinished task cannot be bypassed with a followup link.");
}


static async Task M4dChatPreferences()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    await setup.Engine(new FakeModel(_ => new ModelReply("Original answer", [])), adaptive: true)
        .RunAsync(id, "Original title", new FakeApprovals(false));
    var original = (await setup.Store.LoadAsync(id, default))!;
    Guid child = await setup.Store.CreateFollowupAsync(id, default);
    await setup.Engine(new FakeModel(_ => new ModelReply("Follow-up answer", [])), adaptive: true)
        .RunAsync(child, "Follow-up", new FakeApprovals(false));
    await setup.Store.SetChatPreferencesAsync(id, "Renamed thread", true, default);
    Assert((await setup.Store.ListRecentAsync(default)).Count == 0, "Archived chat hidden");
    var archived = (await new SqliteConversationStore(setup.Database).ListRecentAsync(default, true)).Single();
    Assert(archived.ThreadId == id && archived.Id == child && archived.Archived && archived.ThreadTitle == "Renamed thread", "Metadata persisted per thread");
    await setup.Store.SetChatPreferencesAsync(id, null, false, default);
    Assert((await setup.Store.ListRecentAsync(default)).Single().ThreadTitle == "Renamed thread", "Restore preserves title");
    var chain = await setup.Store.LoadThreadAsync(child, default);
    Assert(chain.Count == 2 && chain[0].Revision == original.Revision && chain[0].LastAnswer == original.LastAnswer && chain[0].Objective == original.Objective,
        "Presentation metadata never rewrites execution journals or original objective");
}

static async Task M8bDirectSaveAndRecall()
{
    using var setup = new TestContext();
    const string note = "The Cedar workshop uses a portable workbench and a manual saw.";
    var registry = new ToolRegistry([new MemorySearchTool(setup.Store), new MemoryRememberTool(setup.Store)]);
    var approvals = new FakeApprovals(true);
    var model = new FakeModel([ModelCall("memory.remember", JsonSerializer.Serialize(new { note, topic = "Cedar workshop" })),
        new ModelReply("Saved the note.", [])]);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true),
        enableAdaptivePlanning: true, memoryContext: setup.Store);
    Guid first = Guid.NewGuid();
    var saved = await engine.RunAsync(first, "Remember: " + note, approvals);
    var state = (await setup.Store.LoadAsync(first, default))!;
    Assert(saved.Status == ConversationStatus.Completed && approvals.Calls == 0 &&
        state.Operations.Count == 1 && state.Operations[0].ToolName == "memory.remember" && state.Operations[0].Status == OperationStatus.Succeeded &&
        (await setup.Store.ListMemoryNotesAsync(default)).Single().Content == note,
        "Development direct save must have a successful receipt and durable note without a confirmation or search loop.");
    var freshStore = new SqliteConversationStore(setup.Database);
    var freshModel = new FakeModel([ModelCall("memory.search", "{\"query\":\"Cedar workshop equipment\"}"), new ModelReply("A portable workbench and a manual saw.", [])]);
    var freshEngine = new NiraEngine(freshModel, new ToolRegistry([new MemorySearchTool(freshStore)]), freshStore,
        new WorkspaceAuthority(setup.Workspace), enableAdaptivePlanning: true, memoryContext: freshStore);
    Guid second = Guid.NewGuid();
    var recalled = await freshEngine.RunAsync(second, "What equipment does my Cedar workshop use?", new FakeApprovals(false));
    var lookup = (await freshStore.LoadAsync(second, default))!;
    Assert(recalled.Status == ConversationStatus.Completed && lookup.PreviousChatContext is null && lookup.Operations.Count == 1 &&
        lookup.Operations[0].Status == OperationStatus.Succeeded && lookup.Operations[0].ResultJson?.Contains(note, StringComparison.Ordinal) == true,
        "Fresh task retrieves the actual persisted note even with automatic recall off, not earlier conversation text.");
    var deniedModel = new FakeModel([ModelCall("memory.remember", "{\"note\":\"A different fact\",\"topic\":\"Other subject\"}")]);
    var deniedEngine = new NiraEngine(deniedModel, registry, setup.Store, new WorkspaceAuthority(setup.Workspace), enableAdaptivePlanning: true);
    var denied = await deniedEngine.RunAsync(Guid.NewGuid(), "Remember a different fact.", new FakeApprovals(false));
    Assert(denied.Status == ConversationStatus.Paused && (await setup.Store.ListMemoryNotesAsync(default)).Count == 1, "Standard-mode creation still requires approval.");
}

static async Task M8bDevelopmentMemoryManagement()
{
    using var setup = new TestContext();
    const string original = "The user's studio uses a wired keyboard.";
    const string revised = "The user's studio uses a wireless keyboard.";
    var saved = await setup.Store.WriteMemoryAsync(original, "explicit-settings-user", new MemoryDetails("fact", "Studio equipment"), default);
    var search = new MemorySearchTool(setup.Store);
    var manage = new MemoryManageTool(setup.Store);
    var remember = new MemoryRememberTool(setup.Store);
    var history = new MemoryHistorySearchTool(setup.Store);
    var dev = new WorkspaceAuthority(setup.Workspace, developmentMode: true);
    var strict = new WorkspaceAuthority(setup.Workspace);
    Assert(dev.IsPreapproved(remember.Descriptor, "persistent-memory") && dev.IsPreapproved(manage.Descriptor, "persistent-memory") &&
        !strict.IsPreapproved(remember.Descriptor, "persistent-memory") && !strict.IsPreapproved(manage.Descriptor, "persistent-memory") &&
        !dev.IsPreapproved(history.Descriptor, "private-conversation-history") &&
        !dev.IsPreapproved(manage.Descriptor, "workspace") &&
        !dev.IsPreapproved(manage.Descriptor with { Name = "memory.future" }, "persistent-memory"),
        "Standing development consent is limited to registered saved-note changes and the exact local target.");
    var registry = new ToolRegistry([search, manage]);
    var broker = new FakeApprovals(false);
    var updateModel = new FakeModel([ModelCall("memory.search", "{\"query\":\"Studio equipment\"}"),
        ModelCall("memory.manage", JsonSerializer.Serialize(new { action = "replace", id = saved.Id, expectedContent = original, replacement = revised })),
        new ModelReply("{\"allow\":true}", []), new ModelReply("I updated my saved note.", [])]);
    var update = await new NiraEngine(updateModel, registry, setup.Store, dev).RunAsync(Guid.NewGuid(),
        "Update your memory: my studio now uses a wireless keyboard.", broker);
    Assert(update.Status == ConversationStatus.Completed && broker.Calls == 0 &&
        (await setup.Store.ListMemoryNotesAsync(default)).Single().Content == revised,
        "Development correction requires no separate prompt but must change the actual note.");
    var deleteModel = new FakeModel([ModelCall("memory.search", "{\"query\":\"Studio equipment\"}"),
        ModelCall("memory.manage", JsonSerializer.Serialize(new { action = "delete", id = saved.Id, expectedContent = revised })),
        new ModelReply("{\"allow\":true}", []), new ModelReply("I removed the saved note.", [])]);
    var deleted = await new NiraEngine(deleteModel, registry, setup.Store, dev).RunAsync(Guid.NewGuid(),
        "Forget the saved note about my studio equipment.", broker);
    Assert(deleted.Status == ConversationStatus.Completed && broker.Calls == 0 &&
        (await setup.Store.ListMemoryNotesAsync(default)).Count == 0,
        "Development deletion requires no separate prompt but must remove the exact note.");
    Assert(updateModel.PromptSnapshots[0].Any(m => m.Role == "system" && m.Content?.Contains("SPEAKER PERSPECTIVE", StringComparison.Ordinal) == true),
        "The live reasoning prompt distinguishes speaker perspective; no response-text substitution is used.");
}

static async Task M8bRelatedNoteGuard()
{
    using var setup = new TestContext();
    const string old = "The Zephyr app uses Rust and a local database.";
    const string updated = "The Zephyr app uses Rust and a remote database.";
    var original = await setup.Store.WriteMemoryAsync(old, "explicit-settings-user", new MemoryDetails("project", "Zephyr", ["Rust"]), default);
    var remember = new MemoryRememberTool(setup.Store);
    var proposed = JsonToolSchema.Schema(JsonSerializer.Serialize(new { note = updated, topic = "Zephyr app" }));
    var state = new ConversationState();
    Assert(remember.MissingEvidence(state, proposed) is null, "A new note proposal needs no preliminary model search; the store still checks actual related records before writing.");
    var search = new MemorySearchTool(setup.Store);
    var query = JsonToolSchema.Schema("{\"query\":\"Zephyr app\"}");
    var found = await search.ExecuteAsync(query, default);
    state.Operations.Add(new AgentOperation { ToolName = "memory.search", Status = OperationStatus.Succeeded,
        ArgumentsJson = query.GetRawText(), ResultJson = JsonSerializer.Serialize(found) });
    Assert(remember.MissingEvidence(state, proposed) is not null, "A related saved record cannot silently be replaced by a new note.");
    var rejected = await remember.ExecuteAsync(proposed, default);
    Assert(!rejected.Success && rejected.ErrorCode == "MEMORY_RELATED_REVIEW" && !rejected.ChangedState &&
        (await setup.Store.ListMemoryNotesAsync(default)).Count == 1, "Transactional guard preserves existing note even without preflight.");
    var forged = JsonToolSchema.Schema(JsonSerializer.Serialize(new { note = updated, topic = "Zephyr app", relatedNoteIds = new[] { Guid.NewGuid().ToString("N") } }));
    Assert(remember.MissingEvidence(state, forged) is not null, "Unobserved related IDs cannot satisfy the review.");
    var distinct = JsonToolSchema.Schema(JsonSerializer.Serialize(new { note = "The Zephyr app has a desktop interface.", topic = "Zephyr app", relatedNoteIds = new[] { original.Id } }));
    Assert(remember.MissingEvidence(state, distinct) is null && (await remember.ExecuteAsync(distinct, default)).Success,
        "An explicitly reviewed distinct fact can coexist with the existing topic.");
}

static async Task M8bCorrectionPreservesMetadata()
{
    using var setup = new TestContext();
    const string original = "The Sable project uses Go and a local database.";
    const string revised = "The Sable project uses Go and a remote database.";
    var saved = await setup.Store.WriteMemoryAsync(original, "explicit-settings-user", new MemoryDetails("project", "Sable", ["Go"], "old excerpt"), default);
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    var model = new FakeModel([
        ModelCall("memory.search", "{\"query\":\"Sable\"}"),
        ModelCall("memory.manage", JsonSerializer.Serialize(new { action = "replace", id = saved.Id, expectedContent = original, replacement = revised })),
        new ModelReply("Updated the project note.", [])]);
    var approvals = new FakeApprovals(true);
    var engine = new NiraEngine(model, new ToolRegistry([new MemorySearchTool(setup.Store), new MemoryManageTool(setup.Store)]), setup.Store,
        new WorkspaceAuthority(setup.Workspace), memoryContext: setup.Store);
    var result = await engine.RunAsync(Guid.NewGuid(), "Update my Sable database to a remote database.", approvals);
    var current = (await setup.Store.ListMemoryNotesAsync(default)).Single();
    Assert(result.Status == ConversationStatus.Completed && approvals.Calls == 1 && current.Id == saved.Id && current.Content == revised &&
        current.Kind == "project" && current.Topic == "Sable" && current.Tags.SequenceEqual(new[] { "Go" }) && current.Metadata?.SourceExcerpt is null,
        "An approved correction retains identity, classification and unaffected facts, without retaining stale provenance.");
    Assert(model.PromptSnapshots[^1].Any(m => m.Role == "system" && m.Content?.Contains(revised, StringComparison.Ordinal) == true) &&
        !model.PromptSnapshots[^1].Any(m => m.Role == "system" && m.Content?.Contains(original, StringComparison.Ordinal) == true),
        "Automatic recall is refreshed after verified correction.");
}

static async Task M8bTopicAndHistory()
{
    using var setup = new TestContext();
    var stored = await setup.Store.WriteMemoryAsync("The Meridian app has a Rust codebase.", "explicit-settings-user", new MemoryDetails("project", "Meridian"), default);
    await setup.Store.WriteMemoryAsync("A different project has a graphical interface.", "explicit-settings-user", new MemoryDetails("project", "Cypress"), default);
    var notes = await setup.Store.FindMemoryAsync("What architecture and framework does Meridian use?", [], 6, default);
    Assert(notes.Count == 1 && notes[0].Id == stored.Id, "A distinctive topic matches even if requested attributes are absent.");
    var oldest = new ConversationState { Status = ConversationStatus.Completed, Messages = [new ChatEntry("user", "Meridian was initially a command line application.")] };
    await setup.Store.SaveAsync(oldest, default);
    await Task.Delay(20);
    var recent = new ConversationState { Status = ConversationStatus.Completed, Messages = [new ChatEntry("user", "Meridian now has a desktop UI.")] };
    await setup.Store.SaveAsync(recent, default);
    var history = new MemoryHistorySearchTool(setup.Store);
    var receipt = await history.ExecuteAsync(JsonToolSchema.Schema("{\"query\":\"Meridian\",\"order\":\"oldest\"}"), default);
    var results = receipt.Data.GetProperty("matches").EnumerateArray().ToArray();
    Assert(results.Length == 2 && results[0].GetProperty("ConversationId").GetGuid() == oldest.Id && results[1].GetProperty("ConversationId").GetGuid() == recent.Id,
        "Chronological history returns original user text, not saved memory summaries.");
}

static async Task M8MemoryMigration()
{
    using var setup = new TestContext();
    string id = await setup.Store.StoreMemoryAsync("Legacy note about astronomy", "explicit-settings-user", default);
    // Simulate the actual pre-M8 table shape while preserving the old DPAPI ciphertext.
    await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + setup.Database))
    {
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "ALTER TABLE memories DROP COLUMN metadata; DROP TABLE memory_preferences; DROP TABLE memory_candidates; DROP TABLE memory_reviewed_tasks;";
        await cmd.ExecuteNonQueryAsync();
    }
    var reopened = new SqliteConversationStore(setup.Database);
    var notes = await reopened.ListMemoryNotesAsync(default);
    Assert(notes.Count == 1 && notes[0].Id == id && notes[0].Content == "Legacy note about astronomy" && notes[0].Kind == "note", "Legacy encrypted note preserved by additive schema migration.");
    Assert(await reopened.GetMemorySettingsAsync(default) == new MemoryRecallSettings(), "Recall and learning must be disabled by default.");
    Assert((await new SqliteConversationStore(setup.Database).ListMemoryNotesAsync(default)).Count == 1, "Repeated initialization must preserve data.");
}

static async Task M8MemoryRankAndDedup()
{
    using var setup = new TestContext();
    var details = new MemoryDetails("project", "Orchid build system", ["CI", "compilation"]);
    var first = await setup.Store.WriteMemoryAsync("Orchid uses a reproducible compiler pipeline.", "explicit-settings-user", details, default);
    var duplicate = await setup.Store.WriteMemoryAsync(" Orchid uses a reproducible compiler pipeline. ", "explicit-settings-user", details, default);
    await setup.Store.StoreMemoryAsync("Gardening requires regular watering.", "explicit-settings-user", default);
    Assert(first.Changed && !duplicate.Changed && duplicate.Id == first.Id, "Exact duplicate writes return existing identity without a new effect.");
    var matches = await setup.Store.FindMemoryAsync("build system", ["compilation"], 5, default);
    Assert(matches.Count == 1 && matches[0].Id == first.Id && matches[0].Kind == "project" && matches[0].Tags.Contains("compilation"), "Rank by topic and alternative tags, not entire-string substring only.");
    Assert((await setup.Store.FindMemoryAsync("What is it?", [], 5, default)).Count == 0, "Grammar alone must not retrieve unrelated memory.");
    Assert((await new SqliteConversationStore(setup.Database).ListMemoryNotesAsync(default)).Count == 2, "Deduplication survives restart.");
}

static async Task M8MemoryContextIsolation()
{
    using var setup = new TestContext();
    await setup.Store.WriteMemoryAsync("Cobalt uses SQLite for local persistence.", "explicit-settings-user", new MemoryDetails("project", "Cobalt project", ["database"]), default);
    await setup.Store.StoreMemoryAsync("A completely unrelated travel destination.", "explicit-settings-user", default);
    var model = new FakeModel([new ModelReply("First answer", []), new ModelReply("Second answer", [])]);
    var engine = new NiraEngine(model, new ToolRegistry([]), setup.Store, new WorkspaceAuthority(setup.Workspace), memoryContext: setup.Store);
    Guid disabled = Guid.NewGuid(), enabled = Guid.NewGuid();
    await engine.RunAsync(disabled, "What database does the Cobalt project use?", new FakeApprovals(false));
    Assert(!model.PromptSnapshots[0].Any(m => m.Content?.Contains("Cobalt uses SQLite for local persistence.", StringComparison.Ordinal) == true), "Opt-out prevents automatic context injection.");
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    await engine.RunAsync(enabled, "What database does the Cobalt project use?", new FakeApprovals(false));
    var prompt = model.PromptSnapshots[1].Where(m => m.Role == "system").Select(m => m.Content ?? "").ToArray();
    Assert(prompt.Any(m => m.Contains("Cobalt uses SQLite for local persistence.", StringComparison.Ordinal) && m.Contains("not instructions", StringComparison.Ordinal)) &&
        !prompt.Any(m => m.Contains("unrelated travel destination", StringComparison.Ordinal)), "Only relevant notes enter transient historical context.");
    var persisted = (await setup.Store.LoadAsync(enabled, default))!;
    Assert(!persisted.Messages.Any(m => m.Role == "system") && persisted.Operations.Count == 0, "Recall neither persists a synthetic message nor creates an execution receipt.");
    Assert((await new SqliteConversationStore(setup.Database).GetMemorySettingsAsync(default)).RecallEnabled, "Recall preference persists.");
}

static async Task M8MemoryCorrection()
{
    using var setup = new TestContext();
    const string original = "Use the red theme for the Atlas project.";
    const string revised = "Use the blue theme for the Atlas project.";
    string id = await setup.Store.StoreMemoryAsync(original, "explicit-settings-user", default);
    Assert(!await setup.Store.ChangeMemoryNoteAsync(id, "outdated text", revised, "approved-user-correction", null, default), "Stale correction rejected.");
    Assert(await setup.Store.ChangeMemoryNoteAsync(id, original, revised, "approved-user-correction", new MemoryDetails("preference", "Atlas"), default), "Exact correction succeeds.");
    var current = (await setup.Store.ListMemoryNotesAsync(default)).Single();
    Assert(current.Id == id && current.Content == revised && current.Kind == "preference", "Correction preserves stable note identity and replaces stale content.");
    Assert(!await setup.Store.ChangeMemoryNoteAsync(id, original, null, "approved-user-correction", null, default), "Stale deletion rejected.");
    Assert(await setup.Store.ChangeMemoryNoteAsync(id, revised, null, "approved-user-correction", null, default), "Exact deletion succeeds.");
    Assert((await setup.Store.FindMemoryAsync("Atlas project", [], 5, default)).Count == 0, "Deleted note is no longer searchable.");
}

static async Task M8MemoryToolEvidence()
{
    using var setup = new TestContext();
    const string note = "The Helix project uses Rust.";
    string id = await setup.Store.StoreMemoryAsync(note, "explicit-settings-user", default);
    string args = JsonSerializer.Serialize(new { action = "replace", id, expectedContent = note, replacement = "The Helix project uses Zig." });
    var tool = new MemoryManageTool(setup.Store);
    var schema = JsonToolSchema.Schema(args);
    Assert(tool.MissingEvidence(new ConversationState(), schema) is not null, "Cannot edit by guessed identity without actual search evidence.");
    var model = new FakeModel([ModelCall("memory.search", "{\"query\":\"Helix project\"}"), ModelCall("memory.manage", args)]);
    var approvals = new FakeApprovals(false);
    var engine = new NiraEngine(model, new ToolRegistry([new MemorySearchTool(setup.Store), tool]), setup.Store, new WorkspaceAuthority(setup.Workspace));
    var result = await engine.RunAsync(Guid.NewGuid(), "Correct the Helix project language to Zig.", approvals);
    Assert(result.Status == ConversationStatus.Paused && approvals.Calls == 1 && (await setup.Store.ListMemoryNotesAsync(default)).Single().Content == note, "Search does not authorize a change in standard mode, and refusal preserves original memory.");
    var remember = new MemoryRememberTool(setup.Store);
    var state = new ConversationState { Messages = [new ChatEntry("user", "I prefer an OLED screen.")] };
    var screenQuery = JsonToolSchema.Schema("{\"query\":\"screen preference\"}");
    var screenMatches = await new MemorySearchTool(setup.Store).ExecuteAsync(screenQuery, default);
    state.Operations.Add(new AgentOperation { ToolName = "memory.search", Status = OperationStatus.Succeeded,
        ArgumentsJson = screenQuery.GetRawText(), ResultJson = JsonSerializer.Serialize(screenMatches) });
    Assert(remember.MissingEvidence(state, JsonToolSchema.Schema("{\"note\":\"Prefers OLED\",\"topic\":\"screen preference\",\"sourceExcerpt\":\"I prefer an OLED screen.\"}")) is null &&
        remember.MissingEvidence(state, JsonToolSchema.Schema("{\"note\":\"Prefers LCD\",\"topic\":\"screen preference\",\"sourceExcerpt\":\"I prefer LCD.\"}")) is not null, "Exact user provenance, not a model-fabricated quote.");
}

static async Task M8c2IntentVeto()
{
    using var setup = new TestContext();
    const string project = "The user's Cedar project uses Kotlin and SQLite.";
    const string preference = "The user prefers SQLite for prototypes.";
    var original = await setup.Store.WriteMemoryAsync(project, "explicit-memory-user", new MemoryDetails("project", "Cedar"), default);
    var saved = await setup.Store.WriteMemoryAsync(preference, "explicit-memory-user", new MemoryDetails("preference", "Prototype database"), default);
    string WrongArgs() => JsonSerializer.Serialize(new { action = "delete", id = original.Id, expectedContent = project, replacement = "", sourceExcerpt = project });
    string CorrectArgs() => JsonSerializer.Serialize(new { action = "delete", id = saved.Id, expectedContent = preference, replacement = "", sourceExcerpt = preference });
    var model = new FakeModel([
        ModelCall("memory.search", "{\"query\":\"SQLite\"}"), ModelCall("memory.manage", WrongArgs()),
        new ModelReply("{\"allow\":false}", []), ModelCall("memory.manage", WrongArgs()),
        ModelCall("memory.manage", CorrectArgs()), new ModelReply("{\"allow\":true}", []),
        new ModelReply("I removed only the saved preference.", [])]);
    var registry = new ToolRegistry([new MemorySearchTool(setup.Store), new MemoryManageTool(setup.Store)]);
    var broker = new FakeApprovals(false);
    Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true))
        .RunAsync(id, "Forget my general preference for a prototype database, but keep my project facts.", broker);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && broker.Calls == 0 &&
        (await setup.Store.ListMemoryNotesAsync(default)).Single().Id == original.Id,
        "Unrelated note survives; only the in-scope preference is removed without a development popup.");
    Assert(state.Operations.Count(o => o.ErrorCode == "INTENT_SCOPE_UNVERIFIED") == 2 &&
        state.Operations.Count(o => o.ToolName == "memory.manage" && o.Status == OperationStatus.Succeeded) == 1 &&
        model.ToolSnapshots.Count(tools => tools.Count == 0) == 2, "Wrong target is not dispatched and repeated proposal does not spend another review.");
}

static async Task M8c3AutomaticLearning()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true, true), default);
    const string original = "I prefer paper maps for hiking.";
    const string changed = "I prefer digital maps for hiking.";
    const string other = "I use a mechanical keyboard.";
    string Decision(string note, string topic, string evidence) => JsonSerializer.Serialize(new[] {
        new { note, kind = "preference", topic, tags = Array.Empty<string>(), sourceExcerpt = evidence } });
    var model = new FakeModel([
        new ModelReply(Decision(original, "Hiking maps", original), []),
        new ModelReply(Decision("I prefer paper  maps for hiking.", "Hiking maps", original), []),
        new ModelReply(Decision(changed, "Hiking maps", changed), []),
        new ModelReply(Decision(other, "Keyboard hardware", "Invented evidence"), [])]);
    var learning = new MemoryLearningService(setup.Store, model);
    Guid first = Guid.Empty;
    foreach (string input in new[] { original, original, changed, other })
    {
        Guid id = Guid.NewGuid();
        if (first == Guid.Empty) first = id;
        await setup.Store.SaveAsync(new ConversationState { Id = id, Status = ConversationStatus.Completed,
            Messages = [new ChatEntry("user", input)] }, default);
        await learning.ObserveCompletedAsync(id, default);
    }
    var reopened = new SqliteConversationStore(setup.Database);
    var saved = (await reopened.ListMemoryNotesAsync(default)).Single();
    Assert(saved.Content == original && saved.Source == "automatic-learning" && saved.Metadata?.SourceConversationId == first,
        "Automatic mode durably saves grounded new facts, not duplicates, potential conflicts or fabricated evidence.");
    Assert((await reopened.ListMemoryCandidatesAsync(default)).Count == 0, "Automatic mode does not require a suggestion approval.");
    Assert((await reopened.RecallAsync("hiking maps", default)).Items.Single().Content == original, "New memory is available after reopening the store.");
    await reopened.SetMemorySettingsAsync(new MemoryRecallSettings(true, false, true), default);
    Assert(!(await reopened.GetMemorySettingsAsync(default)).AutoSaveLearning, "Turning learning off also disables automatic saving.");
    Guid disabled = Guid.NewGuid();
    await reopened.SaveAsync(new ConversationState { Id = disabled, Status = ConversationStatus.Completed,
        Messages = [new ChatEntry("user", other)] }, default);
    await new MemoryLearningService(reopened, model).ObserveCompletedAsync(disabled, default);
    Assert(model.PromptSnapshots.Count == 4 && (await reopened.ListMemoryNotesAsync(default)).Count == 1,
        "Disabled learning performs no additional inference or write.");
}

static async Task M8c3AutomaticLearningMigration()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true), default);
    string id = await setup.Store.StoreMemoryAsync("Previously approved note", "explicit-memory-user", default);
    await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + setup.Database))
    {
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "ALTER TABLE memory_preferences DROP COLUMN auto_save_learning";
        await cmd.ExecuteNonQueryAsync();
    }
    var reopened = new SqliteConversationStore(setup.Database);
    Assert(await reopened.GetMemorySettingsAsync(default) == new MemoryRecallSettings(true, true, false),
        "Existing suggestion-mode consent is not silently expanded to automatic saving.");
    Assert((await reopened.ListMemoryNotesAsync(default)).Single().Id == id, "Existing note preserved by the additive migration.");
    await reopened.SetMemorySettingsAsync(new MemoryRecallSettings(true, true, true), default);
    Assert(await new SqliteConversationStore(setup.Database).GetMemorySettingsAsync(default) == new MemoryRecallSettings(true, true, true),
        "One-time automatic learning consent survives restart.");
}

static async Task M8c2LearningNative()
{
    using var setup = new TestContext();
    const string input = "I prefer offline documentation for embedded work.";
    const string note = "The user prefers offline documentation for embedded work.";
    Guid id = Guid.NewGuid();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true), default);
    await setup.Store.SaveAsync(new ConversationState { Id = id, Status = ConversationStatus.Completed,
        Messages = [new ChatEntry("user", input)] }, default);
    string decision = JsonSerializer.Serialize(new { candidates = new[] { new { note, kind = "preference", topic = "Embedded documentation", tags = new[] { "offline reference", "embedded" }, sourceExcerpt = input } } });
    var model = new PlanningTestModel(ModelCall("nira_review_learning", decision), new FakeModel([]));
    var learning = new MemoryLearningService(setup.Store, model);
    await learning.ObserveCompletedAsync(id, default);
    Assert(model.PlanCalls == 1 && model.DecisionTool?.Name == "nira_review_learning", "A single non-executing structured learning decision is requested.");
    var pending = (await setup.Store.ListMemoryCandidatesAsync(default)).Single();
    Assert((await setup.Store.RecallAsync("embedded documentation", default)).Items.Count == 0, "Pending suggestions cannot be recalled.");
    Assert(await setup.Store.ReviewMemoryCandidateAsync(pending.Id, true, default), "User accepted learning candidate.");
    var fresh = new FakeModel([new ModelReply("You prefer offline documentation for embedded work.", [])]);
    await new NiraEngine(fresh, new ToolRegistry([]), setup.Store, new WorkspaceAuthority(setup.Workspace), memoryContext: setup.Store)
        .RunAsync(Guid.NewGuid(), "What documentation do I prefer for embedded work?", new FakeApprovals(false));
    Assert(fresh.PromptSnapshots.Single().Any(m => m.Role == "system" && m.Content?.Contains(note, StringComparison.Ordinal) == true),
        "Independent conversation receives approved durable memory, not the earlier conversation.");
}

static async Task M8c2LearningSkippedAttempt()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true), default);
    var model = new FakeModel([]);
    var task = new ConversationState { Status = ConversationStatus.Completed, Messages = [
        new ChatEntry("user", "Forget my saved preference."),
        new ChatEntry("assistant", ToolCalls: [new ModelToolCall("invalid", "memory.manage", "{}")])] };
    await setup.Store.SaveAsync(task, default);
    await new MemoryLearningService(setup.Store, model).ObserveCompletedAsync(task.Id, default);
    Assert(model.PromptSnapshots.Count == 0 && (await setup.Store.ListMemoryCandidatesAsync(default)).Count == 0,
        "Rejected schema-level mutation attempts cannot feed the learning review.");
}

static async Task M8cLearningContext()
{
    using var setup = new TestContext();
    const string known = "Peregrine uses a local message queue.";
    const string unrelated = "A separate pottery hobby is unrelated.";
    const string userText = "For Peregrine I use a local message queue.";
    await setup.Store.WriteMemoryAsync(known, "explicit-memory-user", new MemoryDetails("project", "Peregrine"), default);
    await setup.Store.WriteMemoryAsync(unrelated, "explicit-memory-user", new MemoryDetails("fact", "Pottery"), default);
    Guid first = Guid.NewGuid(), second = Guid.NewGuid();
    foreach (Guid id in new[] { first, second })
        await setup.Store.SaveAsync(new ConversationState { Id = id, Status = ConversationStatus.Completed,
            Messages = [new ChatEntry("user", userText)] }, default);
    var model = new FakeModel([new ModelReply("[]", []), new ModelReply("[]", [])]);
    var learning = new MemoryLearningService(setup.Store, model);
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(false, true), default);
    await learning.ObserveCompletedAsync(first, default);
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true), default);
    await learning.ObserveCompletedAsync(second, default);
    Assert(model.PromptSnapshots.Count == 2 && model.ToolSnapshots.All(tools => tools.Count == 0), "Still one tool-free review per eligible task.");
    Assert(!model.PromptSnapshots[0].Any(m => m.Content?.Contains(known, StringComparison.Ordinal) == true), "Learning alone does not transmit approved notes.");
    Assert(model.PromptSnapshots[1].Any(m => m.Content?.Contains(known, StringComparison.Ordinal) == true) &&
        !model.PromptSnapshots[1].Any(m => m.Content?.Contains(unrelated, StringComparison.Ordinal) == true), "Both preferences permit only relevant approved context.");
    Assert((await setup.Store.ListMemoryCandidatesAsync(default)).Count == 0 && (await setup.Store.ListMemoryNotesAsync(default)).Count == 2,
        "Model comparison never changes approved notes.");
}

static async Task M8cLearningDedup()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(false, true), default);
    const string original = "Wren uses local storage.";
    const string opposite = "Wren does not use local storage.";
    const string other = "Wren uses a message queue.";
    var details = new MemoryDetails("project", "Wren");
    PendingMemoryCandidate Candidate(string text) => new(Guid.NewGuid().ToString("N"), Guid.NewGuid(), text, details, DateTimeOffset.UtcNow.ToString("O"));
    await setup.Store.WriteMemoryAsync(original, "explicit-memory-user", details, default);
    var conflict = Candidate(opposite);
    await setup.Store.StageMemoryCandidatesAsync([Candidate("Wren  uses local storage."), conflict, Candidate(other)], default);
    await setup.Store.StageMemoryCandidatesAsync([Candidate("Wren uses a  message queue.")], default);
    var pending = await setup.Store.ListMemoryCandidatesAsync(default);
    Assert(pending.Count == 2 && pending.Any(c => c.Content == opposite) && pending.Any(c => c.Content == other),
        "Formatting duplicates are blocked against approved and pending notes, but meaning-changing negation is preserved for review.");
    Assert(SqliteConversationStore.RelatedLearningNotes(conflict, await setup.Store.ListMemoryNotesAsync(default)).Single().Content == original,
        "A related approved note is available for local comparison.");
    await setup.Store.WriteMemoryAsync("Wren uses a  message queue.", "explicit-memory-user", details, default);
    Assert(await setup.Store.ReviewMemoryCandidateAsync(pending.Single(c => c.Content == other).Id, true, default), "Stale duplicate suggestion can be consumed.");
    Assert((await setup.Store.ListMemoryNotesAsync(default)).Count == 2 && (await setup.Store.ListMemoryCandidatesAsync(default)).Single().Content == opposite,
        "Acceptance rechecks duplicate identity and does not silently merge conflicting facts.");
}

static async Task M8cLearningSkipsExplicitActions()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true), default);
    var model = new FakeModel([]);
    var learning = new MemoryLearningService(setup.Store, model);
    foreach (string name in new[] { "memory.remember", "memory.manage" })
    {
        var task = new ConversationState { Status = ConversationStatus.Completed,
            Messages = [new ChatEntry("user", "Explicitly manage my saved fact.")],
            Operations = [new AgentOperation { ToolName = name, Status = OperationStatus.Succeeded }] };
        await setup.Store.SaveAsync(task, default);
        await learning.ObserveCompletedAsync(task.Id, default);
    }
    Assert(model.PromptSnapshots.Count == 0 && (await setup.Store.ListMemoryCandidatesAsync(default)).Count == 0,
        "Explicit memory operations do not trigger redundant learning or reintroduce removed facts from the same task.");
}

static ModelReply M9ProcedureProposal(int secondIndex = 1) => new(JsonSerializer.Serialize(new {
    learn = true, name = "Inspect and verify a file", purpose = "Inspect a file and verify its hash", inputs = "Current file reference",
    preconditions = "Resolve the current file and authorize access", verification = "Check the fresh hash receipt",
    steps = new[] { new { index = 0, guidance = "Inspect the current file" }, new { index = secondIndex, guidance = "Verify the current file" } }
}), []);

static AgentOperation M9Observation(IAgentTool tool, string value, bool success = true) => new() {
    ToolName = tool.Descriptor.Name, CapabilityContract = CapabilityContractFingerprint.Compute(tool.Descriptor),
    Status = success ? OperationStatus.Succeeded : OperationStatus.Failed,
    ArgumentsJson = JsonSerializer.Serialize(new { target = value }),
    ResultJson = JsonSerializer.Serialize(success ? ToolReceipt.Ok("Observed", new { privateValue = value }) : ToolReceipt.Fail("FAIL", "Failed"))
};

static async Task M9LearnAndReuse()
{
    using var setup = new TestContext();
    var first = new GenericRecoveryTool("test.inspect", false, _ => ToolReceipt.Ok("Inspected"));
    var second = new GenericRecoveryTool("test.verify", false, _ => ToolReceipt.Ok("Verified"));
    ProceduralSkillService? service = null;
    var recall = new SkillRecallTool((id, ct) => service!.RetrieveAsync(id, ct));
    var registry = new ToolRegistry([first, second, recall]);
    var review = new FakeModel([M9ProcedureProposal()]);
    service = new ProceduralSkillService(setup.Store, review, registry);
    var state = new ConversationState { Status = ConversationStatus.Completed, Objective = "Private test objective",
        Operations = [M9Observation(first, "PRIVATE_SOURCE_SENTINEL"), M9Observation(second, "PRIVATE_SOURCE_SENTINEL")] };
    state.Operations.Insert(1, new AgentOperation { ToolName = "test.change", Status = OperationStatus.Rejected, ErrorCode = "INTENT_SCOPE_UNVERIFIED" });
    await setup.Store.SaveAsync(state, default);
    await service.ObserveAsync(state.Id, default, null);
    Assert(review.PromptSnapshots.Count == 0, "Disabled learning must not review tasks.");
    await setup.Store.SetSkillLearningAsync(true, default);
    await service.ObserveAsync(state.Id, default, null);
    await service.ObserveAsync(state.Id, default, null);
    Assert(review.PromptSnapshots.Count == 1 && review.ToolSnapshots.Single().Count == 0, "One non-executing review per source task.");
    Assert(!JsonSerializer.Serialize(review.PromptSnapshots).Contains("PRIVATE_SOURCE_SENTINEL", StringComparison.Ordinal) &&
        !JsonSerializer.Serialize(review.PromptSnapshots).Contains("Private test objective", StringComparison.Ordinal), "Raw source data must not enter procedure learning.");
    var reopened = new SqliteConversationStore(setup.Database);
    var procedure = (await reopened.ListProceduresAsync(default)).Single();
    Assert(procedure.EvidenceIds.SequenceEqual(state.Operations.Where(o => o.Status == OperationStatus.Succeeded).Select(o => o.Id)) && procedure.Version == 1, "Persist exact host evidence and version.");
    Assert((await service.RecallAsync("Inspect and verify a file", default))?.Contains(procedure.Id, StringComparison.Ordinal) == true, "Relevant new request discovers procedure.");
    var model = new FakeModel([new ModelReply(null, [Call("recall", "skill.recall", new { id = procedure.Id })]),
        new ModelReply(null, [Call("inspect", first.Descriptor.Name, new { target = "NEW_VALUE" })]),
        new ModelReply(null, [Call("verify", second.Descriptor.Name, new { target = "NEW_VALUE" })]),
        new ModelReply("Inspected and verified the new file.", [])]);
    var engine = new NiraEngine(model, registry, reopened, new WorkspaceAuthority(setup.Workspace, true), proceduralSkills: service);
    Guid reuse = Guid.NewGuid();
    var answer = await engine.RunAsync(reuse, "Inspect and verify a file", new FakeApprovals(true));
    Assert(answer.Status == ConversationStatus.Completed && first.Calls == 1 && second.Calls == 1, "Skill guidance must use normal executor.");
    var reused = await reopened.LoadAsync(reuse, default);
    Assert(reused is not null && reused.Operations.Where(o => o.ToolName != "skill.recall").All(o => o.ArgumentsJson.Contains("NEW_VALUE", StringComparison.Ordinal)), "Fresh arguments, not replay.");
    Assert((await reopened.ListProceduresAsync(default)).Single().Matches == 1, "Record one matching reuse sequence.");
    await service.ObserveAsync(reuse, default, null);
    Assert((await reopened.ListProceduresAsync(default)).Single().Matches == 1, "Reuse counters are idempotent.");
    await reopened.SetSkillLearningAsync(false, default);
    Assert(await service.RecallAsync("Inspect and verify a file", default) is null && !(await service.RetrieveAsync(procedure.Id, default)).Success, "Opt-out disables recall.");
    await reopened.DeleteProcedureAsync(procedure.Id, default);
    Assert((await reopened.ListProceduresAsync(default)).Count == 0, "Delete learned procedure.");
}

static async Task M9RejectInvalidEvidence()
{
    using var setup = new TestContext();
    await setup.Store.SetSkillLearningAsync(true, default);
    var first = new GenericRecoveryTool("test.inspect", false, _ => ToolReceipt.Ok("Inspected"));
    var second = new GenericRecoveryTool("test.verify", false, _ => ToolReceipt.Ok("Verified"));
    var registry = new ToolRegistry([first, second]);
    var review = new FakeModel([M9ProcedureProposal(9)]);
    var service = new ProceduralSkillService(setup.Store, review, registry);
    var failed = new ConversationState { Status = ConversationStatus.Completed,
        Operations = [M9Observation(first, "a"), M9Observation(second, "b", false)] };
    await setup.Store.SaveAsync(failed, default); await service.ObserveAsync(failed.Id, default, null);
    Assert(review.PromptSnapshots.Count == 0, "Failed operations cannot seed a skill.");
    var stale = new ConversationState { Status = ConversationStatus.Completed,
        Operations = [M9Observation(first, "a"), M9Observation(second, "b")] };
    stale.Operations[0].CapabilityContract = null;
    await setup.Store.SaveAsync(stale, default); await service.ObserveAsync(stale.Id, default, null);
    Assert(review.PromptSnapshots.Count == 0, "Legacy unbound evidence cannot seed a skill.");
    var valid = new ConversationState { Status = ConversationStatus.Completed,
        Operations = [M9Observation(first, "a"), M9Observation(second, "b")] };
    await setup.Store.SaveAsync(valid, default); await service.ObserveAsync(valid.Id, default, null);
    Assert(review.PromptSnapshots.Count == 1 && (await setup.Store.ListProceduresAsync(default)).Count == 0, "Reject model-invented operation index.");
}

static async Task M9Versioning()
{
    using var setup = new TestContext();
    await setup.Store.SetSkillLearningAsync(true, default);
    var first = new GenericRecoveryTool("test.inspect", false, _ => ToolReceipt.Ok("Inspected"));
    var second = new GenericRecoveryTool("test.verify", false, _ => ToolReceipt.Ok("Verified"));
    var review = new FakeModel([M9ProcedureProposal(), M9ProcedureProposal()]);
    var service = new ProceduralSkillService(setup.Store, review, new ToolRegistry([first, second]));
    for (int i = 0; i < 2; i++)
    {
        var source = new ConversationState { Status = ConversationStatus.Completed,
            Operations = [M9Observation(first, i.ToString()), M9Observation(second, i.ToString())] };
        await setup.Store.SaveAsync(source, default); await service.ObserveAsync(source.Id, default, null);
    }
    var p = (await setup.Store.ListProceduresAsync(default)).Single();
    Assert(p.Version == 1 && review.PromptSnapshots.Count == 1, "Same sequence and contract must not create duplicate versions or duplicate inference.");
    var updated = p with { Steps = [p.Steps[0] with { Contract = "changed-contract" }, p.Steps[1]] };
    Assert(await setup.Store.SaveProcedureAsync(updated, default), "Changed dependency creates new version.");
    var latest = (await setup.Store.ListProceduresAsync(default)).Single();
    Assert(latest.Id == p.Id && latest.Version == 2 && !(await service.RetrieveAsync(p.Id, default)).Success, "Old version cannot bypass contract validation.");
    await setup.Store.DeleteProcedureAsync(p.Id, default);
    Assert((await setup.Store.ListProceduresAsync(default)).Count == 0, "Remove all procedure versions.");
}

static async Task M9SkillCannotAuthorize()
{
    using var setup = new TestContext();
    await setup.Store.SetSkillLearningAsync(true, default);
    var first = new GenericRecoveryTool("test.inspect", false, _ => ToolReceipt.Ok("Inspected"));
    var write = new GenericRecoveryTool("test.change", true, _ => ToolReceipt.Ok("Changed", changedState: true));
    ProceduralSkillService? service = null;
    var recall = new SkillRecallTool((id, ct) => service!.RetrieveAsync(id, ct));
    var registry = new ToolRegistry([first, write, recall]);
    service = new ProceduralSkillService(setup.Store, new FakeModel([M9ProcedureProposal()]), registry);
    var source = new ConversationState { Status = ConversationStatus.Completed,
        Operations = [M9Observation(first, "a"), M9Observation(write, "a")] };
    await setup.Store.SaveAsync(source, default); await service.ObserveAsync(source.Id, default, null);
    var p = (await setup.Store.ListProceduresAsync(default)).Single();
    var model = new FakeModel([new ModelReply(null, [Call("recall", "skill.recall", new { id = p.Id })]),
        new ModelReply(null, [Call("write", write.Descriptor.Name, new { target = "different" })])]);
    var approvals = new FakeApprovals(false);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, true), proceduralSkills: service);
    var result = await engine.RunAsync(Guid.NewGuid(), "Inspect and verify a file", approvals);
    Assert(result.Status == ConversationStatus.Paused && approvals.Calls == 1 && write.Calls == 0, "Learned skill cannot grant write authorization.");
    var incompatible = new ProceduralSkillService(setup.Store, new FakeModel([]), new ToolRegistry([first]));
    Assert(!(await incompatible.RetrieveAsync(p.Id, default)).Success, "Missing dependency blocks retrieval.");
}

static async Task M8d3ScopeDenied() => await M8d3ScopeCase("{\"expand\":false}", false);
static async Task M8d3ScopeAllowed() => await M8d3ScopeCase("{\"expand\":true}", true);
static async Task M8d3ScopeInvalid() => await M8d3ScopeCase("{\"expand\":true,\"unexpected\":true}", false);

static async Task M8d3ScopeCase(string decision, bool allowed)
{
    using var setup = new TestContext();
    // No saved notes means no semantic expansion inference before the search.
    const string marker = "unrelated-file-content";
    File.WriteAllText(Path.Combine(setup.Workspace, "fixture.txt"), marker);
    var model = new FakeModel([
        ModelCall("memory.search", "{\"query\":\"Example subject\"}"),
        ModelCall("filesystem.read_text", "{\"path\":\"fixture.txt\"}"),
        new ModelReply(decision, []), new ModelReply(allowed ? marker : "I do not have that saved information.", [])]);
    var registry = new ToolRegistry([new MemorySearchTool(setup.Store), new FileReadTool(new WorkspacePathResolver(setup.Workspace))]);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
        memoryContext: new SemanticMemoryContextProvider(setup.Store, model));
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, allowed ? "Recall the subject and inspect fixture.txt if necessary." : "What did I tell you about my example subject?", new FakeApprovals(false));
    var saved = await setup.Store.LoadAsync(id, default);
    Assert(result.Status == ConversationStatus.Completed && saved is not null, "Complete with actual available evidence.");
    Assert(saved!.Operations.Count(op => op.ToolName == "filesystem.read_text") == (allowed ? 1 : 0), "Unrequested investigation cannot dispatch; explicitly supported source expansion can.");
    Assert(model.PromptSnapshots.Count == 4 && model.ToolSnapshots[2].Count == 0, "One non-executing scope review only when switching source.");
    if (!allowed) Assert(model.ToolSnapshots[3].Count == 0 && !model.PromptSnapshots[3].Any(m => m.Content?.Contains(marker, StringComparison.Ordinal) == true), "Final response cannot read unrelated data.");
}

static string M8d3Proposal(string note, string topic, string evidence, string relation, params string[] ids) =>
    JsonSerializer.Serialize(new[] { new { note, kind = "preference", topic, tags = Array.Empty<string>(), sourceExcerpt = evidence, relation, relatedIds = ids } });

static async Task<Guid> M8d3Learn(SqliteConversationStore store, IChatModel model, string message)
{
    Guid id = Guid.NewGuid();
    await store.SaveAsync(new ConversationState { Id = id, Status = ConversationStatus.Completed, Messages = [new ChatEntry("user", message)] }, default);
    await new MemoryLearningService(store, model).ObserveCompletedAsync(id, default);
    return id;
}

static async Task M8d3VersionedLearning()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true, true), default);
    const string old = "The user prefers printed hiking maps.", topic = "Hiking maps";
    const string evidence = "For hiking maps, I now prefer digital instead of printed maps.";
    const string updated = "The user prefers digital hiking maps.";
    var note = await setup.Store.WriteMemoryAsync(old, "automatic-learning", new MemoryDetails("preference", topic, SourceExcerpt: "I prefer printed hiking maps."), default);
    var model = new FakeModel([new ModelReply(M8d3Proposal(updated, topic, evidence, "supersedes", note.Id), []), new ModelReply("{\"approved\":true}", [])]);
    Guid task = await M8d3Learn(setup.Store, model, evidence);
    var reopened = new SqliteConversationStore(setup.Database);
    var current = (await reopened.ListMemoryNotesAsync(default)).Single();
    Assert(current.Id == note.Id && current.Content == updated && current.Metadata?.SourceConversationId == task, "Versioned change retains durable identity and new provenance.");
    var versions = await reopened.ListMemoryVersionsAsync(note.Id, default);
    Assert(versions.Count == 1 && versions[0].Prior.Content == old && versions[0].Prior.Metadata?.SourceExcerpt == "I prefer printed hiking maps.", "Prior provenance survives encrypted history.");
    Assert((await reopened.RecallAsync(topic, default)).Items.Single().Content == updated, "Superseded version cannot enter recall.");
    Assert(model.PromptSnapshots.Count == 2 && model.ToolSnapshots.All(tools => tools.Count == 0), "One proposal and one independent non-executing review.");
    Assert(await reopened.RemoveMemoryNoteAsync(note.Id, default) && (await reopened.ListMemoryVersionsAsync(note.Id, default)).Count == 0, "Deleting a note also removes its automatic versions.");
}

static async Task M8d3ComplementaryLearning()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true, true), default);
    const string topic = "Hiking maps", prior = "The user prefers printed hiking maps.", evidence = "I prefer hiking maps with contour lines.";
    const string complement = "The user prefers hiking maps with contour lines.";
    var note = await setup.Store.WriteMemoryAsync(prior, "explicit-memory-user", new MemoryDetails("preference", topic), default);
    var model = new FakeModel([new ModelReply(M8d3Proposal(complement, topic, evidence, "complementary", note.Id), []), new ModelReply("{\"approved\":true}", []), new ModelReply("[]", [])]);
    await M8d3Learn(setup.Store, model, evidence);
    await M8d3Learn(setup.Store, model, "For hiking I like maps that show contour lines.");
    var notes = await setup.Store.ListMemoryNotesAsync(default);
    Assert(notes.Count == 2 && notes.Any(n => n.Id == note.Id && n.Content == prior) && notes.Any(n => n.Content == complement), "Distinct related knowledge is added without modifying manual notes or duplicating equivalent facts.");
    Assert((await setup.Store.ListMemoryVersionsAsync(note.Id, default)).Count == 0, "Complementary learning never creates a replacement version.");
}

static async Task M8d3ProtectedLearning()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true, true), default);
    const string topic = "Hiking maps", prior = "The user prefers printed hiking maps.", evidence = "For hiking maps I now prefer digital instead.";
    const string updated = "The user prefers digital hiking maps.";
    var note = await setup.Store.WriteMemoryAsync(prior, "explicit-memory-user", new MemoryDetails("preference", topic), default);
    var model = new FakeModel([new ModelReply(M8d3Proposal(updated, topic, evidence, "supersedes", note.Id), [])]);
    await M8d3Learn(setup.Store, model, evidence);
    Assert((await setup.Store.ListMemoryNotesAsync(default)).Single().Content == prior && model.PromptSnapshots.Count == 1, "Manual notes cannot be automatically rewritten even when proposed.");
    await setup.Store.RemoveMemoryNoteAsync(note.Id, default);
    var automatic = await setup.Store.WriteMemoryAsync(prior, "automatic-learning", new MemoryDetails("preference", topic), default);
    var rejected = new FakeModel([new ModelReply(M8d3Proposal(updated, topic, evidence, "supersedes", automatic.Id), []), new ModelReply("{\"approved\":false}", [])]);
    await M8d3Learn(setup.Store, rejected, evidence);
    Assert((await setup.Store.ListMemoryNotesAsync(default)).Single().Content == prior && (await setup.Store.ListMemoryVersionsAsync(automatic.Id, default)).Count == 0, "Independent veto leaves existing automatic note untouched.");
}

static async Task M8d3StaleLearning()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true, true), default);
    const string topic = "Hiking maps", prior = "The user prefers printed hiking maps.", evidence = "For hiking maps I now prefer digital instead.";
    var note = await setup.Store.WriteMemoryAsync(prior, "automatic-learning", new MemoryDetails("preference", topic), default);
    int calls = 0;
    var model = new FakeModel(_ => {
        if (++calls == 1) return new ModelReply(M8d3Proposal("The user prefers digital hiking maps.", topic, evidence, "supersedes", note.Id), []);
        Assert(setup.Store.ChangeMemoryNoteAsync(note.Id, prior, "The user prefers a compass.", "explicit-memory-user", new MemoryDetails("preference", topic), default).GetAwaiter().GetResult(), "Simulate user editing during review.");
        return new ModelReply("{\"approved\":true}", []);
    });
    await M8d3Learn(setup.Store, model, evidence);
    Assert((await setup.Store.ListMemoryNotesAsync(default)).Single().Content == "The user prefers a compass." && (await setup.Store.ListMemoryVersionsAsync(note.Id, default)).Count == 0, "Stale review cannot replace concurrent manual correction.");
}

static async Task M8d3SingleField()
{
    using var setup = new TestContext();
    var model = new FakeModel([ModelCall("nira_ask_user", "{\"question\":\"Which format?\",\"fields\":[\"Format\"]}")]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Ask which output format I want.", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser && result.Message == "Which format?", "Single optional field uses normal single question rather than a rejected form.");
}

static async Task M8d2Associations()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    const string a = "The Acorn workspace uses a relational store.";
    const string b = "The Acorn workspace builds with Gradle.";
    var first = await setup.Store.WriteMemoryAsync(a, "explicit-settings-user", new MemoryDetails("project", "Acorn workspace"), default);
    var duplicate = await setup.Store.WriteMemoryAsync("The Acorn workspace uses a  relational store.", "explicit-settings-user", new MemoryDetails("project", "Acorn workspace"), default);
    Assert(!duplicate.Changed && duplicate.Id == first.Id, "Whitespace-only duplicates keep the original durable note.");
    await setup.Store.WriteMemoryAsync(b, "explicit-settings-user", new MemoryDetails("project", "Acorn workspace"), default);
    await setup.Store.WriteMemoryAsync("The Beech workspace uses a separate store.", "explicit-settings-user", new MemoryDetails("project", "Beech workspace"), default);
    var result = await setup.Store.RecallAsync("relational store", default);
    Assert(result.Items.Count == 2 && result.Items.Any(n => n.Content == a) && result.Items.Any(n => n.Content == b),
        "Recall includes complementary facts from the exact same subject, not another workspace.");
    Assert((await setup.Store.ListMemoryNotesAsync(default)).Count == 3, "No consolidation deletes or rewrites original notes.");
}

static async Task M8d2GenericAlias()
{
    using var setup = new TestContext();
    await setup.Store.WriteMemoryAsync("Acorn project uses a relational database.", "explicit-settings-user", new MemoryDetails("project", "Acorn project"), default);
    Assert((await setup.Store.FindMemoryAsync("Beech", ["project"], 5, default)).Count == 0,
        "Generic fallback must not substitute a different project for a named subject.");
    Assert((await setup.Store.FindMemoryAsync("Acorn", ["project"], 5, default)).Count == 1,
        "Specific subject still matches.");
}

static async Task M8d2PartialDomain()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    const string note = "The user prefers printed electronics manuals.";
    await setup.Store.WriteMemoryAsync(note, "explicit-settings-user", new MemoryDetails("preference", "Electronics manuals"), default);
    await setup.Store.WriteMemoryAsync("The user likes historical novels.", "explicit-settings-user", new MemoryDetails("preference", "Novels"), default);
    var model = new FakeModel([new ModelReply("""{"anchor":"","phrases":["electronics manuals documentation"]}""", [])]);
    var context = await new SemanticMemoryContextProvider(setup.Store, model).RecallAsync("How do I learn circuit design?", default);
    Assert(context.Items.Single().Content == note && context.SelectionMethod == "semantic", "Bounded domain overlap does not require the exact model-generated wording.");
}

static async Task M8d1SemanticRecall()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    const string note = "The user prefers visual diagrams for complex software architecture.";
    const string unrelated = "The user likes pottery on weekends.";
    await setup.Store.WriteMemoryAsync(note, "explicit-memory-user", new MemoryDetails("preference", "Software architecture"), default);
    await setup.Store.WriteMemoryAsync(unrelated, "explicit-memory-user", new MemoryDetails("preference", "Pottery"), default);
    const string query = "Explain how distributed services communicate.";
    var model = new FakeModel([new ModelReply("{\"anchor\":\"\",\"phrases\":[\"software architecture diagrams\"]}", []), new ModelReply("Here is an explanation.", [])]);
    var context = new SemanticMemoryContextProvider(setup.Store, model);
    var result = await new NiraEngine(model, new ToolRegistry([]), setup.Store, new WorkspaceAuthority(setup.Workspace), memoryContext: context)
        .RunAsync(Guid.NewGuid(), query, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && model.PromptSnapshots.Count == 2, "One expansion and one normal answer.");
    Assert(model.ToolSnapshots[0].Count == 0 && model.PromptSnapshots[0].Count == 2 &&
        model.PromptSnapshots[0][1].Content?.Contains(query, StringComparison.Ordinal) == true &&
        !model.PromptSnapshots[0].Any(m => m.Content?.Contains(note, StringComparison.Ordinal) == true || m.Content?.Contains(unrelated, StringComparison.Ordinal) == true),
        "Only current query, never saved notes, enters expansion.");
    Assert(model.PromptSnapshots[1].Any(m => m.Role == "system" && m.Content?.Contains(note, StringComparison.Ordinal) == true) &&
        !model.PromptSnapshots[1].Any(m => m.Content?.Contains(unrelated, StringComparison.Ordinal) == true), "Only locally matched note enters final inference.");
    Assert((await setup.Store.ListMemoryNotesAsync(default)).Count == 2, "Recall does not mutate notes.");
}

static async Task M8d1RecallFastPath()
{
    using var setup = new TestContext();
    var model = new FakeModel([]);
    var context = new SemanticMemoryContextProvider(setup.Store, model);
    Assert((await context.RecallAsync("Orchid", default)).Items.Count == 0, "Opt-out.");
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    Assert((await context.RecallAsync("Orchid", default)).Items.Count == 0, "Empty store.");
    await setup.Store.WriteMemoryAsync("Orchid uses a message queue.", "explicit-memory-user", new MemoryDetails("project", "Orchid"), default);
    var match = await context.RecallAsync("Orchid", default);
    Assert(match.Items.Count == 1 && match.SelectionMethod == "local" && model.PromptSnapshots.Count == 0, "Existing exact matches do not spend an extra model call.");
}

static async Task M8d1RecallIsolation()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    await setup.Store.WriteMemoryAsync("Orchid uses a relational database.", "explicit-memory-user", new MemoryDetails("project", "Orchid"), default);
    const string query = "What storage does Quasar use?";
    var model = new FakeModel([
        new ModelReply("{\"anchor\":\"Quasar\",\"phrases\":[\"Quasar relational database\"]}", []),
        new ModelReply("{\"anchor\":\"Orchid\",\"phrases\":[\"Orchid relational database\"]}", []),
        new ModelReply("{\"anchor\":\"Quasar\",\"phrases\":[\"relational database\"]}", []),
        new ModelReply("{\"anchor\":\"\",\"phrases\":[\"project details\"]}", []),
        new ModelReply("{\"anchor\":\"\",\"phrases\":[\"relational database\"],\"extra\":true}", []),
        new ModelReply("{\"anchor\":\"\",\"phrases\":[\"relational database\"]}", []),
        ModelCall("memory.manage", "{}"), new ModelReply("not json", [])]);
    var context = new SemanticMemoryContextProvider(setup.Store, model);
    for (int i = 0; i < 8; i++)
        Assert((await context.RecallAsync(query, default)).Items.Count == 0, "Invalid or unrelated expansion must not recall a note.");
    Assert(model.PromptSnapshots.Count == 8 && (await setup.Store.ListMemoryNotesAsync(default)).Count == 1, "One decision per miss; no mutation.");
}

static async Task M8d1NativeDecision()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    const string note = "The user prefers printed manuals for electronics.";
    await setup.Store.WriteMemoryAsync(note, "explicit-memory-user", new MemoryDetails("preference", "Electronics references"), default);
    var execution = new FakeModel([]);
    var model = new PlanningTestModel(ModelCall("nira_expand_memory_query", "{\"anchor\":\"\",\"phrases\":[\"printed manuals\"]}"), execution);
    var context = await new SemanticMemoryContextProvider(setup.Store, model).RecallAsync("How can I learn circuit design?", default);
    Assert(context.SelectionMethod == "semantic" && context.Items.Single().Content == note && model.PlanCalls == 1 &&
        model.DecisionTool?.Name == "nira_expand_memory_query" && execution.PromptSnapshots.Count == 0,
        "Native non-executing decision selects real stored memory; no execution model call or mutation.");
}

static async Task M8d1RecallFreshness()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    const string note = "The user prefers physical trail maps.";
    var saved = await setup.Store.WriteMemoryAsync(note, "explicit-memory-user", new MemoryDetails("preference", "Trail maps"), default);
    const string decision = "{\"anchor\":\"\",\"phrases\":[\"physical trail maps\"]}";
    var model = new FakeModel(_ => {
        Assert(setup.Store.ChangeMemoryNoteAsync(saved.Id, note, null, "explicit-memory-user", null, default).GetAwaiter().GetResult(), "Delete during expansion.");
        return new ModelReply(decision, []);
    });
    Assert((await new SemanticMemoryContextProvider(setup.Store, model).RecallAsync("How should I navigate outdoors?", default)).Items.Count == 0,
        "Deleted note cannot be recalled from the old snapshot.");
    await setup.Store.WriteMemoryAsync(note, "explicit-memory-user", new MemoryDetails("preference", "Trail maps"), default);
    var optedOut = new FakeModel(_ => {
        setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(false, false), default).GetAwaiter().GetResult();
        return new ModelReply(decision, []);
    });
    var result = await new SemanticMemoryContextProvider(setup.Store, optedOut).RecallAsync("How should I navigate outdoors?", default);
    Assert(!result.Settings.RecallEnabled && result.Items.Count == 0, "Recall opt-out is rechecked before delivery.");
}

static async Task M8cRecallAliases()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, false), default);
    var note = await setup.Store.WriteMemoryAsync("Lark uses a relational data store.", "explicit-memory-user",
        new MemoryDetails("project", "Lark project", ["SQL database", "persistence"]), default);
    await setup.Store.WriteMemoryAsync("Finch uses a different architecture.", "explicit-memory-user",
        new MemoryDetails("project", "Finch project"), default);
    Assert((await setup.Store.RecallAsync("project", default)).Items.Count == 0 &&
        (await setup.Store.RecallAsync("project details", default)).Items.Count == 0, "Generic shared context alone cannot automatically disclose saved notes.");
    var matches = await setup.Store.FindMemoryAsync("Lark persistence", ["SQL database"], 5, default);
    Assert(matches.Count == 1 && matches[0].Id == note.Id, "Subject-specific aliases and tags support explicit contextual lookup.");
}

static async Task M8MemoryLearningConsent()
{
    using var setup = new TestContext();
    const string userText = "For my Juniper project I always use a dark editor theme.";
    Guid id = Guid.NewGuid();
    await setup.Store.SaveAsync(new ConversationState { Id = id, Objective = userText, Status = ConversationStatus.Completed,
        Messages = [new ChatEntry("user", userText), new ChatEntry("assistant", "Noted.")] }, default);
    string valid = JsonSerializer.Serialize(new[] { new { note = "Juniper project uses a dark editor theme.", kind = "preference", topic = "Juniper project", tags = new[] { "editor", "theme" }, sourceExcerpt = userText } });
    var model = new FakeModel([new ModelReply(valid, [])]);
    var learning = new MemoryLearningService(setup.Store, model);
    await learning.ObserveCompletedAsync(id, default);
    Assert(model.PromptSnapshots.Count == 0, "Learning disabled means no additional model call.");
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(true, true), default);
    await learning.ObserveCompletedAsync(id, default);
    await learning.ObserveCompletedAsync(id, default);
    Assert(model.ToolSnapshots.Count == 1 && model.ToolSnapshots[0].Count == 0, "One non-executing review per completed task.");
    Assert(!model.PromptSnapshots[0].Any(m => m.Content?.Contains("Noted.", StringComparison.Ordinal) == true), "Assistant output is not evidence for learned user facts.");
    var pending = (await setup.Store.ListMemoryCandidatesAsync(default)).Single();
    Assert(pending.Details.SourceConversationId == id && pending.Details.SourceExcerpt == userText, "Candidate has actual source and task provenance.");
    Assert((await setup.Store.ListMemoryNotesAsync(default)).Count == 0 && (await setup.Store.RecallAsync("Juniper project", default)).Items.Count == 0, "Unapproved candidate cannot be recalled.");
    Assert(await setup.Store.ReviewMemoryCandidateAsync(pending.Id, true, default), "Explicit candidate acceptance.");
    Assert((await setup.Store.ListMemoryCandidatesAsync(default)).Count == 0 && (await setup.Store.RecallAsync("Juniper project", default)).Items.Count == 1, "Acceptance atomically moves candidate into approved recall.");
}

static async Task M8MemoryLearningRejectsUntrusted()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(false, true), default);
    Guid first = Guid.NewGuid(), second = Guid.NewGuid(), third = Guid.NewGuid();
    foreach (Guid id in new[] { first, second, third })
        await setup.Store.SaveAsync(new ConversationState { Id = id, Objective = "I prefer mechanical keyboards.", Status = ConversationStatus.Completed,
            Messages = [new ChatEntry("user", "I prefer mechanical keyboards.")] }, default);
    var model = new FakeModel([
        new ModelReply("[{\"note\":\"Prefers an ergonomic mouse\",\"kind\":\"preference\",\"topic\":\"Hardware\",\"tags\":[],\"sourceExcerpt\":\"I prefer ergonomic mice.\"}]", []),
        ModelCall("memory.remember", "{\"note\":\"untrusted\"}"),
        new ModelReply("[{\"note\":\"Prefers mechanical keyboards.\",\"kind\":\"preference\",\"topic\":\"Hardware\",\"tags\":[],\"sourceExcerpt\":\"I prefer mechanical keyboards.\"}]", [])]);
    var learning = new MemoryLearningService(setup.Store, model);
    await learning.ObserveCompletedAsync(first, default);
    await learning.ObserveCompletedAsync(second, default);
    Assert((await setup.Store.ListMemoryCandidatesAsync(default)).Count == 0, "Fabricated evidence and structured tool calls are not accepted by the learning review.");
    await learning.ObserveCompletedAsync(third, default);
    var pending = (await setup.Store.ListMemoryCandidatesAsync(default)).Single();
    Assert(await setup.Store.ReviewMemoryCandidateAsync(pending.Id, false, default), "Dismiss candidate explicitly.");
    await learning.ObserveCompletedAsync(third, default);
    Assert((await setup.Store.ListMemoryCandidatesAsync(default)).Count == 0 && (await setup.Store.ListMemoryNotesAsync(default)).Count == 0 && model.PromptSnapshots.Count == 3, "Dismissed candidate does not get re-created from the same completed task.");
}

static async Task M8MemoryHistoryScope()
{
    using var setup = new TestContext();
    Guid visible = Guid.NewGuid(), archived = Guid.NewGuid(), attached = Guid.NewGuid();
    foreach (Guid id in new[] { visible, archived, attached })
        await setup.Store.SaveAsync(new ConversationState { Id = id, Objective = "Lunar project", Status = ConversationStatus.Completed,
            Messages = [new ChatEntry("user", "Lunar project uses " + id.ToString("N"), AttachmentPaths: id == attached ? ["private-file"] : null),
                new ChatEntry("assistant", "Lunar project invented answer"), new ChatEntry("tool", "Lunar project private tool data")] }, default);
    await setup.Store.SetChatPreferencesAsync(archived, null, true, default);
    var matches = await setup.Store.SearchUserHistoryAsync("Lunar project", [], default);
    Assert(matches.Count == 1 && matches[0].ConversationId == visible && matches[0].Content.Contains(visible.ToString("N"), StringComparison.Ordinal), "Only user-authored completed, nonarchived, nonattachment text is eligible.");
    var approvals = new FakeApprovals(false);
    var engine = new NiraEngine(new FakeModel([ModelCall("memory.search_history", "{\"query\":\"Lunar project\"}")]),
        new ToolRegistry([new MemoryHistorySearchTool(setup.Store)]), setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true));
    var result = await engine.RunAsync(Guid.NewGuid(), "Find old Lunar project discussions", approvals);
    Assert(result.Status == ConversationStatus.Paused && approvals.Calls == 1, "Private history reading still requires separate approval in development mode.");
}

static async Task M8MemoryLearningIntegration()
{
    using var setup = new TestContext();
    await setup.Store.SetMemorySettingsAsync(new MemoryRecallSettings(false, true), default);
    const string input = "My Aurora project uses a modular architecture.";
    string proposal = JsonSerializer.Serialize(new[] { new { note = input, kind = "project", topic = "Aurora project", tags = new[] { "architecture" }, sourceExcerpt = input } });
    var model = new FakeModel([new ModelReply("Understood.", []), new ModelReply(proposal, [])]);
    var engine = new NiraEngine(model, new ToolRegistry([]), setup.Store, new WorkspaceAuthority(setup.Workspace), memoryContext: setup.Store,
        memoryLearning: new MemoryLearningService(setup.Store, model));
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, input, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.Message == "Understood." && model.PromptSnapshots.Count == 2 &&
        (await setup.Store.ListMemoryCandidatesAsync(default)).Count == 1, "Actual agent completion drives a bounded optional review without altering the delivered answer.");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Status == ConversationStatus.Completed && state.Operations.Count == 0 && state.Messages.All(m => m.Role != "system"), "Learning is not an action receipt or synthetic conversation entry.");
}

static async Task M8MemoryProtectedMetadata()
{
    using var setup = new TestContext();
    const string marker = "UNIQUE_MEMORY_METADATA_7823";
    await setup.Store.WriteMemoryAsync("Protected note", "explicit-settings-user", new MemoryDetails("project", marker, [marker], marker), default);
    await using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + setup.Database);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT metadata FROM memories";
    string encrypted = (string)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("No protected metadata"));
    Assert(!encrypted.Contains(marker, StringComparison.Ordinal) && Convert.FromBase64String(encrypted).Length > 32 &&
        (await setup.Store.ListMemoryNotesAsync(default)).Single().Metadata?.SourceExcerpt == marker, "Memory classification and provenance are DPAPI-protected at rest and round-trip.");
}

static async Task M4dMemoryManagement()
{
    using var setup = new TestContext();
    string keep = await setup.Store.StoreMemoryAsync("Keep this note", "explicit-settings-user", default);
    string remove = await setup.Store.StoreMemoryAsync("Remove this note", "explicit-settings-user", default);
    var notes = await new SqliteConversationStore(setup.Database).ListMemoryNotesAsync(default);
    Assert(notes.Count == 2 && notes.Any(n => n.Id == keep && n.Content == "Keep this note"), "Saved notes round-trip");
    Assert(await setup.Store.RemoveMemoryNoteAsync(remove, default), "Selected note removed");
    Assert(!await setup.Store.RemoveMemoryNoteAsync(remove, default) && !await setup.Store.RemoveMemoryNoteAsync("invalid", default), "Missing and malformed IDs rejected");
    Assert((await setup.Store.ListMemoryNotesAsync(default)).Single().Id == keep && (await setup.Store.SearchMemoryAsync("Remove this note", 10, default)).Count == 0, "Deletion is scoped to selected note and reflected in search");
}

static async Task M4dInvalidPreferences()
{
    using var setup = new TestContext();
    bool rejected = false;
    try { await setup.Store.SetChatPreferencesAsync(Guid.NewGuid(), "Unknown", false, default); }
    catch (AgentException) { rejected = true; }
    Assert(rejected, "Unknown chat cannot acquire metadata");
    rejected = false;
    try { await setup.Store.SetChatPreferencesAsync(Guid.NewGuid(), "bad\ntitle", false, default); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected && (await setup.Store.ListRecentAsync(default, true)).Count == 0, "Invalid title rejected without creating history");
}

static async Task M4cAttachmentPersisted()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "selected.txt");
    await File.WriteAllTextAsync(path, "local bytes not embedded");
    Guid id = Guid.NewGuid();
    var model = new FakeModel(messages => {
        ChatEntry user = messages.Last(m => m.Role == "user");
        Assert(user.Content?.Contains("selected.txt", StringComparison.Ordinal) == true &&
            user.Content?.Contains("local bytes not embedded", StringComparison.Ordinal) == false,
            "Model receives references, not file bytes.");
        return new ModelReply("Attachment noted.", []);
    });
    var result = await setup.Engine(model, adaptive: true).RunAsync(id, "Review this", new FakeApprovals(false),
        attachmentPaths: new[] { path });
    var saved = (await setup.Store.LoadAsync(id, default))!;
    var entry = saved.Messages.Single(m => m.Role == "user");
    Assert(result.Status == ConversationStatus.Completed && entry.UserDisplayText == "Review this" &&
        entry.AttachmentPaths?.SequenceEqual(new[] { path }) == true && saved.Operations.Count == 0,
        "Attachment metadata survives encrypted storage without creating an operation or approval.");
    Guid child = await setup.Store.CreateFollowupAsync(id, default);
    var thread = await setup.Store.LoadThreadAsync(child, default);
    Assert(thread[0].Messages.Single(m => m.Role == "user").AttachmentPaths?.Single() == path,
        "Reopened chat retains attachment metadata.");
}

static async Task M4cAttachmentScope()
{
    using var setup = new TestContext();
    string outside = Path.Combine(setup.Root, "external.txt");
    await File.WriteAllTextAsync(outside, "private");
    Guid id = Guid.NewGuid();
    var model = new FakeModel(_ => new ModelReply(null, [Call("read-outside", "filesystem.read_text", new { path = outside })]));
    var approval = new FakeApprovals(false);
    var result = await setup.Engine(model, adaptive: true).RunAsync(id, "Read attachment", approval,
        attachmentPaths: new[] { outside });
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && approval.Calls == 1 &&
        state.Operations.Single().Status == OperationStatus.Rejected && state.Operations.Single().ResultJson is null,
        "Selection cannot authorize external reads.");
}

static Task M4cAttachmentBounds()
{
    string path = Path.GetFullPath("selected.txt");
    ChatEntry entry = ChatAttachmentMessage.Create(null, new[] { path, path });
    Assert(entry.AttachmentPaths?.Count == 1 && entry.UserDisplayText == "" &&
        entry.Content?.Contains("Examine the attached files", StringComparison.Ordinal) == true,
        "Attachment-only messages work and duplicate paths are removed.");
    foreach (string[] rejected in new[] { new[] { "relative.txt" }, Enumerable.Repeat(path, 9).ToArray() })
    {
        bool blocked = false;
        try { _ = ChatAttachmentMessage.Create("test", rejected); }
        catch (ArgumentException) { blocked = true; }
        Assert(blocked, "Invalid attachment paths or oversized selections are rejected.");
    }
    return Task.CompletedTask;
}

static Task M4cReceiptOnly()
{
    string path = Path.GetFullPath("verified.xlsx");
    string hash = new string('a', 64);
    var state = new ConversationState { LastAnswer = "I created a fake document." };
    state.Operations.Add(new AgentOperation { Status = OperationStatus.Succeeded, ToolName = "productivity.create_workbook",
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Saved", new { path, sha256 = hash, contentVerified = true }, true)) });
    state.Operations.Add(new AgentOperation { Status = OperationStatus.Failed, ToolName = "productivity.create_document",
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Fake", new { path = Path.GetFullPath("failed.docx"), sha256 = hash, contentVerified = true }, true)) });
    state.Operations.Add(new AgentOperation { Status = OperationStatus.Succeeded, ToolName = "filesystem.read_text",
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Read", new { path = Path.GetFullPath("read.txt"), sha256 = hash }, false)) });
    state.Operations.Add(new AgentOperation { Status = OperationStatus.Succeeded, ToolName = "productivity.create_document",
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Unverified", new { path = Path.GetFullPath("unverified.docx"), sha256 = hash, contentVerified = false }, true)) });
    state.Operations.Add(new AgentOperation { Status = OperationStatus.Succeeded, ToolName = "filesystem.write_text",
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Executable", new { path = Path.GetFullPath("script.cmd"), sha256 = hash }, true)) });
    state.Operations.Add(new AgentOperation { Status = OperationStatus.Succeeded, ToolName = "productivity.pdf_export_text",
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Export", new { outputPath = Path.GetFullPath("export.txt"), outputSha256 = hash, contentVerified = true }, true)) });
    var files = RecordedFileArtifacts.From(state);
    Assert(files.Count == 2 && files[0].Path == path && files[1].Path.EndsWith("export.txt", StringComparison.Ordinal),
        "Only supported verified changed-state file receipts produce cards.");
    return Task.CompletedTask;
}

static async Task M4cChangedFile()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "report.txt");
    await File.WriteAllTextAsync(path, "original");
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path)));
    var artifact = new RecordedFileArtifact(path, hash, "filesystem.write_text");
    Assert(await RecordedFileArtifacts.MatchesAsync(artifact, setup.Workspace), "Recorded bytes match.");
    await File.WriteAllTextAsync(path, "changed");
    Assert(!await RecordedFileArtifacts.MatchesAsync(artifact, setup.Workspace), "Changed files do not open as verified artifacts.");
    File.Delete(path);
    Assert(!await RecordedFileArtifacts.MatchesAsync(artifact, setup.Workspace), "Missing file is rejected.");
}

static string M4b3Sse(params string[] events) => string.Concat(events.Select(e => "data: " + e + "\n\n"));

static async Task M4b3Visible()
{
    using var handler = new M4b3StreamHandler(M4b3Sse(
        """{"choices":[{"delta":{"reasoning_content":"PRIVATE","content":"Hello "},"finish_reason":null}]}""",
        """{"choices":[{"delta":{"content":"world"},"finish_reason":null}]}""",
        """{"choices":[{"delta":{},"finish_reason":"stop"}]}""", "[DONE]"));
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var preview = new List<string?>();
    var reply = await model.CompleteStreamingAsync([new ChatEntry("user", "Hello")], [], preview.Add, default);
    Assert(reply.Content == "Hello world" && reply.ToolCalls.Count == 0 &&
        preview.SequenceEqual(new string?[] { null, "Hello ", "world" }) && handler.Calls == 1 &&
        handler.StreamRequested, "Streaming must preserve visible deltas only, with one request.");
}

static async Task M8d2EmptyFields()
{
    using var setup = new TestContext();
    var model = new FakeModel([ModelCall("nira_ask_user", """{"question":"Which destination?","fields":[]}""")]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Help choose a destination.", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser && result.Message.Contains("Which destination?", StringComparison.Ordinal) &&
        model.PromptSnapshots.Count == 1, "An empty optional field list is an ordinary single question, not a protocol failure.");
}

static async Task M8d2StreamBeforeFinal()
{
    string first = M4b3Sse("""{"choices":[{"delta":{"content":"Early visible words"},"finish_reason":null}]}""");
    string last = M4b3Sse("""{"choices":[{"delta":{"content":" and final words"},"finish_reason":null}]}""",
        """{"choices":[{"delta":{},"finish_reason":"stop"}]}""", "[DONE]");
    using var body = new M8d2GatedStream(first, last);
    using var http = new HttpClient(new M8d2GatedHandler(body));
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var early = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var completion = model.CompleteStreamingAsync([new ChatEntry("user", "Explain")], [], delta =>
    {
        if (delta == "Early visible words") early.TrySetResult(true);
    }, deadline.Token);
    try
    {
        await early.Task.WaitAsync(deadline.Token);
        Assert(!completion.IsCompleted, "Visible delta reaches consumer while provider still withholds final response.");
    }
    finally { body.Release(); }
    var reply = await completion;
    Assert(reply.Content == "Early visible words and final words" && reply.DeliveryMode == "event-stream", "Final response is complete and authoritative.");
}

static async Task M8d2StreamParts()
{
    using var handler = new M4b3StreamHandler(M4b3Sse(
        """{"choices":[{"delta":{"content":[{"type":"text","text":"First "}],"tool_calls":[]},"finish_reason":null}]}""",
        """{"choices":[{"delta":{"content":"second","reasoning_content":"PRIVATE","tool_calls":[]},"finish_reason":null}]}""",
        """{"choices":[{"delta":{},"finish_reason":"stop"}]}""", "[DONE]"));
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var preview = new List<string?>();
    var reply = await model.CompleteStreamingAsync([new ChatEntry("user", "Explain")], [], preview.Add, default);
    Assert(reply.Content == "First second" && reply.DeliveryMode == "event-stream" &&
        preview.SequenceEqual(new string?[] { null, "First ", "second" }), "Empty tool arrays cannot erase real visible content; private fields stay hidden.");
}

static async Task M4b3Tool()
{
    using var handler = new M4b3StreamHandler(M4b3Sse(
        """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-a","function":{"name":"filesystem.","arguments":"{\"path\":"}}]},"finish_reason":null}]}""",
        """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"read_text","arguments":"\"test.txt\"}"}}]},"finish_reason":null}]}""",
        """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""", "[DONE]"));
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var preview = new List<string?>();
    var reply = await model.CompleteStreamingAsync([new ChatEntry("user", "Read a file")], [], preview.Add, default);
    Assert(reply.ToolCalls.Count == 1 && reply.ToolCalls[0].Id == "call-a" && reply.ToolCalls[0].Name == "filesystem.read_text" &&
        reply.ToolCalls[0].ArgumentsJson == "{\"path\":\"test.txt\"}" && preview.All(x => x is null),
        "Fragmented native tool call must assemble without exposing arguments as chat text.");
}

static async Task M4b3Incomplete()
{
    using var handler = new M4b3StreamHandler(M4b3Sse(
        """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-a","function":{"name":"filesystem.read_text","arguments":"{}"}}]},"finish_reason":null}]}"""));
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    bool rejected = false;
    try { await model.CompleteStreamingAsync([new ChatEntry("user", "Read")], [], _ => { }, default); }
    catch (AgentException) { rejected = true; }
    Assert(rejected && handler.Calls == 1, "Truncated stream cannot return a dispatchable tool call.");
}

static async Task M4b3JsonFallback()
{
    using var handler = new FakeApiHandler("""{"choices":[{"message":{"content":"Ordinary response"}}]}""");
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var preview = new List<string?>();
    var reply = await model.CompleteStreamingAsync([new ChatEntry("user", "Hello")], [], preview.Add, default);
    Assert(reply.Content == "Ordinary response" && reply.DeliveryMode == "json" && preview.SequenceEqual(new string?[] { null }) && handler.Calls == 1,
        "JSON response fallback must not fake deltas or make another request.");
}

static Task SchemaRejectsUnexpectedProperties()
{
    JsonElement schema = JsonToolSchema.Schema("""
      {"type":"object","required":["path"],"properties":{"path":{"type":"string"}},
       "additionalProperties":false}
      """);
    Assert(!JsonToolSchema.Validate(schema,
        JsonSerializer.SerializeToElement(new { path="abc", unsafeFlag=true }), out _),
        "Unknown property should be rejected");
    Assert(JsonToolSchema.Validate(schema,
        JsonSerializer.SerializeToElement(new { path="abc" }), out _),
        "Valid property should be accepted");
    return Task.CompletedTask;
}

static async Task ClarificationResumes()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("ask1", "nira_ask_user", new { question="Which location should I use?", fields = new[] { "Location", "Purpose" } })]),
        new ModelReply(null, [Call("finish2", "nira_finish", new {
            outcome="answer", answer="I can use the location you provided.", evidence_ids=Array.Empty<string>() })])
    ]);
    var engine = setup.Engine(model);
    Guid id = Guid.NewGuid();
    var first = await engine.RunAsync(id, "Help me with an unspecified destination.", new FakeApprovals(false));
    Assert(first.Status == ConversationStatus.AwaitingUser && first.Message.Contains("- Location") && first.Message.Contains("- Purpose"), "AwaitingUser and structured fields should be persisted");
    var next = await engine.RunAsync(id, "Use my Documents folder.", new FakeApprovals(false));
    Assert(next.Status == ConversationStatus.Completed, "Follow-up should finish same run");
    ConversationState saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Objective == "Help me with an unspecified destination.", "Objective must survive follow-up");
    Assert(saved.Messages.Any(x => x.Role == "user" && x.Content == "Use my Documents folder."), "Follow-up must enter transcript");
    var desktopModel = new FakeModel([
        new ModelReply(null, [Call("ask-form", "nira_ask_user", new { question="Tell me your preferences.", fields = new[] { "Budget", "Resolution" } })]),
        new ModelReply("Thanks for the details.", [])
    ]);
    var desktopEngine = setup.Engine(desktopModel);
    Guid desktopId = Guid.NewGuid();
    var form = await desktopEngine.RunAsync(desktopId, "Help me choose an appropriate configuration.", new FakeApprovals(false), new M5bDesktopProgress());
    var pending = (await setup.Store.LoadAsync(desktopId, default))!;
    Assert(form.Status == ConversationStatus.AwaitingUser && form.Message.Contains("```nira-ui") && form.Message.Contains("Budget") && pending.PendingQuestion == form.Message, "Desktop clarification must persist native form");
    var resumed = await desktopEngine.RunAsync(desktopId, "Budget: 1000; Resolution: 1080p", new FakeApprovals(false), new M5bDesktopProgress());
    Assert(resumed.Status == ConversationStatus.Completed, "Desktop clarification resumes normally");
    var history = (await setup.Store.LoadAsync(desktopId, default))!;
    Assert(history.Messages.Any(m => m.Role == "assistant" && m.Content == form.Message), "Delivered form survives continuation for history replay.");
    var repairModel = new FakeModel([
        new ModelReply("Could you tell me the following details?\n1. Budget\n2. Primary use\n3. Constraints", []),
        new ModelReply(null, [Call("repair-ask", "nira_ask_user", new { question = "Tell me the essentials.", fields = new[] { "Budget", "Primary use", "Constraints" } })])
    ]);
    var repaired = await setup.Engine(repairModel).RunAsync(Guid.NewGuid(), "Help me choose an option.", new FakeApprovals(false), new M5bDesktopProgress());
    Assert(repaired.Status == ConversationStatus.AwaitingUser && repaired.Message.Contains("```nira-ui") && repairModel.ToolSnapshots.Count == 2 &&
        repairModel.ToolSnapshots[1].Select(t => t.Name).SequenceEqual(new[] { "nira_ask_user" }), "Prose questionnaire gets one non-executing structured correction.");
    var ordinaryModel = new FakeModel([new ModelReply("Here are the steps:\n1. Read\n2. Compare", [])]);
    var ordinary = await setup.Engine(ordinaryModel).RunAsync(Guid.NewGuid(), "Explain the procedure.", new FakeApprovals(false), new M5bDesktopProgress());
    Assert(ordinary.Status == ConversationStatus.Completed && ordinaryModel.ToolSnapshots.Count == 1, "Ordinary lists do not trigger clarification repair.");
}

static async Task WorkspaceWrite()
{
    using var setup = new TestContext();
    const string fileName = "research-summary.txt";
    var model = new FakeModel(messages => {
        var observed = messages.LastOrDefault(m => m.Role == "tool" && m.Content?.Contains("receiptId", StringComparison.Ordinal) == true);
        if (observed is null)
            return new ModelReply(null, [Call("write1", "filesystem.write_text", new { path=fileName, content="Grounded text." })]);
        using var result = JsonDocument.Parse(observed.Content!);
        string receipt = result.RootElement.GetProperty("receiptId").GetString()!;
        return new ModelReply(null, [Call("finish1", "nira_finish", new {
            outcome="action", answer="Created and verified the file.", evidence_ids=new[] { receipt } })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create a new text file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Verified write should complete");
    Assert(File.ReadAllText(Path.Combine(setup.Workspace, fileName)) == "Grounded text.", "File contents must match");
    Assert(result.EvidenceIds.Count == 1, "Receipt must appear in result");
}

static async Task ExternalWriteDenied()
{
    using var setup = new TestContext();
    string external = Path.Combine(Path.GetTempPath(), "nira-unapproved-" + Guid.NewGuid().ToString("N") + ".txt");
    var model = new FakeModel([
        new ModelReply(null, [Call("outside1", "filesystem.write_text", new { path=external, content="Denied" })]),
        new ModelReply(null, [Call("finish2", "nira_finish", new {
            outcome="answer", answer="I did not write outside the workspace.", evidence_ids=Array.Empty<string>() })])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Write to this outside path", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused && result.Message.Contains("Not executed", StringComparison.Ordinal),
        "A declined outside write must pause immediately without another model round");
    Assert(!File.Exists(external), "No unauthorized file should exist");
}

static async Task InterruptedWrite()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var state = new ConversationState { Id = id, Objective = "Write important output", Status = ConversationStatus.Running };
    state.Operations.Add(new AgentOperation { Id="pending123", ToolName="filesystem.write_text",
        Status = OperationStatus.Dispatched, ArgumentsJson="{}" });
    await setup.Store.SaveAsync(state, default);
    var engine = setup.Engine(new FakeModel([]));
    var result = await engine.RunAsync(id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.NeedsReconciliation,
        "In-flight action must not replay after restart");
}

static async Task NoOverwrite()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "existing.txt");
    await File.WriteAllTextAsync(path, "ORIGINAL");
    var model = new FakeModel([
        new ModelReply(null, [Call("write1", "filesystem.write_text", new { path="existing.txt", content="OVERWRITE" })]),
        new ModelReply(null, [Call("finish2", "nira_finish", new {
            outcome="answer", answer="The existing file was protected.", evidence_ids=Array.Empty<string>() })])
    ]);
    await setup.Engine(model).RunAsync(Guid.NewGuid(), "Replace file", new FakeApprovals(false));
    Assert(await File.ReadAllTextAsync(path) == "ORIGINAL", "Existing file must not be overwritten");
}

static async Task OptimisticConcurrency()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    await setup.Store.SaveAsync(new ConversationState { Id=id, Objective="one" }, default);
    ConversationState a = (await setup.Store.LoadAsync(id, default))!;
    ConversationState b = (await setup.Store.LoadAsync(id, default))!;
    a.Objective = "changed";
    await setup.Store.SaveAsync(a, default);
    bool threw = false;
    try { await setup.Store.SaveAsync(b, default); }
    catch (AgentException) { threw = true; }
    Assert(threw, "Stale writers must not overwrite state");
}

static async Task InvalidArguments()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("invalid", "filesystem.write_text", new { path="bad.txt" })]),
        new ModelReply(null, [Call("ask", "nira_ask_user", new { question="What content should I write?" })])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Write content", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser, "Invalid shape should allow user recovery");
    Assert(!File.Exists(Path.Combine(setup.Workspace, "bad.txt")), "Malformed call should not write");
}

static async Task PlanPersistence()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan1", "nira_plan", new { steps = new[] {
            new { description="Inspect the environment", successCriterion="Observation recorded" },
            new { description="Verify the result", successCriterion="Actual output checked" }
        } })]),
        new ModelReply(null, [Call("update1", "nira_plan_update", new { index=0, status="completed" })]),
        new ModelReply(null, [Call("ask1", "nira_ask_user", new { question="What should I verify next?" })])
    ]);
    Guid id = Guid.NewGuid();
    var outcome = await setup.Engine(model).RunAsync(id, "Investigate this task", new FakeApprovals(false));
    Assert(outcome.Status == ConversationStatus.AwaitingUser, "Task should await clarification");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Plan.Count == 2 && state.Plan[0].Status == "completed", "Plan steps must be durable");
}

static async Task UnverifiedCompletion()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("finish1", "nira_finish", new {
            outcome="action", answer="I saved the file", evidence_ids=Array.Empty<string>() })]),
        new ModelReply(null, [Call("ask2", "nira_ask_user", new { question="I lack a write receipt; may I proceed?" })])
    ]);
    var outcome = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create a file", new FakeApprovals(false));
    Assert(outcome.Status == ConversationStatus.AwaitingUser, "Unverified effect must not finish as success");
}

static async Task OutsideWriteApproved()
{
    using var setup = new TestContext();
    string externalDir = Path.Combine(setup.Root, "external");
    Directory.CreateDirectory(externalDir);
    string path = Path.Combine(externalDir, "approved.txt");
    var model = new FakeModel(messages => {
        var observed = messages.LastOrDefault(m => m.Role == "tool" && m.Content?.Contains("receiptId", StringComparison.Ordinal) == true);
        if (observed is null)
            return new ModelReply(null, [Call("outside1", "filesystem.write_text", new { path, content="Approved" })]);
        using var result = JsonDocument.Parse(observed.Content!);
        string receipt = result.RootElement.GetProperty("receiptId").GetString()!;
        return new ModelReply(null, [Call("finish2", "nira_finish", new {
            outcome="action", answer="Created approved file", evidence_ids=new[] { receipt } })]);
    });
    var answer = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Write an approved external file", new FakeApprovals(true));
    Assert(answer.Status == ConversationStatus.Completed && File.Exists(path), "Approved external write should be allowed");
}

static async Task ProtectedConversation()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    const string marker = "NIRA_TEST_PRIVATE_PHRASE_88273419";
    await setup.Store.SaveAsync(new ConversationState { Id = id, Objective = marker }, default);

    // SQLite uses WAL and pooled connections. Reading the live .db file directly
    // with File.ReadAllBytesAsync can fail on Windows because SQLite still holds
    // a handle, and a raw file read can miss the latest row in the WAL anyway.
    // Read the persisted ciphertext through SQLite's normal snapshot semantics.
    var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
        DataSource = setup.Database,
        Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString();
    string ciphertext;
    await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM conversations WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        ciphertext = await command.ExecuteScalarAsync() as string
            ?? throw new InvalidOperationException("No stored conversation payload was found.");
    }

    Assert(!ciphertext.Contains(marker, StringComparison.Ordinal),
        "Plaintext objective leaked into persisted SQLite payload");
    byte[] protectedBytes = Convert.FromBase64String(ciphertext);
    Assert(protectedBytes.Length > 32,
        "Persisted conversation does not have a plausible protected payload");
    var recovered = await setup.Store.LoadAsync(id, default);
    Assert(recovered?.Objective == marker, "DPAPI roundtrip failed");
}

static Task WorkspaceDefaultsToAppData()
{
    using var setup = new TestContext();
    string appDataRoot = Path.Combine(setup.Root, "application-data");
    string workspace = NiraWorkspaceLocation.Open(appDataRoot, null);
    Assert(workspace == Path.Combine(appDataRoot, "Workspace"),
        "Default workspace must be app-private, not the Windows Documents folder");
    Assert(Directory.Exists(workspace), "Default workspace must exist");
    Assert(!Directory.EnumerateFileSystemEntries(workspace).Any(),
        "Write-access probe must not leave files behind");
    return Task.CompletedTask;
}

static async Task WorkspaceCustomLocation()
{
    using var setup = new TestContext();
    string custom = Path.Combine(setup.Root, "chosen-workspace");
    Directory.CreateDirectory(custom);
    string existing = Path.Combine(custom, "existing.txt");
    await File.WriteAllTextAsync(existing, "KEEP THIS DATA");
    string selected = NiraWorkspaceLocation.Open(Path.Combine(setup.Root, "appdata"), custom);
    Assert(selected == Path.GetFullPath(custom), "Explicit workspace choice must be respected");
    Assert(await File.ReadAllTextAsync(existing) == "KEEP THIS DATA",
        "Existing workspace content must remain intact");
    Assert(Directory.EnumerateFiles(custom).Count() == 1,
        "Write probe must not leave any temporary files behind");
}

static async Task WorkspaceBlockedLocation()
{
    using var setup = new TestContext();
    string blocked = Path.Combine(setup.Root, "occupied-by-a-file");
    await File.WriteAllTextAsync(blocked, "ORIGINAL");
    bool explained = false;
    try { NiraWorkspaceLocation.Open(Path.Combine(setup.Root, "appdata"), blocked); }
    catch (IOException ex)
    {
        explained = ex.Message.Contains("NIRA_WORKSPACE", StringComparison.Ordinal) &&
            ex.Message.Contains(blocked, StringComparison.OrdinalIgnoreCase);
    }
    Assert(explained, "Unavailable explicit workspace must provide actionable diagnostics");
    Assert(await File.ReadAllTextAsync(blocked) == "ORIGINAL",
        "Rejected workspace must not alter existing data");
}

static Task WorkspaceRelativeOverride()
{
    using var setup = new TestContext();
    bool rejected = false;
    try { NiraWorkspaceLocation.Open(setup.Root, "relative\\not-absolute"); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected, "Relative workspace override must be rejected");
    return Task.CompletedTask;
}

static async Task M6e3Animation()
{
    using var setup = new TestContext();
    const string encoded = "iVBORw0KGgoAAAANSUhEUgAAAgAAAAIAAgMAAACJFjxpAAAADFBMVEX/AAAA/wAAAP///wDWAo97AAABKElEQVR42u3RwQkAMAwCwKQ7ZslO2SF8hML5Vw7sCjNh/9RyAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPqGA+MCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACA3wEPqPIFUwqT7/oAAAAASUVORK5CYII=";
    using var handler = new FakeApiHandler(JsonSerializer.Serialize(new { success = true, result = new { image = encoded } }));
    using var http = new HttpClient(handler);
    var provider = new ImageGenerationProvider(new string('a', 32), new string('T', 32));
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, imageGeneration: provider);
    var args = new { prompt = "A rotating colored square", width = 512, height = 512, animation = true, frameDurationMs = 250 };
    var model = new FakeModel([new ModelReply(null, [Call("gif1", "image.generate", args)]),
        new ModelReply(null, [Call("gif2", "image.generate", args)]), new ModelReply("Here is your animation.", [])]);
    var approvals = new FakeApprovals(true);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Create a looping GIF", approvals);
    Assert(result.Status == ConversationStatus.Completed && handler.Calls == 1 && approvals.Calls == 1 &&
        handler.RequestBody is { } request && request.Contains("2 by 2 animation sprite sheet", StringComparison.Ordinal),
        "GIF must be generated with one approved sprite request.");
    var entries = await new SqliteConversationStore(setup.Database).ListGalleryAsync(default);
    Assert(entries.Count == 1 && entries[0].IsGenerated && entries[0].IsAnimated && entries[0].FrameDurationMs == 250 &&
        entries[0].Path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase), "Animation gallery evidence not retained.");
    byte[] gif = File.ReadAllBytes(entries[0].Path);
    Assert(gif.AsSpan().StartsWith("GIF89a"u8) && gif[6] == 0 && gif[7] == 1 && gif[8] == 0 && gif[9] == 1 &&
        gif[^1] == 0x3b && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(gif)) == entries[0].Sha256 &&
        System.Text.Encoding.ASCII.GetString(gif).Contains("NETSCAPE2.0", StringComparison.Ordinal), "Saved animation structure or hash invalid.");
}

static async Task M6e3AnimationDenied()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("{}");
    using var http = new HttpClient(handler);
    var provider = new ImageGenerationProvider(new string('a', 32), new string('T', 32));
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, imageGeneration: provider);
    var tool = new ImageGenerationTool(new WorkspacePathResolver(setup.Workspace), http, provider);
    bool rejected = false;
    try { _ = tool.GetTarget(JsonSerializer.SerializeToElement(new { prompt = "Blue sky", width = 512, height = 512, animation = true, frameDurationMs = 15 })); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected, "Invalid GIF delay must fail before approval.");
    var model = new FakeModel([new ModelReply(null, [Call("gif1", "image.generate", new { prompt = "Blue sky", width = 512, height = 512, animation = true })])]);
    var approvals = new FakeApprovals(false);
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace))
        .RunAsync(Guid.NewGuid(), "Create a GIF", approvals);
    Assert(result.Status == ConversationStatus.Paused && approvals.Calls == 1 && handler.Calls == 0 &&
        !Directory.Exists(Path.Combine(setup.Workspace, ImageGenerationTool.CacheFolder)), "Denied animation must have no effects.");
}

static async Task M6eGenerationSuccess()
{
    using var setup = new TestContext();
    const string encoded = "iVBORw0KGgoAAAANSUhEUgAAAQAAAAEACAIAAADTED8xAAACv0lEQVR4nO3TQQEAEADAQJSSSUTtxPDYXYJ9Nve5A6rW7wD4yQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSHvAvANZ+OncoAAAAABJRU5ErkJggg==";
    string body = JsonSerializer.Serialize(new { success = true, result = new { image = encoded } });
    using var handler = new FakeApiHandler(body);
    using var http = new HttpClient(handler);
    // Create the runtime catalog before credentials exist, then save them without a restart.
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http,
        imageGenerationResolver: () => ImageGenerationProvider.Load(setup.Root));
    Assert(registry.Descriptors.Any(t => t.Name == "image.generate"), "Generation must remain registered before setup.");
    ImageGenerationProvider.Save(setup.Root, new string('a', 32), new string('T', 32));
    var args = new { prompt = "Blue geometric landscape", width = 256, height = 256, animation = false, frameDurationMs = 0 };
    var model = new FakeModel([new ModelReply(null, [Call("gen1", "image.generate", args)]),
        new ModelReply(null, [Call("gen2", "image.generate", args)]), new ModelReply("Here is your artwork.", [])]);
    var approvals = new FakeApprovals(true);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Create a blue landscape", approvals);
    Assert(result.Status == ConversationStatus.Completed && handler.Calls == 1 && approvals.Calls == 1,
        "Generation must dispatch exactly once and require consent.");
    Assert(handler.AuthorizationHeader == "Bearer " + new string('T', 32) && handler.RequestBody is { } request && request.Contains("Blue geometric landscape", StringComparison.Ordinal), "Provider multipart/auth request incorrect.");
    var state = await setup.Store.LoadAsync(id, default) ?? throw new InvalidOperationException("Task missing.");
    var entries = await new SqliteConversationStore(setup.Database).ListGalleryAsync(default);
    Assert(entries.Count == 1 && entries[0].IsGenerated && !entries[0].IsAnimated && entries[0].FrameDurationMs == 0 && entries[0].Source == "" && File.Exists(entries[0].Path) &&
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entries[0].Path))) == entries[0].Sha256,
        "Generated artwork must persist with a verified hash and no fabricated web source.");
    Assert(state.Operations.Count(o => o.ToolName == "image.generate") == 1 && !JsonSerializer.Serialize(state).Contains(new string('T', 32), StringComparison.Ordinal),
        "Duplicate attempts and credentials must not enter the journal.");
}

static async Task M6eGenerationDenied()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("{}");
    using var http = new HttpClient(handler);
    var provider = new ImageGenerationProvider(new string('a', 32), new string('T', 32));
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, imageGeneration: provider);
    var model = new FakeModel([new ModelReply(null, [Call("gen1", "image.generate", new { prompt = "Blue sky", width = 256, height = 256 })])]);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    var approvals = new FakeApprovals(false);
    var result = await engine.RunAsync(Guid.NewGuid(), "Create artwork", approvals);
    Assert(result.Status == ConversationStatus.Paused && approvals.Calls == 1 && handler.Calls == 0 &&
        !Directory.Exists(Path.Combine(setup.Workspace, ImageGenerationTool.CacheFolder)), "Denied generation must have no network or image effects.");
    Assert(!registry.Validate("image.generate", "{\"prompt\":\"Blue sky\",\"width\":256,\"height\":256,\"endpoint\":\"https://example.com\"}", out _, out _), "Model cannot choose provider URL.");
}

static async Task M6eGenerationInvalid()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("{\"success\":true,\"result\":{\"image\":\"PGh0bWw+PC9odG1sPg==\"}}");
    using var http = new HttpClient(handler);
    var tool = new ImageGenerationTool(new WorkspacePathResolver(setup.Workspace), http,
        new ImageGenerationProvider(new string('a', 32), new string('T', 32)));
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { prompt = "Blue sky", width = 256, height = 256 }), default);
    Assert(!receipt.Success && !receipt.ChangedState && !Directory.Exists(Path.Combine(setup.Workspace, ImageGenerationTool.CacheFolder)), "Invalid image must not be saved.");
}

static Task M6eGenerationCredentials()
{
    using var setup = new TestContext();
    const string token = "TestTokenOnlyNotARealCredential12345";
    ImageGenerationProvider.Save(setup.Root, new string('a', 32), token);
    Assert(ImageGenerationProvider.Load(setup.Root) is not null, "Stored generation credentials did not reopen.");
    var files = Directory.GetFiles(Path.Combine(setup.Root, "Credentials"));
    Assert(files.Length == 1 && !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(files[0])).Contains(token, StringComparison.Ordinal) &&
        new NiraModelCredentialStore(setup.Root, new Uri("https://ollama.com/v1/")).ReadStored() is null, "Image credential must be encrypted and separately scoped.");
    ImageGenerationProvider.Clear(setup.Root);
    Assert(ImageGenerationProvider.Load(setup.Root) is null, "Image credential clear failed.");
    return Task.CompletedTask;
}

static async Task M6e2Readiness()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("{}");
    using var http = new HttpClient(handler);
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http,
        imageGenerationResolver: () => ImageGenerationProvider.Load(setup.Root));
    Assert(registry.TryGet("image.generate", out var tool) && tool is IToolReadiness,
        "Stable generation schema must exist before credentials are configured.");
    var readiness = (IToolReadiness)tool!;
    Assert(readiness.UnavailableReason() is not null, "Unconfigured generation must be detected locally.");
    ImageGenerationProvider.Save(setup.Root, new string('a', 32), new string('T', 32));
    Assert(readiness.UnavailableReason() is null, "Saving must activate the existing registry immediately.");
    ImageGenerationProvider.Clear(setup.Root);
    Assert(readiness.UnavailableReason() is not null, "Clearing must disable provider access immediately.");
    var args = new { prompt = "Blue sky", width = 256, height = 256 };
    var model = new FakeModel([new ModelReply(null, [Call("gen1", "image.generate", args)]),
        new ModelReply(null, [Call("gen2", "image.generate", args)])]);
    var approvals = new FakeApprovals(true);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
        enableCapabilityRouting: true);
    Guid id = Guid.NewGuid();
    var missing = await engine.RunAsync(id, "Create artwork", approvals);
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(missing.Status == ConversationStatus.Paused && missing.Message.Contains("credentials", StringComparison.OrdinalIgnoreCase) &&
        saved.Operations.Count == 0 && approvals.Calls == 0 && handler.Calls == 0 &&
        model.ToolSnapshots[0].Any(t => t.Name == "image.generate"),
        "Missing credentials must not dispatch, consume quota or ask network permission.");
    ImageGenerationProvider.Save(setup.Root, new string('a', 32), new string('T', 32));
    var retry = await engine.RunAsync(id, null, approvals);
    Assert(retry.Status == ConversationStatus.Paused && approvals.Calls == 1 && handler.Calls == 1,
        "The same saved task must be able to reach the provider after setup; malformed fake response must pause.");
}

static async Task M6e2RoutingRecovery() => await M6e2RoutingCase(true);
static async Task M6e2RoutingPause() => await M6e2RoutingCase(false);

static async Task M6e2RoutingCase(bool recover)
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("{}");
    using var http = new HttpClient(handler);
    ImageGenerationProvider.Save(setup.Root, new string('a', 32), new string('T', 32));
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http,
        imageGenerationResolver: () => ImageGenerationProvider.Load(setup.Root));
    const string refusal = "The image generation capability is unavailable in this environment.";
    var model = new FakeModel([new ModelReply(null, [Call("select1", "nira_select_tools", new { names = new[] { "image.generate" } })]),
        new ModelReply(refusal, []), recover
            ? new ModelReply(null, [Call("generate1", "image.generate", new { prompt = "Futuristic city", width = 256, height = 256 })])
            : new ModelReply(refusal, [])]);
    var approvals = new FakeApprovals(false);
    Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
        enableCapabilityRouting: true).RunAsync(id, "Create an image of a city", approvals);
    Assert(result.Status == ConversationStatus.Paused && model.ToolSnapshots.Count == 3 &&
        model.ToolSnapshots.All(ts => ts.Any(t => t.Name == "image.generate")) &&
        model.PromptSnapshots[2].Any(m => m.Role == "system" && m.Content!.Contains("selected a registered action capability", StringComparison.Ordinal)),
        "Selected generation must receive one bounded corrective round rather than false completion.");
    Assert(approvals.Calls == (recover ? 1 : 0) && handler.Calls == 0,
        "Recovery must never bypass approval or dispatch after a refusal.");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == (recover ? 1 : 0) && saved.LastAnswer != refusal,
        "A refusal after selection must not be stored as a completed artwork task.");
}

static async Task M6dGalleryPersistence()
{
    using var setup = new TestContext();
    const string url = "https://upload.wikimedia.org/example.jpg";
    string path = Path.Combine(setup.Workspace, ImagePreviewTool.CacheFolder, "preview.png");
    string hash = new string('A', 64);
    var state = new ConversationState { Objective = "Find photos", Status = ConversationStatus.Completed };
    state.Operations.Add(new AgentOperation { ToolName = "image.search", Status = OperationStatus.Succeeded,
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Found", new { candidates = new[] { new {
            MediaUrl = url, Title = "File:Example.jpg", Author = "Example author", License = "CC BY 4.0", SourceUrl = "https://commons.wikimedia.org/wiki/File:Example.jpg" } } })) });
    state.Operations.Add(new AgentOperation { ToolName = "image.retrieve", Status = OperationStatus.Succeeded,
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Retrieved", new { previews = new[] {
            new { url, path, sha256 = hash }, new { url = url + "?unobserved", path, sha256 = hash } } })) });
    Assert(ImageGalleryEntry.FromTask(state).Count == 1, "Unobserved media must not enter the gallery.");
    await setup.Store.SaveAsync(state, default);
    var reopened = new SqliteConversationStore(setup.Database);
    var entries = await reopened.ListGalleryAsync(default);
    Assert(entries.Count == 1 && entries[0].Title == "Example.jpg" && entries[0].Sha256 == hash &&
        entries[0].Author == "Example author" && entries[0].Path == path && !File.Exists(path),
        "Gallery index persists in the protected journal and does not create or retrieve files.");
    state.Operations[1].Status = OperationStatus.Failed;
    Assert(ImageGalleryEntry.FromTask(state).Count == 0, "Failed retrieval must not enter the gallery.");
}

static Task M6dGalleryUpload()
{
    using var setup = new TestContext();
    string home = Path.Combine(setup.Root, "local-gallery");
    string original = Path.Combine(setup.Root, "original.png");
    byte[] bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1ZkAAAAASUVORK5CYII=");
    File.WriteAllBytes(original, bytes);
    var library = new NIRAGalleryLibraryService(home);
    var item = library.Import(original, NIRAGalleryCategory.Uploaded) ?? throw new InvalidOperationException("Import failed");
    Assert(File.ReadAllBytes(original).SequenceEqual(bytes) && File.ReadAllBytes(library.ResolvePath(item)).SequenceEqual(bytes),
        "Gallery must keep a separate verified copy, not move or alter the user file.");
    var reopened = new NIRAGalleryLibraryService(home);
    Assert(reopened.Items.Count == 1 && reopened.Items[0].Id == item.Id && reopened.Import(original, NIRAGalleryCategory.Uploaded)?.Id == item.Id,
        "Imported images must survive reopening and avoid duplicate copies.");
    File.WriteAllText(original, "not an image");
    Assert(reopened.Import(original, NIRAGalleryCategory.Uploaded) is null && File.ReadAllBytes(reopened.ResolvePath(item)).SequenceEqual(bytes),
        "A modified invalid original must not overwrite the gallery copy.");
    return Task.CompletedTask;
}

static Task M6aFixPreviewBinding()
{
    var state = new ConversationState();
    const string url = "https://upload.wikimedia.org/example.jpg";
    var args = JsonSerializer.SerializeToElement(new { urls = new[] { url } });
    Assert(!ImageEvidenceBinding.Allows(state, args), "Unobserved URL must fail.");
    state.Operations.Add(new AgentOperation { ToolName = "image.search", Status = OperationStatus.Succeeded,
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Found", new { candidates = new[] { new { MediaUrl = url } } })) });
    Assert(ImageEvidenceBinding.Allows(state, args), "Exact successful candidate must be bound.");
    Assert(!ImageEvidenceBinding.Allows(state, JsonSerializer.SerializeToElement(new { urls = new[] { url + "?changed=1" } })), "Modified URL must fail.");
    state.Operations[0].Status = OperationStatus.Failed;
    Assert(!ImageEvidenceBinding.Allows(state, args), "Failed discovery must not authorize retrieval.");
    return Task.CompletedTask;
}

static async Task M6aFixUnobservedPreview()
{
    using var setup = new TestContext();
    var model = new FakeModel([new ModelReply(null, [Call("preview1", "image.retrieve", new { urls = new[] { "https://upload.wikimedia.org/example.jpg" } })]), new ModelReply("No image was retrieved.", [])]);
    var approvals = new FakeApprovals(false);
    Guid id = Guid.NewGuid();
    await setup.Engine(model).RunAsync(id, "Show an image", approvals);
    var state = await setup.Store.LoadAsync(id, default);
    Assert(approvals.Calls == 0 && state is not null && !state.Operations.Any(op => op.ToolName == "image.retrieve"), "Unknown media must be rejected before approval or dispatch.");
}

static async Task M6aFixPreviewApproval()
{
    using var setup = new TestContext();
    const string url = "https://upload.wikimedia.org/example.jpg";
    using var handler = new FakeApiHandler("""
        {"query":{"pages":[{"title":"File:Example.jpg","imageinfo":[{"mime":"image/jpeg","url":"https://upload.wikimedia.org/example.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Example.jpg"}]}]}}
        """);
    using var http = new HttpClient(handler);
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    var model = new FakeModel([new ModelReply(null, [Call("search1", "image.search", new { query = "example", provider = "commons" })]),
        new ModelReply(null, [Call("preview1", "image.retrieve", new { urls = new[] { url } })])]);
    var approvals = new ScriptedApprovals([true, false]);
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace)).RunAsync(Guid.NewGuid(), "Find and show example images", approvals);
    Assert(result.Status == ConversationStatus.Paused && approvals.Calls == 2 && handler.Calls == 1 && !Directory.Exists(Path.Combine(setup.Workspace, ImagePreviewTool.CacheFolder)), "Denied preview must not download or create a cache.");
}

static async Task M6Rich3BareImageRecovery() => await M6Rich3ImageProtocol(recover: true);
static async Task M6Rich3BareImagePause() => await M6Rich3ImageProtocol(recover: false);

static async Task M6Rich3ImageProtocol(bool recover)
{
    using var setup = new TestContext();
    const string url = "https://upload.wikimedia.org/example.jpg";
    string raw = JsonSerializer.Serialize(new { urls = new[] { url } });
    using var handler = new FakeApiHandler("""
        {"query":{"pages":[{"title":"File:Example.jpg","imageinfo":[{"mime":"image/jpeg","url":"https://upload.wikimedia.org/example.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Example.jpg"}]}]}}
        """);
    using var http = new HttpClient(handler);
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    var script = new List<ModelReply> {
        new(null, [Call("select1", "nira_select_tools", new { names = new[] { "image.search", "image.retrieve" } })]),
        new(null, [Call("search1", "image.search", new { query = "example", provider = "commons" })]),
        new(raw, [])
    };
    if (recover) script.Add(new ModelReply(null, [Call("preview1", "image.retrieve", new { urls = new[] { url } })]));
    else { script.Add(new ModelReply(raw, [])); script.Add(new ModelReply("Done", [])); }
    var model = new FakeModel(script);
    var approvals = new ScriptedApprovals(recover ? new[] { true, false } : new[] { true });
    Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
        enableCapabilityRouting: true).RunAsync(id, "Find and show example images", approvals);
    var state = await setup.Store.LoadAsync(id, default);
    Assert(result.Status == ConversationStatus.Paused && state is not null &&
        state.Messages.All(m => m.Role != "assistant" || m.Content != raw) && state.LastAnswer != raw &&
        handler.Calls == 1 && approvals.Calls == (recover ? 2 : 1) &&
        !Directory.Exists(Path.Combine(setup.Workspace, ImagePreviewTool.CacheFolder)),
        "Bare image arguments must never be a completed answer or bypass approval.");
    Assert(model.PromptSnapshots[3].Any(m => m.Role == "system" && m.Content!.Contains("image.retrieve", StringComparison.Ordinal)),
        "Recovery should request the actual image retrieval capability.");
}

static async Task M6Rich3OrdinaryJson()
{
    using var setup = new TestContext();
    const string raw = "{\"urls\":[\"https://example.com\"]}";
    var model = new FakeModel([new ModelReply(raw, [])]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Give a sample URL JSON object", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.Message.StartsWith(raw, StringComparison.Ordinal),
        "Ordinary JSON without current image discovery must remain a valid answer.");
}

static async Task M6aCommonsDiscovery()
{
    using var handler = new FakeApiHandler("""
        {"batchcomplete":true,"query":{"pages":[
          {"title":"File:Example.jpg","imageinfo":[{"mime":"image/jpeg","url":"https://upload.wikimedia.org/example.jpg",
           "descriptionurl":"https://commons.wikimedia.org/wiki/File:Example.jpg","thumburl":"https://upload.wikimedia.org/thumb.jpg",
           "extmetadata":{"Artist":{"value":"<b>Example photographer</b>"},"LicenseShortName":{"value":"CC BY-SA 4.0"},
           "LicenseUrl":{"value":"https://creativecommons.org/licenses/by-sa/4.0/"}}}]},
          {"title":"File:Unsafe.svg","imageinfo":[{"mime":"image/svg+xml","url":"https://upload.wikimedia.org/unsafe.svg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Unsafe.svg"}]}
        ]}}
        """);
    using var http = new HttpClient(handler);
    var tool = new ImageDiscoveryTool(http, () => null);
    var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { query="example", provider="commons" }), default);
    Assert(result.Success && !result.ChangedState && handler.Calls == 1 && handler.AuthorizationHeader is null,
        "Commons metadata must require no API credential, fetch no media, and perform no mutation.");
    Assert(result.Data.GetProperty("count").GetInt32() == 1 && !result.Data.GetProperty("pixelsVerified").GetBoolean() &&
        !result.Data.GetProperty("downloaded").GetBoolean(), "Only raster metadata is accepted; pixels cannot be verified.");
    var candidate = result.Data.GetProperty("candidates")[0];
    Assert(candidate.GetProperty("SourceUrl").GetString() == "https://commons.wikimedia.org/wiki/File:Example.jpg" &&
        candidate.GetProperty("License").GetString() == "CC BY-SA 4.0" &&
        candidate.GetProperty("Author").GetString()!.Trim() == "Example photographer", "Commons attribution metadata lost.");
}

static async Task M6fDevelopmentPermissions()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("{\"batchcomplete\":true}");
    using var http = new HttpClient(handler);
    var provider = new ImageGenerationProvider(new string('a', 32), new string('T', 32));
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, imageGeneration: provider);
    var dev = new WorkspaceAuthority(setup.Workspace, developmentMode: true);
    var strict = new WorkspaceAuthority(setup.Workspace);
    foreach (string name in new[] { "web.search", "video.search", "image.search", "image.retrieve", "image.generate" })
    {
        var descriptor = registry.Descriptors.Single(t => t.Name == name);
        Assert(dev.IsPreapproved(descriptor, "https://example.com") && !strict.IsPreapproved(descriptor, "https://example.com"), "Media consent must be scoped to development mode: " + name);
    }
    foreach (var descriptor in registry.Descriptors.Where(t => t.Risk is ToolRisk.Dangerous or ToolRisk.WriteWorkspace or ToolRisk.WriteOutsideWorkspace))
        Assert(!dev.IsPreapproved(descriptor, Path.Combine(setup.Workspace, "protected.txt")), "Development mode must not grant mutation: " + descriptor.Name);
    var upload = new ToolDescriptor("vision.analyze", "Private image transfer", ToolRisk.ReadExternal, JsonToolSchema.Schema("{}"));
    Assert(!dev.IsPreapproved(upload, "https://example.com"), "Private image upload must still ask.");
    Assert(!dev.IsPreapproved(upload with { Name = "future.external" }, "https://example.com"), "Unknown network capabilities must still ask.");
    var model = new FakeModel([new ModelReply(null, [Call("img1", "image.search", new { query = "example", provider = "commons" })]), new ModelReply("No results found.", [])]);
    var broker = new FakeApprovals(false);
    var result = await new NiraEngine(model, registry, setup.Store, dev).RunAsync(Guid.NewGuid(), "Find example images", broker);
    Assert(result.Status == ConversationStatus.Completed && handler.Calls == 1 && broker.Calls == 0, "Development media search must not show approval.");
    using var videoHandler = new FakeApiHandler("""{"results":[{"url":"https://www.youtube.com/watch?v=abcdefghijk","title":"Video"}]}""");
    using var videoHttp = new HttpClient(videoHandler);
    var videoModel = new FakeModel([new ModelReply(null, [Call("video1", "video.search", new { query = "example video" })]), new ModelReply("Here is a video.", [])]);
    result = await new NiraEngine(videoModel, new ToolRegistry([new VideoSearchTool(videoHttp, () => "test-key")]), setup.Store, dev)
        .RunAsync(Guid.NewGuid(), "Find a video", broker);
    Assert(result.Status == ConversationStatus.Completed && videoHandler.Calls == 1 && broker.Calls == 0, "Video search must run without confirmation in development mode.");
    using var generationHandler = new FakeApiHandler("""{"success":true,"result":{"image":"PGh0bWw+PC9odG1sPg=="}}""");
    using var generationHttp = new HttpClient(generationHandler);
    var generationTool = new ImageGenerationTool(new WorkspacePathResolver(setup.Workspace), generationHttp, provider);
    var generationModel = new FakeModel([new ModelReply(null, [Call("gen1", "image.generate", new { prompt = "Blue sky", width = 256, height = 256 })])]);
    result = await new NiraEngine(generationModel, new ToolRegistry([generationTool]), setup.Store, dev)
        .RunAsync(Guid.NewGuid(), "Create blue sky artwork", broker);
    Assert(result.Status == ConversationStatus.Paused && generationHandler.Calls == 1 && broker.Calls == 0 &&
        !Directory.Exists(Path.Combine(setup.Workspace, ImageGenerationTool.CacheFolder)), "Generation dispatch needs no confirmation; invalid data still must not save or retry.");
    var writer = new FakeModel([new ModelReply(null, [Call("write1", "filesystem.write_text", new { path = "protected.txt", content = "Blocked" })])]);
    result = await new NiraEngine(writer, registry, setup.Store, dev).RunAsync(Guid.NewGuid(), "Create a file", broker);
    Assert(result.Status == ConversationStatus.Paused && broker.Calls == 1 && !File.Exists(Path.Combine(setup.Workspace, "protected.txt")), "Workspace writes still need approval in development mode.");
}

static async Task M6fVideoSearch()
{
    const string url = "https://www.youtube.com/watch?v=abcdefghijk";
    using var handler = new FakeApiHandler("""
        {"results":[{"url":"https://www.youtube.com/watch?v=abcdefghijk","title":"Video"},
         {"url":"https://youtu.be/abcdefghijk","title":"Duplicate"},
         {"url":"https://www.youtube.com.evil.example/watch?v=abcdefghijk"},
         {"url":"https://www.youtube.com/channel/abcdefghijk"},
         {"url":"https://vimeo.com/12345678","title":"Other"}]}
        """);
    using var http = new HttpClient(handler);
    var args = JsonSerializer.SerializeToElement(new { query = "demo" });
    var tool = new VideoSearchTool(http, () => "test-key");
    var result = await tool.ExecuteAsync(args, default);
    Assert(result.Success && result.Data.GetProperty("results").GetArrayLength() == 2 && handler.Calls == 1, "Only supported deduplicated references expected.");
    Assert(handler.AuthorizationHeader == "Bearer test-key" && !result.Data.GetRawText().Contains("test-key", StringComparison.Ordinal), "Credential must remain outside receipts.");
    using var request = JsonDocument.Parse(handler.RequestBody!);
    Assert(request.RootElement.GetProperty("include_domains").GetArrayLength() == 3, "Video-specific provider filter missing.");
    var state = new ConversationState { Operations = [new AgentOperation { ToolName = "video.search", Status = OperationStatus.Succeeded, ResultJson = JsonSerializer.Serialize(result) }] };
    var allowed = VideoEvidence.Allowed(state);
    state.Messages.Add(new ChatEntry("user", "Find a video about this."));
    Assert(VideoEvidence.SearchFallback(state).Count == 1, "Single-video fallback must not expose all candidates.");
    state.Messages.Add(new ChatEntry("user", "Find five interesting videos."));
    Assert(VideoEvidence.SearchFallback(state).Count == 2, "Fallback cannot invent missing requested results.");
    Assert(VideoEvidence.Allows(allowed, url) && !VideoEvidence.Allows(allowed, "https://youtu.be/abcdefghijk"), "Only exact observed references may embed.");
    Assert(VideoEvidence.Titles(state)[url] == "Video" && VideoEvidence.Titles(state)["https://vimeo.com/12345678"] == "Other", "Player titles must come from the recorded search results.");
    state.Operations[0].Status = OperationStatus.Failed;
    Assert(VideoEvidence.Allowed(state).Count == 0 && VideoEvidence.SearchFallback(state).Count == 0, "Failed search cannot authorize an embed or recovery deck.");
    var missing = new VideoSearchTool(http, () => null);
    Assert(missing.UnavailableReason() is not null && !(await missing.ExecuteAsync(args, default)).Success && handler.Calls == 1, "Missing credentials must not send request.");
}

static async Task M6aTavilyDiscovery()
{
    using var handler = new FakeApiHandler("""
        {"images":[{"url":"https://example.com/image.jpg","description":"Example"},"https://example.com/image.jpg",
          "http://example.com/unsafe.jpg","https://127.0.0.1/private.jpg","https://user:secret@example.com/unsafe.jpg"],
         "results":[{"url":"https://example.com/article","images":[{"url":"https://example.com/image.jpg","description":"Example caption"}]}]}
        """);
    using var http = new HttpClient(handler);
    var args = JsonSerializer.SerializeToElement(new { query="example", provider="tavily" });
    var result = await new ImageDiscoveryTool(http, () => "test-key").ExecuteAsync(args, default);
    Assert(result.Success && result.Data.GetProperty("count").GetInt32() == 1 && handler.Calls == 1,
        "Tavily must deduplicate and reject malformed or obviously private media references.");
    Assert(handler.AuthorizationHeader == "Bearer test-key" && !result.Data.GetRawText().Contains("test-key", StringComparison.Ordinal),
        "Provider key must not enter receipts.");
    using var body = JsonDocument.Parse(handler.RequestBody!);
    Assert(body.RootElement.GetProperty("include_images").GetBoolean() && body.RootElement.GetProperty("include_image_descriptions").GetBoolean(),
        "Tavily image mode missing.");
    Assert(result.Data.GetProperty("candidates")[0].GetProperty("SourceUrl").GetString() == "https://example.com/article", "Observed source association lost.");
    var missing = await new ImageDiscoveryTool(http, () => null).ExecuteAsync(args, default);
    Assert(!missing.Success && missing.ErrorCode == "IMAGE_SEARCH_KEY_MISSING" && handler.Calls == 1, "Missing key must not send request.");
}

static async Task M6aMalformedAndBounded()
{
    var args = JsonSerializer.SerializeToElement(new { query="example", provider="commons" });
    foreach (string raw in new[] { "not JSON", "{\"error\":{}}", new string('x', 1_200_001) })
    {
        using var handler = new FakeApiHandler(raw);
        using var http = new HttpClient(handler);
        var result = await new ImageDiscoveryTool(http).ExecuteAsync(args, default);
        Assert(!result.Success && handler.Calls == 1, "Malformed/oversize metadata must fail without retries.");
    }
    using var emptyHandler = new FakeApiHandler("{\"batchcomplete\":true}");
    using var emptyHttp = new HttpClient(emptyHandler);
    var empty = await new ImageDiscoveryTool(emptyHttp).ExecuteAsync(args, default);
    Assert(empty.Success && empty.Data.GetProperty("count").GetInt32() == 0, "Empty search is not a fabricated result.");
}

static async Task M6aApprovalAndRegistry()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("{\"batchcomplete\":true}");
    using var http = new HttpClient(handler);
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    Assert(registry.TryGet("image.search", out var tool) && tool is not null &&
        tool.Descriptor.Risk == ToolRisk.ReadExternal && !tool.Descriptor.HasSideEffects, "Image search registration or risk incorrect.");
    Assert(!registry.Validate("image.search", "{\"query\":\"example\",\"provider\":\"commons\",\"url\":\"https://example.com\"}", out _, out _),
        "Model-controlled search endpoint must be rejected.");
    var model = new FakeModel([new ModelReply(null, [Call("image1", "image.search", new { query="example", provider="commons" })])]);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    var broker = new FakeApprovals(false);
    var result = await engine.RunAsync(Guid.NewGuid(), "Find example images", broker);
    Assert(result.Status == ConversationStatus.Paused && broker.Calls == 1 && handler.Calls == 0, "Denied search must send no network traffic.");
}

static async Task M5cIdentityResearchPolicy()
{
    using var setup = new TestContext();
    var model = new FakeModel([new ModelReply("No established public affiliation.", [])]);
    var result = await setup.Engine(model, routing: true, adaptive: true).RunAsync(Guid.NewGuid(),
        "Is this local project's namesake website official?", new FakeApprovals(false), new M5bDesktopProgress());
    string instructions = model.PromptSnapshots.Single()[0].Content!;
    Assert(result.Status == ConversationStatus.Completed && instructions.Contains("ENTITY RESOLUTION", StringComparison.Ordinal) &&
        instructions.Contains("Do not infer affiliation from name similarity", StringComparison.Ordinal) &&
        instructions.Contains("RESEARCH EFFICIENCY", StringComparison.Ordinal) &&
        instructions.Contains("For genuinely broad research", StringComparison.Ordinal),
        "The model must receive identity disambiguation and bounded research guidance without removing broad research.");
}

static async Task M5bGroundingAndStyle()
{
    using var setup = new TestContext();
    var model = new FakeModel([new ModelReply("Illustrative idea.", [])]);
    var result = await setup.Engine(model, routing: true, adaptive: true).RunAsync(Guid.NewGuid(), "Explain your design", new FakeApprovals(false), new M5bDesktopProgress());
    string instructions = model.PromptSnapshots.Single()[0].Content!;
    Assert(result.Status == ConversationStatus.Completed && instructions.Contains("FACTUAL GROUNDING", StringComparison.Ordinal) &&
        instructions.Contains("local development project", StringComparison.Ordinal) && instructions.Contains("Never invent a", StringComparison.Ordinal) &&
        instructions.Contains("[color=#RRGGBB]text[/color]", StringComparison.Ordinal), "Grounding and desktop formatting must reach the same ordinary model inference.");
}

static async Task M5bUnobservedUrls()
{
    using var setup = new TestContext();
    var model = new FakeModel([new ModelReply("[Example](https://invented.example.invalid/guide)\n\n```python\nurl = 'https://code.example.invalid/'\n```", [])]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Explain with sources", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && model.PromptSnapshots.Count == 1 &&
        result.Message.Contains("1 model-supplied URL(s)", StringComparison.Ordinal) && result.Message.Contains("Treat them as unverified", StringComparison.Ordinal),
        "Unobserved citations must be flagged without extra model calls; code literals are not citations.");
}

static async Task M5bStructuredSourceNotice()
{
    using var setup = new TestContext();
    var model = new FakeModel([new ModelReply(null, [Call("finish", "nira_finish", new { outcome = "answer", answer = "[Docs](https://invented.example.invalid/)", evidence_ids = Array.Empty<string>() })])]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Explain with sources", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.Message.Contains("Source check (host)", StringComparison.Ordinal), "Structured finish must use the same source check.");
}

static async Task M5bObservedUrls()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var saved = new ConversationState { Id = id, Status = ConversationStatus.Running, Objective = "Explain the observed source", AutoPlanningAttempted = true,
        Messages = [new ChatEntry("user", "Explain the observed source")],
        Operations = [new AgentOperation { ToolName = "web.search", Status = OperationStatus.Succeeded,
            ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Search result", new { url = "https://observed.example.invalid/guide" })) }] };
    await setup.Store.SaveAsync(saved, default);
    var model = new FakeModel([new ModelReply("[Docs](https://observed.example.invalid/guide)", [])]);
    var result = await setup.Engine(model).RunAsync(id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && !result.Message.Contains("Source check (host)", StringComparison.Ordinal), "An exact URL recorded in a successful receipt is not unobserved.");
}

static async Task M5bExampleWithCitation()
{
    using var setup = new TestContext();
    string example = "Example only: " + PseudoBrowserCall() + " — see https://invented.example.invalid/guide";
    var model = new FakeModel([new ModelReply(example, [])]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Explain a legacy envelope with sources", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 0 &&
        model.PromptSnapshots.Count == 1 && result.Message.StartsWith(example, StringComparison.Ordinal) &&
        result.Message.Contains("1 model-supplied URL(s)", StringComparison.Ordinal),
        "The example must remain intact, and the separate prose citation must still be flagged.");
}

static async Task M5bNonEnvelopeUrl()
{
    using var setup = new TestContext();
    const string answer = "Documentation: {\"url\":\"https://invented.example.invalid/guide\"}";
    var result = await setup.Engine(new FakeModel([new ModelReply(answer, [])]))
        .RunAsync(Guid.NewGuid(), "Give documentation", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.Message.StartsWith(answer, StringComparison.Ordinal) &&
        result.Message.Contains("1 model-supplied URL(s)", StringComparison.Ordinal),
        "Arbitrary JSON is not a tool example and does not exempt a URL from the source check.");
}

static async Task OrdinaryConversation()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply("Hello! How can I help?", [])
    ]);
    Guid id = Guid.NewGuid();
    AgentRunResult result = await setup.Engine(model).RunAsync(id, "hi", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Ordinary chat must complete immediately");
    Assert(result.Message == "Hello! How can I help?", "Normal assistant text should be displayed");
    Assert(result.EvidenceIds.Count == 0, "A greeting must not invent action evidence");
    ConversationState saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.LastAnswer == result.Message, "Reply must survive restart");
    Assert(saved.Messages.Count(x => x.Role == "assistant") == 1,
        "The model's normal answer must not be rejected or duplicated");
    Assert(!saved.Messages.Any(x => x.Role == "system"),
        "No artificial repair prompt may be persisted in conversation history");
}

static async Task EmptyReplyRecovery()
{
    using var setup = new TestContext();
    int calls = 0;
    var model = new FakeModel(messages =>
    {
        calls++;
        if (calls == 1) return new ModelReply(null, []);
        Assert(messages.Any(x => x.Role == "system" &&
            x.Content?.Contains("neither user-visible text nor a tool call", StringComparison.Ordinal) == true),
            "An empty response should receive transient model-level recovery guidance");
        return new ModelReply("Ready.", []);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Hello", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.Message == "Ready.",
        "One empty provider output should not break an otherwise valid turn");
    Assert(calls == 2, "Recovery must be bounded");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Messages.Count == 2 && saved.Messages[0].Role == "user" &&
        saved.Messages[1].Role == "assistant",
        "Transient recovery guidance must not corrupt durable conversation history");
}

static async Task ToolFreeRecovery()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, []), new ModelReply(null, []),
        new ModelReply("Hello from the ordinary chat endpoint.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "hi", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed,
        "An empty tool-enabled response must recover through tool-free chat");
    Assert(result.Message == "Hello from the ordinary chat endpoint.",
        "Recovery must return the provider's actual answer, not a hardcoded greeting");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 0 && result.EvidenceIds.Count == 0,
        "A tool-free fallback must not imply an external action occurred");
}

static async Task PersistentEmptyReply()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, []), new ModelReply(" ", []), new ModelReply(null, [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Describe the workspace", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "Missing visible output must not complete");
    Assert(result.Message.Contains("no user-visible answer", StringComparison.Ordinal),
        "The reason for the pause must be concrete");
    Assert(result.EvidenceIds.Count == 0, "No phantom action receipt");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Messages.All(x => x.Role == "user"), "No reasoning or fake assistant output persisted");
}

static async Task PlainReplyWithPendingPlan()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan1", "nira_plan", new { steps = new[] {
            new { description = "Inspect environment", successCriterion = "Inspect receipt" },
            new { description = "Verify results", successCriterion = "Readback receipt" }
        } })]),
        new ModelReply("I have a plan but have not completed it.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Investigate and verify", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "Plain text must not complete an open plan");
    Assert(result.EvidenceIds.Count == 0, "Planning must not create action evidence");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Plan.Count == 2 && saved.Plan.All(x => x.Status == "pending"),
        "Unfinished plan must survive the pause");
}

static async Task ReadThenPlainAnswer()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "facts.txt"), "NIRA_FACTS");
    var model = new FakeModel([
        new ModelReply(null, [Call("read1", "filesystem.read_text", new { path = "facts.txt" })]),
        new ModelReply("The file contains NIRA_FACTS.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Read facts.txt and summarize", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "A read tool should allow an ordinary final reply");
    Assert(result.Message.Contains("NIRA_FACTS", StringComparison.Ordinal), "Visible answer missing");
    Assert(result.EvidenceIds.Count == 0, "Read-only tools must not be treated as mutation evidence");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded,
        "Read action must be audited");
}

static async Task WirePlainText()
{
    using var handler = new FakeApiHandler("""
        {"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"Hello there!"}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"), "mock", () => "test-key");
    ModelReply reply = await model.CompleteAsync([new ChatEntry("user", "hi")], [], default);
    Assert(reply.Content == "Hello there!" && reply.ToolCalls.Count == 0,
        "OpenAI-compatible plain content must be exposed to the engine");
    Assert(handler.Calls == 1, "One provider response expected");
    using var wireRequest = JsonDocument.Parse(handler.RequestBody!);
    Assert(!wireRequest.RootElement.TryGetProperty("tools", out _) &&
        !wireRequest.RootElement.TryGetProperty("tool_choice", out _),
        "Tool-free API requests must omit tools and tool_choice for provider compatibility");
}

static async Task WireContentParts()
{
    using var handler = new FakeApiHandler("""
        {"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":[{"type":"text","text":"Hello "},{"type":"text","text":"there!"}]}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"), "mock", () => null);
    ModelReply reply = await model.CompleteAsync([new ChatEntry("user", "hi")], [], default);
    Assert(reply.Content == "Hello \nthere!" && reply.ToolCalls.Count == 0,
        "Visible text parts must be extracted; reasoning is not user content");
}

static async Task HttpToolOnlyAssistantNull()
{
    using var handler = new FakeApiHandler("""
        {"choices":[{"message":{"role":"assistant","content":"done"}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => null);
    ModelReply reply = await model.CompleteAsync([
        new ChatEntry("user", "create the file"),
        new ChatEntry("assistant", null, ToolCalls: [
            new ModelToolCall("original-call-id", "filesystem.write_text", "{}")]),
        new ChatEntry("tool", "{\"success\":true}", "original-call-id")
    ], [], default);
    Assert(reply.Content == "done", "Test provider must reply normally");
    using var request = JsonDocument.Parse(handler.RequestBody!);
    JsonElement[] messages = request.RootElement.GetProperty("messages").EnumerateArray().ToArray();
    Assert(messages[1].GetProperty("content").ValueKind == JsonValueKind.Null &&
        messages[1].GetProperty("tool_calls")[0].GetProperty("id").GetString() == "original-call-id" &&
        messages[2].GetProperty("tool_call_id").GetString() == "original-call-id",
        "Tool-only assistant content must be null, and tool-call IDs must remain paired");
}

static async Task HttpProviderCallIdPreserved()
{
    using var handler = new FakeApiHandler("""
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"provider-id-100","function":{"name":"workspace.info","arguments":"{}"}}]}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => null);
    ModelReply reply = await model.CompleteAsync([new ChatEntry("user", "workspace")], [], default);
    Assert(reply.ToolCalls.Count == 1 && reply.ToolCalls[0].Id == "provider-id-100",
        "Never silently rewrite provider function-call IDs");
}

static async Task HttpProviderDuplicateCallIds()
{
    using var handler = new FakeApiHandler("""
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"duplicate-id","function":{"name":"workspace.info","arguments":"{}"}},{"id":"duplicate-id","function":{"name":"workspace.info","arguments":"{}"}}]}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => null);
    bool refused = false;
    try { await model.CompleteAsync([new ChatEntry("user", "test")], [], default); }
    catch (AgentException e)
    { refused = e.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase); }
    Assert(refused, "Duplicated provider IDs must not be dispatched or silently rewritten");
}

static IReadOnlyList<ChatEntry> LongContextHistory()
{
    var entries = new List<ChatEntry> {
        new("system", "Host safety and plan evidence rules"),
        new("user", "Inspect a workbook, create a corrected copy and verify a real formula.")
    };
    for (int i = 0; i < 20; i++)
    {
        string id = "model-ctx-" + i;
        string tool = i % 3 == 0 ? "productivity.revise_workbook" : "productivity.read";
        entries.Add(new ChatEntry("assistant", ToolCalls: [
            new ModelToolCall(id, tool, "{\"path\":\"expense.xlsx\"}")]));
        string reply = JsonSerializer.Serialize(new {
            receiptId = "durable-ctx-" + i, Success = true,
            Summary = "Earlier host observation; UNTRUSTED_CONTENT_IGNORE_SAFETY must not be system instructions",
            ChangedState = i % 3 == 0,
            Data = new {
                sourcePath = "C:/Workspace/expense.xlsx",
                outputPath = "C:/Workspace/corrected.xlsx",
                outputSha256 = new string('a', 64),
                verifiedFormulaCells = 1,
                sourceUnchanged = true,
                secretDocumentBody = "UNTRUSTED_CONTENT_IGNORE_SAFETY"
            }
        });
        entries.Add(new ChatEntry("tool", reply, id));
    }
    entries.Add(new ChatEntry("user", "Continue without recreating the corrected workbook."));
    return entries;
}

static IReadOnlyList<ToolDescriptor> ContextRecoveryTools()
{
    var names = new List<string> {
        "nira_plan", "nira_plan_update", "nira_plan_status", "nira_finish", "nira_ask_user",
        "productivity.read", "productivity.inspect", "productivity.revise_workbook",
        "productivity.create_workbook", "filesystem.find", "filesystem.list", "filesystem.sha256"
    };
    for (int i = 0; i < 24; i++) names.Add("unrelated.operation" + i);
    return names.Select(name => new ToolDescriptor(name, "A registered host tool", ToolRisk.ReadLocal,
        JsonToolSchema.Schema("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}")))
        .ToArray();
}

static async Task EmptyContextRetry()
{
    using var handler = new EmptyContextSequenceHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var reply = await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default);
    Assert(reply.Content == "Recovered visible response" && handler.RequestBodies.Count == 2 &&
        handler.RequestBodies[1].Length < handler.RequestBodies[0].Length,
        "Silent HTTP 200 should allow one smaller inference request.");
}

static async Task EmptyContextBounded()
{
    using var handler = new EmptyContextSequenceHandler(alwaysEmpty: true);
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var reply = await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default);
    Assert(string.IsNullOrWhiteSpace(reply.Content) && reply.ToolCalls.Count == 0 &&
        handler.RequestBodies.Count == 2, "No fabrication or unbounded retry for persistent empty output.");
}

static async Task EmptyContextSticky()
{
    using var handler = new EmptyContextSequenceHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default);
    await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default);
    Assert(handler.RequestBodies.Count == 3 && handler.RequestBodies[1] == handler.RequestBodies[2],
        "Later rounds retain the smaller inference after an empty full response.");
}

static Task ToolFreeContextReduction()
{
    var original = LongContextHistory();
    bool reduced = ModelContextRecovery.TryReduce(original, [], out var messages, out var tools);
    Assert(reduced && messages.Count < original.Count && tools.Count == 0 &&
        messages.Any(m => m.Role == "user" && m.Content == original[^1].Content),
        "A tool-free request may shrink without introducing executable schemas.");
    return Task.CompletedTask;
}

static async Task ToolFreeEmptyContextRetry()
{
    using var handler = new EmptyContextSequenceHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var reply = await model.CompleteAsync(LongContextHistory(), [], default);
    Assert(reply.Content == "Recovered visible response" && handler.RequestBodies.Count == 2 &&
        handler.RequestBodies[1].Length < handler.RequestBodies[0].Length,
        "Tool-free empty output must be eligible for smaller inference.");
    foreach (string body in handler.RequestBodies)
    {
        using var json = JsonDocument.Parse(body);
        Assert(!json.RootElement.TryGetProperty("tools", out _) &&
            !json.RootElement.TryGetProperty("tool_choice", out _), "No tools introduced during fallback.");
    }
}

static Task ContextKeepsLargeLatestUser()
{
    var original = LongContextHistory().ToList();
    var instruction = new ChatEntry("user", "Keep the original file. " + new string('x', 9000));
    original.Insert(4, instruction);
    original.RemoveAt(original.Count - 1);
    bool reduced = ModelContextRecovery.TryReduce(original, ContextRecoveryTools(), out var messages, out _);
    Assert(reduced && messages.Contains(instruction) && messages.Contains(original[1]) &&
        messages.ToList().IndexOf(original[1]) < messages.ToList().IndexOf(instruction),
        "Long newest user instruction cannot disappear behind newer tool history.");
    return Task.CompletedTask;
}

static async Task SmallEmptyContextNoRetry()
{
    using var handler = new EmptyContextSequenceHandler(alwaysEmpty: true);
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var reply = await model.CompleteAsync([new ChatEntry("user", "Hello")], [], default);
    Assert(handler.RequestBodies.Count == 1 && reply.ToolCalls.Count == 0 &&
        string.IsNullOrWhiteSpace(reply.Content), "Small empty reply keeps the existing engine recovery policy.");
}

static async Task EmptyPauseShowsAction()
{
    using var setup = new TestContext();
    // Use a real local file write and then simulate a persistently silent model.
    var model = new FakeModel([
        new ModelReply(null, [Call("write-before-silence", "filesystem.write_text", new { path = "saved.txt", content = "saved evidence" })]),
        new ModelReply(null, []), new ModelReply(null, []), new ModelReply(null, [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Create a file and report it", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && result.EvidenceIds.Count == 1 &&
        result.Message.Contains("Host-recorded execution evidence", StringComparison.Ordinal) &&
        result.Message.Contains("saved.txt", StringComparison.Ordinal) &&
        saved.Operations.Count == 1 && File.ReadAllText(Path.Combine(setup.Workspace, "saved.txt")) == "saved evidence",
        "A silent model must report the saved receipt without replaying or falsely completing the task.");
}

static Task ContextRecoveryShrinksContext()
{
    IReadOnlyList<ChatEntry> original = LongContextHistory();
    var tools = ContextRecoveryTools();
    bool allowed = ModelContextRecovery.TryReduce(original, tools,
        out var result, out var selected);
    Assert(allowed && result.Count < original.Count && selected.Count <= 15 &&
        selected.Count < tools.Count && result.Count >= 4,
        "Recoverable provider history must shrink while retaining usable registered tools.");
    return Task.CompletedTask;
}

static Task ContextRecoveryKeepsUserAndPlan()
{
    var original = LongContextHistory();
    bool result = ModelContextRecovery.TryReduce(original, ContextRecoveryTools(),
        out var messages, out _);
    Assert(result && messages.Any(m => m.Role == "system" &&
        m.Content == "Host safety and plan evidence rules") &&
        messages.Any(m => m.Role == "user" && m.Content!.Contains("real formula", StringComparison.Ordinal)) &&
        messages.Any(m => m.Role == "user" && m.Content!.Contains("without recreating", StringComparison.Ordinal)),
        "Context recovery must retain host constraints, original objective and newest user instruction.");
    return Task.CompletedTask;
}

static Task ContextRecoveryPairsToolMessages()
{
    ModelContextRecovery.TryReduce(LongContextHistory(), ContextRecoveryTools(),
        out var messages, out _);
    var visibleCalls = messages.Where(m => m.Role == "assistant")
        .SelectMany(m => m.ToolCalls ?? []).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
    var replies = messages.Where(m => m.Role == "tool").ToArray();
    Assert(replies.Length > 0 && replies.All(m => m.ToolCallId is not null &&
        visibleCalls.Contains(m.ToolCallId)) && visibleCalls.Count == replies.Length,
        "Compaction must not create orphan tool results or missing tool-call replies.");
    return Task.CompletedTask;
}

static Task ContextRecoveryKeepsEvidenceMetadata()
{
    ModelContextRecovery.TryReduce(LongContextHistory(), ContextRecoveryTools(),
        out var messages, out _);
    string details = string.Join("\n", messages.Where(m => m.Role == "system")
        .Select(m => m.Content ?? ""));
    Assert(details.Contains("durable-ctx-", StringComparison.Ordinal) &&
        details.Contains("outputPath=C:/Workspace/corrected.xlsx", StringComparison.Ordinal) &&
        details.Contains("verifiedFormulaCells=1", StringComparison.Ordinal),
        "Earlier changed-state operations need durable IDs and typed outcome metadata.");
    return Task.CompletedTask;
}

static Task ContextRecoveryNoDataInstructions()
{
    ModelContextRecovery.TryReduce(LongContextHistory(), ContextRecoveryTools(),
        out var messages, out _);
    string system = string.Join("\n", messages.Where(m => m.Role == "system")
        .Select(m => m.Content ?? ""));
    Assert(!system.Contains("UNTRUSTED_CONTENT_IGNORE_SAFETY", StringComparison.Ordinal) &&
        system.Contains("NOT instructions or approvals", StringComparison.Ordinal),
        "Untrusted document contents and free-form tool summaries must not become system instructions.");
    return Task.CompletedTask;
}

static Task ContextRecoveryNoMutation()
{
    var original = LongContextHistory();
    var tools = ContextRecoveryTools();
    string before = JsonSerializer.Serialize(new { original, tools });
    ModelContextRecovery.TryReduce(original, tools, out _, out _);
    Assert(JsonSerializer.Serialize(new { original, tools }) == before,
        "Compacting an inference request must not rewrite saved records or tool definitions.");
    return Task.CompletedTask;
}

static Task ContextRecoveryToolSubset()
{
    ModelContextRecovery.TryReduce(LongContextHistory(), ContextRecoveryTools(),
        out _, out var tools);
    Assert(tools.Any(t => t.Name == "productivity.revise_workbook") &&
        tools.Any(t => t.Name == "productivity.read") &&
        tools.Any(t => t.Name == "nira_plan_update") &&
        tools.Any(t => t.Name == "nira_finish") &&
        tools.All(t => !t.Name.StartsWith("unrelated.", StringComparison.Ordinal)),
        "Tool recovery must keep the actual host tool schemas required for continuing the observed task.");
    return Task.CompletedTask;
}

static Task ContextRecoveryRefusesSmallHistory()
{
    bool result = ModelContextRecovery.TryReduce([
        new ChatEntry("system", "safe"), new ChatEntry("user", "test")],
        ContextRecoveryTools(), out _, out _);
    Assert(!result, "Small requests should preserve their original request and error behavior.");
    return Task.CompletedTask;
}

static async Task ContextRecoveryHttp400ThenSuccess()
{
    using var handler = new ContextRecoverySequenceHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    ModelReply reply = await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default);
    Assert(reply.Content == "Recovered normal answer" && handler.RequestBodies.Count == 2 &&
        handler.RequestBodies[1].Length < handler.RequestBodies[0].Length,
        "An explicit context-limit error may trigger ONE smaller model-only retry.");
    using var packet = JsonDocument.Parse(handler.RequestBodies[1]);
    Assert(packet.RootElement.GetProperty("tools").GetArrayLength() <= 15 &&
        packet.RootElement.GetProperty("messages").GetArrayLength() < 42,
        "Recovered provider request must actually be smaller.");
}

static async Task ContextRecoveryHttp400TwiceStops()
{
    using var handler = new ContextRecoverySequenceHandler(alwaysReject: true);
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    bool failed = false;
    try { await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default); }
    catch (AgentException error)
    {
        failed = error.Message.Contains("possible_context_limit", StringComparison.Ordinal);
    }
    Assert(failed && handler.RequestBodies.Count == 2,
        "Persistent context errors must stop after exactly one compact inference retry.");
}

static async Task ContextRecoveryStickyPreference()
{
    using var handler = new ContextRecoverySequenceHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default);
    await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default);
    Assert(handler.RequestBodies.Count == 3 &&
        handler.RequestBodies[0].Length > handler.RequestBodies[1].Length &&
        handler.RequestBodies[1].Length == handler.RequestBodies[2].Length,
        "After one known provider context rejection, later rounds must stay compact without a repeated HTTP 400.");
}

static async Task ContextRecoveryOther400NeverRetries()
{
    using var handler = new ContextRecoverySequenceHandler(errorBody:
        "{\"error\":{\"message\":\"Unexpected tool call id\"}}");
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    bool failed = false;
    try { await model.CompleteAsync(LongContextHistory(), ContextRecoveryTools(), default); }
    catch (AgentException error)
    {
        failed = error.Message.Contains("possible_tool_transcript", StringComparison.Ordinal);
    }
    Assert(failed && handler.RequestBodies.Count == 1,
        "A malformed tool transcript is not a context problem and must not be retried.");
}

static async Task Http400ContextSafe()
{
    using var handler = new ModelRejectionHandler("""
        {"error":{"code":"context_length_exceeded","message":"SECRET_DO_NOT_REPEAT context window exceeded at C:\\private"}}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => "PRIVATE_BEARER_VALUE");
    string message = "";
    try { await model.CompleteAsync([new ChatEntry("user", "USER_PRIVATE_CONTENT")], [], default); }
    catch (AgentException e) { message = e.Message; }
    Assert(message.Contains("HTTP 400", StringComparison.Ordinal) &&
        message.Contains("category=possible_context_limit", StringComparison.Ordinal) &&
        message.Contains("messages=1", StringComparison.Ordinal) &&
        message.Contains("requestBytes=", StringComparison.Ordinal) &&
        !message.Contains("SECRET_DO_NOT_REPEAT", StringComparison.Ordinal) &&
        !message.Contains("USER_PRIVATE_CONTENT", StringComparison.Ordinal) &&
        !message.Contains("PRIVATE_BEARER_VALUE", StringComparison.Ordinal),
        "A rejected provider request must show safe request metadata, never private content");
}

static async Task Http400ToolTranscriptSafe()
{
    using var handler = new ModelRejectionHandler("""
        {"error":{"message":"Unexpected tool call id PRIVATE_CALL_ID in tool results"}}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => null);
    string message = "";
    try { await model.CompleteAsync([new ChatEntry("user", "test")], [], default); }
    catch (AgentException e) { message = e.Message; }
    Assert(message.Contains("category=possible_tool_transcript", StringComparison.Ordinal) &&
        !message.Contains("PRIVATE_CALL_ID", StringComparison.Ordinal),
        "Tool-history provider error must be classified without disclosing the rejected ID");
}

static async Task Http400UnrecognizedSafe()
{
    using var handler = new ModelRejectionHandler("<html>CONFIDENTIAL_PROVIDER_REPLY</html>");
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => null);
    string message = "";
    try { await model.CompleteAsync([new ChatEntry("user", "test")], [], default); }
    catch (AgentException e) { message = e.Message; }
    Assert(message.Contains("category=unknown", StringComparison.Ordinal) &&
        !message.Contains("CONFIDENTIAL_PROVIDER_REPLY", StringComparison.Ordinal) &&
        message.Contains("Use :status", StringComparison.Ordinal),
        "Unrecognized provider errors must remain redacted and preserve task recovery advice");
}

static Task CredentialEncrypted()
{
    using var setup = new TestContext();
    string appRoot = Path.Combine(setup.Root, "app-auth");
    var store = new NiraModelCredentialStore(appRoot, new Uri("https://example.invalid/v1/"));
    const string marker = "NIRA_TEST_PROVIDER_KEY_PRIVATE_4610";
    store.Save(marker);
    string[] files = Directory.GetFiles(Path.Combine(appRoot, "Credentials"), "*.dpapi");
    Assert(files.Length == 1, "Only one provider-scoped encrypted credential expected");
    Assert(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(files[0])).Contains(marker, StringComparison.Ordinal),
        "Credential plaintext leaked to disk");
    var reopened = new NiraModelCredentialStore(appRoot, new Uri("https://example.invalid/v1/"));
    Assert(reopened.ReadStored() == marker && reopened.Resolve() == marker,
        "DPAPI credential must persist and take priority over process environment");
    reopened.ClearStored();
    Assert(reopened.ReadStored() is null, "Clearing the stored credential must remove it");
    return Task.CompletedTask;
}

static Task CredentialEndpointIsolation()
{
    using var setup = new TestContext();
    string appRoot = Path.Combine(setup.Root, "app-auth");
    var provider1 = new NiraModelCredentialStore(appRoot, new Uri("https://one.example/v1/"));
    var provider2 = new NiraModelCredentialStore(appRoot, new Uri("https://two.example/v1/"));
    provider1.Save("NIRA_KEY_PROVIDER_ONE");
    Assert(provider2.ReadStored() is null, "Credentials must not cross provider endpoints");
    provider2.Save("NIRA_KEY_PROVIDER_TWO");
    Assert(provider1.ReadStored() == "NIRA_KEY_PROVIDER_ONE" &&
        provider2.ReadStored() == "NIRA_KEY_PROVIDER_TWO", "Independent provider keys must remain separate");
    return Task.CompletedTask;
}

static Task CredentialCorruptedRecord()
{
    using var setup = new TestContext();
    string appRoot = Path.Combine(setup.Root, "app-auth");
    var store = new NiraModelCredentialStore(appRoot, new Uri("https://example.invalid/v1/"));
    store.Save("ORIGINAL_KEY");
    string file = Directory.GetFiles(Path.Combine(appRoot, "Credentials"), "*.dpapi").Single();
    File.WriteAllBytes(file, new byte[48]);
    Assert(store.ReadStored() is null, "Invalid protected record must not be mistaken for a key");
    store.Save("REPLACEMENT_KEY");
    Assert(store.ReadStored() == "REPLACEMENT_KEY", "An invalid record must be recoverable");
    return Task.CompletedTask;
}

static async Task ProviderUnauthorized()
{
    using var handler = new ModelAuthHttpHandler(System.Net.HttpStatusCode.Unauthorized);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => null);
    bool authenticated = false;
    try { await model.CompleteAsync([new ChatEntry("user", "hi")], [], default); }
    catch (ModelAuthenticationException error)
    { authenticated = error.Message.Contains("401", StringComparison.Ordinal); }
    Assert(authenticated, "HTTP 401 must be distinguishable from other HTTP provider failures");
    Assert(handler.RequestAuthorization is null, "No absent API key should be fabricated");
}

static async Task ProviderUsesSavedKey()
{
    using var setup = new TestContext();
    var endpoint = new Uri("https://example.invalid/v1/");
    var store = new NiraModelCredentialStore(Path.Combine(setup.Root, "auth"), endpoint);
    store.Save("NIRA_EXAMPLE_BEARER_KEY");
    using var handler = new FakeApiHandler("""
        {"choices":[{"message":{"role":"assistant","content":"Hi!"}}]}
        """);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, endpoint, "mock", store.Resolve);
    ModelReply reply = await model.CompleteAsync([new ChatEntry("user", "hi")], [], default);
    Assert(reply.Content == "Hi!", "Encrypted key should authenticate a normal chat response");
    Assert(handler.AuthorizationHeader == "Bearer NIRA_EXAMPLE_BEARER_KEY",
        "Provider must receive saved key in the Bearer header, never the request body");
    Assert(handler.RequestBody is not null &&
        !handler.RequestBody.Contains("NIRA_EXAMPLE_BEARER_KEY", StringComparison.Ordinal),
        "Credentials must not appear in the JSON request body");
}

static async Task CredentialRetryDoesNotDuplicate()
{
    using var setup = new TestContext();
    int calls = 0;
    var model = new FakeModel(_ => {
        calls++;
        if (calls == 1) throw new ModelAuthenticationException("HTTP 401");
        return new ModelReply("Hello from the live model.", []);
    });
    Guid conversation = Guid.NewGuid();
    var engine = setup.Engine(model);
    bool rejected = false;
    try { await engine.RunAsync(conversation, "hi", new FakeApprovals(false)); }
    catch (ModelAuthenticationException) { rejected = true; }
    Assert(rejected, "Simulated provider must reject the first key");
    var saved = (await setup.Store.LoadAsync(conversation, default))!;
    Assert(saved.Messages.Count(x => x.Role == "user") == 1,
        "User text must be checkpointed exactly once before auth failure");
    AgentRunResult recovered = await engine.RunAsync(conversation, null, new FakeApprovals(false));
    Assert(recovered.Status == ConversationStatus.Completed && recovered.Message == "Hello from the live model.",
        "Credential replacement must resume original objective");
    saved = (await setup.Store.LoadAsync(conversation, default))!;
    Assert(saved.Messages.Count(x => x.Role == "user") == 1,
        "Retry after HTTP 401 must not append the same message twice");
}

static async Task MutationPlainReplyNeedsEvidence()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages =>
    {
        rounds++;
        if (rounds == 1)
            return new ModelReply(null, [Call("write", "filesystem.write_text",
                new { path = "verified.txt", content = "Verified contents" })]);
        return new ModelReply("I created the file.", []);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Create a file", new FakeApprovals(false));
    Assert(rounds == 2 && result.Status == ConversationStatus.Completed,
        "A normal final answer after verified write must finish without a special tool roundtrip");
    Assert(result.EvidenceIds.Count == 1, "Changed-state receipt must be attached by the host");
    Assert(result.Message.Contains("filesystem.write_text", StringComparison.Ordinal) &&
           result.Message.Contains(result.EvidenceIds[0], StringComparison.Ordinal) &&
           result.Message.Contains("verified.txt", StringComparison.Ordinal),
        "Final answer must include host-attested tool identity, receipt ID, and saved location");
    Assert(File.ReadAllText(Path.Combine(setup.Workspace, "verified.txt")) == "Verified contents",
        "The real file must exist");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Status == ConversationStatus.Completed && state.LastAnswer == result.Message,
        "The host-attested answer must be durably checkpointed");
}

static async Task MutationPlainReplyPauses()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1)
            return new ModelReply(null, [Call("write", "filesystem.write_text",
                new { path = "pending.txt", content = "Persisted" })]);
        if (rounds == 2) return new ModelReply("Created the file.", []);
        throw new InvalidOperationException("A third model request is an unnecessary retry");
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Write and verify", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && rounds == 2,
        "An already observed write must not trigger repetitive completion rounds");
    Assert(result.EvidenceIds.Count == 1 &&
           result.Message.Contains("Host-recorded execution evidence:", StringComparison.Ordinal),
        "Host evidence must accompany model text");
    Assert(File.Exists(Path.Combine(setup.Workspace, "pending.txt")),
        "Completing must not undo or silently replay an observed operation");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded,
        "The operation must still be durably audited");
}

static async Task MutationMustFinishAsAction()
{
    using var setup = new TestContext();
    int calls = 0;
    var model = new FakeModel(messages =>
    {
        calls++;
        if (calls == 1)
            return new ModelReply(null, [Call("write", "filesystem.write_text",
                new { path = "effect.txt", content = "Actual" })]);
        string receipt = ReceiptIdForMutation(messages);
        if (calls == 2)
            return new ModelReply(null, [Call("invalid", "nira_finish", new {
                outcome = "answer", answer = "I created the file", evidence_ids = new[] { receipt }
            })]);
        Assert(messages.Any(x => x.Role == "tool" &&
            x.Content?.Contains("UNVERIFIED_EFFECT", StringComparison.Ordinal) == true),
            "The invalid informational finish must have a tool error receipt");
        return new ModelReply(null, [Call("valid", "nira_finish", new {
            outcome = "action", answer = "Created the file", evidence_ids = new[] { receipt }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && calls == 3,
        "A model can repair its incorrect outcome but must not bypass mutation evidence");
}

static async Task ToolFailureCanRecover()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "exists.txt"), "ORIGINAL");
    int calls = 0;
    var model = new FakeModel(messages =>
    {
        calls++;
        if (calls == 1)
            return new ModelReply(null, [Call("attempt", "filesystem.write_text",
                new { path = "exists.txt", content = "Never overwrite" })]);
        if (calls == 2)
        {
            Assert(messages.Any(m => m.Role == "tool" &&
                m.Content?.Contains("ALREADY_EXISTS", StringComparison.Ordinal) == true),
                "The model should see why the first attempt failed");
            return new ModelReply(null, [Call("alternative", "filesystem.write_text",
                new { path = "alternative.txt", content = "RECOVERED" })]);
        }
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "Created the alternative file",
            evidence_ids = new[] { ReceiptIdForMutation(messages) }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Create a file without overwriting existing data", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && calls == 3,
        "Recoverable tool failure should allow an alternative action");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "exists.txt")) == "ORIGINAL",
        "Existing content must remain intact");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "alternative.txt")) == "RECOVERED",
        "Alternative result must actually exist");
}

static async Task UnfinishedPlanRejectsFinish()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan", "nira_plan", new { steps = new[] {
            new { description="Inspect workspace", successCriterion="Observed entries" },
            new { description="Verify result", successCriterion="Readback" }
        } })]),
        new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome="answer", answer="Task complete", evidence_ids=Array.Empty<string>()
        })]),
        new ModelReply(null, [Call("ask", "nira_ask_user", new {
            question="The task remains unfinished. Can I continue?"
        })])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Complete a multistep task", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser, "An incomplete plan cannot finish");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Messages.Any(m => m.Role == "tool" &&
        m.Content?.Contains("PLAN_UNFINISHED", StringComparison.Ordinal) == true),
        "The model should receive the unfinished-plan error as a tool observation");
}


static async Task ProcessDenied()
{
    using var setup = new TestContext();
    string marker = Path.Combine(setup.Workspace, "must-not-create.txt");
    var model = new FakeModel([
        new ModelReply(null, [Call("run-denied", "process.run", new {
            executable = "powershell.exe", arguments = new[] {
                "-NoProfile", "-NonInteractive", "-Command",
                "$null = New-Item -ItemType File -LiteralPath 'must-not-create.txt'"
            }, workingDirectory = setup.Workspace
        })]),
        new ModelReply("The process was not approved or started.", [])
    ]);
    var answer = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Attempt to run a command", new FakeApprovals(false));
    Assert(answer.Status == ConversationStatus.Paused && !File.Exists(marker),
        "A declined process must be paused without executing or retrying it");
}

static async Task ProcessDotnetVersion()
{
    using var setup = new TestContext();
    ToolReceipt result = await InvokeProcessTool(setup, "dotnet", ["--version"]);
    Assert(result.Success && result.ChangedState, "Started process must have a journalable effects receipt");
    Assert(result.Data.GetProperty("exitCode").GetInt32() == 0 &&
           !string.IsNullOrWhiteSpace(result.Data.GetProperty("stdout").GetString()),
           "Separate argv process invocation must capture the actual dotnet version");
    Assert(result.Data.GetProperty("workingDirectory").GetString() == setup.Workspace,
           "Process must run with the selected workspace working directory");
}

static Task ProcessWorkingDirectoryScope()
{
    using var setup = new TestContext();
    var tool = new ProcessRunTool(new WorkspacePathResolver(setup.Workspace));
    bool rejected = false;
    try { tool.GetTarget(JsonSerializer.SerializeToElement(new {
        executable = "dotnet", arguments = new[] { "--version" },
        workingDirectory = setup.Root
    })); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected, "A process working directory outside the workspace must be rejected");
    return Task.CompletedTask;
}

static async Task ProcessSecretsExcluded()
{
    using var setup = new TestContext();
    const string marker = "NIRA_TEST_KEY_MUST_NOT_REACH_CHILD_98211";
    string? before = Environment.GetEnvironmentVariable("NIRA_API_KEY");
    try
    {
        Environment.SetEnvironmentVariable("NIRA_API_KEY", marker);
        ToolReceipt result = await InvokeProcessTool(setup, "powershell.exe", [
            "-NoProfile", "-NonInteractive", "-Command",
            "if ($env:NIRA_API_KEY) { 'LEAK' } else { 'NOT_PRESENT' }"
        ]);
        Assert(result.Success && result.Data.GetProperty("exitCode").GetInt32() == 0,
               "Secret-environment probe must execute");
        string output = result.Data.GetProperty("stdout").GetString() ?? "";
        Assert(output.Contains("NOT_PRESENT", StringComparison.Ordinal) &&
               !output.Contains("LEAK", StringComparison.Ordinal),
               "Model-run child process must not inherit NIRA_API_KEY");
    }
    finally { Environment.SetEnvironmentVariable("NIRA_API_KEY", before); }
}

static async Task ProcessTimeout()
{
    using var setup = new TestContext();
    ToolReceipt result = await InvokeProcessTool(setup, "powershell.exe", [
        "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 15"
    ], timeout: 1);
    Assert(result.Success && result.ChangedState &&
           result.Data.GetProperty("timedOut").GetBoolean(),
           "Timed-out processes must be terminated and recorded as potentially state-changing");
}

static async Task ProcessNonzeroExit()
{
    using var setup = new TestContext();
    ToolReceipt result = await InvokeProcessTool(setup, "powershell.exe", [
        "-NoProfile", "-NonInteractive", "-Command", "exit 7"
    ]);
    Assert(result.Success && result.ChangedState &&
           result.Data.GetProperty("exitCode").GetInt32() == 7,
           "Nonzero exit is an observed process result, not proof of zero side effects");
}

static async Task ProcessAgentExecution()
{
    using var setup = new TestContext();
    var model = new FakeModel(messages => {
        string? receipt = messages.Where(x => x.Role == "tool" && x.Content is not null)
            .Select(x => x.Content).FirstOrDefault(x => x!.Contains("receiptId", StringComparison.Ordinal));
        if (receipt is null)
            return new ModelReply(null, [Call("process1", "process.run", new {
                executable = "dotnet", arguments = new[] { "--version" },
                workingDirectory = setup.Workspace
            })]);
        using var data = JsonDocument.Parse(receipt);
        string receiptId = data.RootElement.GetProperty("receiptId").GetString()!;
        return new ModelReply(null, [Call("finished", "nira_finish", new {
            outcome = "action", answer = "Executed dotnet --version with recorded output.",
            evidence_ids = new[] { receiptId }
        })]);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Execute the installed dotnet CLI version check", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
           "An approved subprocess must finish only with its changed-state receipt");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded,
           "Subprocess execution must be durably recorded");
}

static async Task ProcessPlainFinalEvidence()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1)
            return new ModelReply(null, [Call("process1", "process.run", new {
                executable = "dotnet", arguments = new[] { "--version" },
                workingDirectory = setup.Workspace
            })]);
        if (rounds == 2)
            return new ModelReply("The dotnet version command returned a result.", []);
        throw new InvalidOperationException("Do not re-execute the process for completion formatting");
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Check installed SDK version", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Completed && rounds == 2,
        "An observed approved process must finish after a normal final text answer");
    Assert(result.EvidenceIds.Count == 1 &&
           result.Message.Contains(result.EvidenceIds[0], StringComparison.Ordinal) &&
           result.Message.Contains("exitCode: 0", StringComparison.Ordinal) &&
           result.Message.Contains("stdout:", StringComparison.Ordinal),
        "Host must report real stdout, exit code, and audited receipt independent of model text");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Status == ConversationStatus.Completed,
        "A process must execute once and its completion must persist");
}

static async Task PlainFinalRecoverableFailure()
{
    using var setup = new TestContext();
    await File.WriteAllTextAsync(Path.Combine(setup.Workspace, "exists.txt"), "ORIGINAL");
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1) return new ModelReply(null, [Call("failed", "filesystem.write_text",
            new { path = "exists.txt", content = "new" })]);
        if (rounds == 2) return new ModelReply(null, [Call("success", "filesystem.write_text",
            new { path = "new-file.txt", content = "new" })]);
        return new ModelReply("Created the replacement file without overwriting.", []);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Create an unused file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "Only successful effects receive completion evidence");
    Assert(result.Message.Contains("new-file.txt", StringComparison.Ordinal) &&
           !result.Message.Contains("exists.txt", StringComparison.Ordinal),
        "Failed tool attempts cannot appear as successful host-attested operations");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "exists.txt")) == "ORIGINAL",
        "The original file must be preserved");
}

static async Task MutationPlainPendingPlan()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan", "nira_plan", new { steps = new[] {
            new { description="Create a file", successCriterion="File exists" },
            new { description="Verify final content", successCriterion="Readback observed" }
        } })]),
        new ModelReply(null, [Call("write", "filesystem.write_text",
            new { path = "pending-plan.txt", content = "data" })]),
        new ModelReply("Done!", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Create and verify", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused,
        "Host must reject a plain final when recorded plan steps are unfinished");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Succeeded &&
           saved.Status == ConversationStatus.Paused,
        "The actual write must persist without falsely completing the plan");
}

static async Task UnknownOperationPlainFinal()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var state = new ConversationState { Id=id, Objective="Report prior action", Status=ConversationStatus.Paused };
    state.Operations.Add(new AgentOperation {
        Id="uncertain", ToolName="process.run", Status=OperationStatus.Unknown,
        ArgumentsJson="{}"
    });
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel([
        new ModelReply("Completed!", [])
    ])).RunAsync(id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.NeedsReconciliation,
        "Unknown process effects must never be converted into trusted completion");
}

static Task<ToolReceipt> InvokeProcessTool(TestContext context, string executable,
    string[] arguments, int timeout = 30)
{
    var tool = new ProcessRunTool(new WorkspacePathResolver(context.Workspace));
    JsonElement request = JsonSerializer.SerializeToElement(new {
        executable, arguments, workingDirectory = context.Workspace,
        timeoutSeconds = timeout
    });
    return tool.ExecuteAsync(request, default);
}

static string ReceiptIdForMutation(IReadOnlyList<ChatEntry> messages)
{
    foreach (var message in messages.Where(m => m.Role == "tool" && m.Content is not null))
    {
        using var doc = JsonDocument.Parse(message.Content!);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("ChangedState", out var changed) &&
            changed.ValueKind == JsonValueKind.True &&
            root.TryGetProperty("receiptId", out var id))
            return id.GetString()!;
    }
    throw new InvalidOperationException("No observed changed-state receipt");
}

static async Task WireEndToEndToolPipeline()
{
    using var setup = new TestContext();
    using var handler = new WireToolPipelineHandler(setup.Workspace);
    using var client = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(client, new Uri("https://example.invalid/v1/"),
        "mock", () => "wire-test-key");
    Guid conversation = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(conversation,
        "Inspect the workspace, create a file, read it back and verify its hash.",
        new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed,
        "Full provider-wire sequence must finish with real receipts");
    Assert(handler.PlanningRequests == 1 && handler.RequestCount == 6 && handler.ReadbackSeen && handler.HashSeen,
        "All six real HTTP wire turns must be processed with observed outputs");
    string file = Path.Combine(setup.Workspace, WireToolPipelineHandler.FileName);
    Assert(File.Exists(file) && await File.ReadAllTextAsync(file) == WireToolPipelineHandler.FileContent,
        "The actual file must be created and readable");
    var persisted = (await setup.Store.LoadAsync(conversation, default))!;
    Assert(persisted.Operations.Count == 5 && persisted.Operations.All(o =>
        o.Status == OperationStatus.Succeeded), "Every real operation must be durably audited");
    Assert(result.EvidenceIds.Count == 1, "Only the file mutation counts as changed-state evidence");
}

static async Task DirectoryCreateObserved()
{
    using var setup = new TestContext();
    var model = new FakeModel(messages => {
        if (!messages.Any(m => m.Role == "tool"))
            return new ModelReply(null, [Call("mkdir", "filesystem.mkdir", new { path = "new-folder" })]);
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "Created the directory.",
            evidence_ids = new[] { ReceiptIdForMutation(messages) }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create a directory", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "Directory creation must finish with observed mutation receipt");
    Assert(Directory.Exists(Path.Combine(setup.Workspace, "new-folder")), "Actual new directory missing");
}

static async Task DirectoryExistingPreserved()
{
    using var setup = new TestContext();
    string occupied = Path.Combine(setup.Workspace, "occupied");
    await File.WriteAllTextAsync(occupied, "PROTECT EXISTING DATA");
    var model = new FakeModel([
        new ModelReply(null, [Call("mkdir", "filesystem.mkdir", new { path = "occupied" })]),
        new ModelReply("The name already exists, so it was not replaced.", [])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create occupied directory", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Failed creation should permit an honest answer");
    Assert(await File.ReadAllTextAsync(occupied) == "PROTECT EXISTING DATA", "Existing file must remain untouched");
}

static async Task DirectoryOutsideDenied()
{
    using var setup = new TestContext();
    string outside = Path.Combine(setup.Root, "not-allowed-directory");
    var model = new FakeModel([
        new ModelReply(null, [Call("mkdir", "filesystem.mkdir", new { path = outside })]),
        new ModelReply("The external directory was not created because consent was denied.", [])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Create an external directory", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused && !Directory.Exists(outside),
        "Workspace authority must halt denied external mkdir without retrying");
}

static async Task EditTextPreservesBytes()
{
    using var setup = new TestContext();
    string file = Path.Combine(setup.Workspace, "existing-source.txt");
    byte[] before = [0xEF, 0xBB, 0xBF, ..System.Text.Encoding.UTF8.GetBytes("first\r\nSECOND\r\nthird\r\n")];
    await File.WriteAllBytesAsync(file, before);
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)).ToLowerInvariant();
    var model = new FakeModel(messages => {
        if (!messages.Any(m => m.Role == "tool"))
            return new ModelReply(null, [Call("edit", "filesystem.edit_text", new {
                path = "existing-source.txt", expectedSha256 = hash,
                oldText = "SECOND", newText = "CHANGED"
            })]);
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "Verified revision was published.",
            evidence_ids = new[] { ReceiptIdForMutation(messages) }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Revise the existing text", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "Versioned edit must have a verified receipt");
    byte[] expected = [0xEF, 0xBB, 0xBF, ..System.Text.Encoding.UTF8.GetBytes("first\r\nCHANGED\r\nthird\r\n")];
    Assert((await File.ReadAllBytesAsync(file)).SequenceEqual(expected), "BOM, CRLF and unaffected bytes must be preserved");
    Assert(!Directory.EnumerateFiles(setup.Workspace, ".nira-edit-*").Any(),
        "Successful edit must not leave temporary or rollback files");
}

static async Task EditTextStaleHash()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "guarded.txt");
    await File.WriteAllTextAsync(path, "the actual version");
    string staleHash = new string('0', 64);
    var model = new FakeModel([
        new ModelReply(null, [Call("edit", "filesystem.edit_text", new {
            path = "guarded.txt", expectedSha256 = staleHash,
            oldText = "actual", newText = "incorrect"
        })]),
        new ModelReply("The hash did not match, so the source was left unchanged.", [])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Edit a stale revision", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Stale write should allow honest reporting");
    Assert(await File.ReadAllTextAsync(path) == "the actual version", "Stale edits must not touch the current version");
}

static async Task EditTextAmbiguous()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "ambiguous.txt");
    await File.WriteAllTextAsync(path, "repeat / repeat");
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        await File.ReadAllBytesAsync(path))).ToLowerInvariant();
    var model = new FakeModel([
        new ModelReply(null, [Call("edit", "filesystem.edit_text", new {
            path = "ambiguous.txt", expectedSha256 = hash,
            oldText = "repeat", newText = "changed"
        })]),
        new ModelReply("Multiple matching spans were found; no edit was made.", [])
    ]);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Edit unique text", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Ambiguous edit should be reported accurately");
    Assert(await File.ReadAllTextAsync(path) == "repeat / repeat", "Ambiguous edits must preserve input");
}

static async Task EditTextApprovedOutside()
{
    using var setup = new TestContext();
    string outside = Path.Combine(setup.Root, "external-approved.txt");
    await File.WriteAllTextAsync(outside, "BEFORE");
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        await File.ReadAllBytesAsync(outside))).ToLowerInvariant();
    var model = new FakeModel(messages => {
        if (!messages.Any(m => m.Role == "tool"))
            return new ModelReply(null, [Call("edit", "filesystem.edit_text", new {
                path = outside, expectedSha256 = hash, oldText = "BEFORE", newText = "AFTER"
            })]);
        return new ModelReply(null, [Call("finish", "nira_finish", new {
            outcome = "action", answer = "User-approved outside edit was verified.",
            evidence_ids = new[] { ReceiptIdForMutation(messages) }
        })]);
    });
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(), "Edit this approved outside file", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "Outside edit requires explicit approval and receipts");
    Assert(await File.ReadAllTextAsync(outside) == "AFTER", "Approved outside edit was not published");
}

static async Task WireEditTextPipeline()
{
    using var setup = new TestContext();
    string file = Path.Combine(setup.Workspace, "wire-edit.txt");
    await File.WriteAllTextAsync(file, "V1 INITIAL\n");
    using var handler = new WireEditPipelineHandler(file);
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => "integration-key");
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Hash, edit and read back the existing file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1,
        "HTTP tool pipeline edit must finish with changed-state evidence");
    Assert(handler.PlanningRequests == 1 && handler.Rounds == 4 && handler.ReadbackVerified,
        "Provider transcript must include the real readback and a verified finish");
    Assert(await File.ReadAllTextAsync(file) == "V2 UPDATED\n", "Actual file must contain the edited content");
}

static async Task ToolApprovalQuestionRoutesToAction()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("permission", "nira_ask_user", new {
            question = "May I create a directory and write source code in the workspace?" })]),
        new ModelReply(null, [Call("write", "filesystem.write_text", new {
            path = "routed.txt", content = "ROUTED_BY_HOST" })]),
        new ModelReply("File was created through the registered tool.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Create a new file", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed, "Model-only approval must not interrupt task");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "routed.txt")) == "ROUTED_BY_HOST",
        "Registered workspace tool must perform the actual action");
    Assert(result.EvidenceIds.Count == 1, "Actual file write must have one host receipt");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.PendingQuestion is null, "Permission question must never be displayed to user");
    Assert(saved.Messages.Any(x => x.Role == "tool" &&
        x.Content?.Contains("USE_HOST_TOOL_APPROVAL", StringComparison.Ordinal) == true),
        "The model must receive actionable, auditable tool feedback");
}

static async Task DeniedActionCannotBeBypassed()
{
    using var setup = new TestContext();
    string outside = Path.Combine(setup.Root, "unapproved-outside.txt");
    var model = new FakeModel([
        new ModelReply(null, [Call("write", "filesystem.write_text", new {
            path = outside, content = "FORBIDDEN" })]),
        new ModelReply(null, [Call("permission", "nira_ask_user", new {
            question = "May I write to the outside directory instead?" })]),
        new ModelReply("The operation was not approved; I did not write the file.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Write this file outside the workspace only with approval", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused, "A declined outside write must stop model execution");
    Assert(!File.Exists(outside) && result.EvidenceIds.Count == 0,
        "A conversational request must not bypass the denied host operation");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Operations.Count == 1 && saved.Operations[0].Status == OperationStatus.Rejected,
        "Operation must remain rejected after attempted conversational approval");
}

static async Task LegitimateMissingDetailQuestion()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("clarify", "nira_ask_user", new {
            question = "Can you tell me which existing folder contains the project?" })])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Inspect the project in my selected folder", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.AwaitingUser,
        "A request for truly missing information must still reach the user");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.PendingQuestion?.Contains("which existing folder", StringComparison.Ordinal) == true,
        "Actual clarification must be preserved and resumable");
}

static async Task RepeatedPermissionProbesPause()
{
    using var setup = new TestContext();
    var responses = Enumerable.Range(0, 24).Select(i =>
        new ModelReply(null, [Call("permission" + i, "nira_ask_user", new {
            question = "May I execute the next registered tool?" })]));
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(new FakeModel(responses)).RunAsync(id,
        "Run a previously described task", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused,
        "A looping provider must pause safely rather than ask permission indefinitely");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.PendingQuestion is null && saved.Operations.Count == 0,
        "Repeated approval probes may neither interrupt the user nor run tools");
    Assert(saved.Messages.Count(x => x.Role == "tool" &&
        x.Content?.Contains("USE_HOST_TOOL_APPROVAL", StringComparison.Ordinal) == true) == 6,
        "Model must pause after six unproductive permission probes, not exhaust 24 rounds");
}

static async Task HttpPermissionQuestionGate()
{
    using var setup = new TestContext();
    using var handler = new WirePermissionGateHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"),
        "mock", () => "wire-auth");
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Describe the current workspace using available tools", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.Message.Contains("Workspace inspected"),
        "HTTP transcript should recover from a conversational permission probe");
    Assert(handler.PlanningRequests == 1 && handler.Rounds == 3 && handler.HostDenialObserved,
        "Real transport must return the host guidance to the provider before tool execution");
    Assert(result.EvidenceIds.Count == 0,
        "Read-only workspace information must not fabricate mutation evidence");
}

static async Task ProcessInheritsOsIdentity()
{
    using var setup = new TestContext();
    string? prior = Environment.GetEnvironmentVariable("OS");
    try
    {
        Environment.SetEnvironmentVariable("OS", "Windows_NT");
        ToolReceipt receipt = await InvokeProcessTool(setup, "powershell.exe", [
            "-NoProfile", "-NonInteractive", "-Command", "$env:OS"
        ]);
        Assert(receipt.Success && receipt.Data.GetProperty("exitCode").GetInt32() == 0,
            "OS identity probe must execute successfully");
        Assert(receipt.Data.GetProperty("stdout").GetString()!.Trim() == "Windows_NT",
            "Approved child must receive only the allowlisted OS identity needed by dotnet templates");
    }
    finally { Environment.SetEnvironmentVariable("OS", prior); }
}


static Task ExplicitYesVariants()
{
    foreach (string input in new[] { "yes", "YES", "YeS", " yes ", "\tYES\t" })
        Assert(ApprovalInput.IsExplicitYes(input), "Explicit case-insensitive yes must authorize: " + input);
    foreach (string? input in new string?[] { null, "", " ", "y", "no", "yes please", "yes!", "yes\nno" })
        Assert(!ApprovalInput.IsExplicitYes(input), "Only an unambiguous yes token may authorize: " + input);
    return Task.CompletedTask;
}

static async Task DeniedProcessPausesWithoutReplay()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(_ => {
        if (++rounds != 1) throw new InvalidOperationException("Model retried a denied command");
        return new ModelReply(null, [Call("process", "process.run", new {
            executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
        })]);
    });
    var broker = new FakeApprovals(false);
    Guid id = Guid.NewGuid();
    var answer = await setup.Engine(model).RunAsync(id,
        "Check dotnet version", broker);
    Assert(answer.Status == ConversationStatus.Paused && rounds == 1 && broker.Calls == 1,
        "Refusal must result in one authorization attempt and no retry");
    Assert(answer.EvidenceIds.Count == 0 && answer.Message.Contains("Not executed", StringComparison.Ordinal),
        "A denied process must not look completed or create execution evidence");
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Status == ConversationStatus.Paused && saved.Operations.Count == 1 &&
        saved.Operations[0].Status == OperationStatus.Rejected &&
        saved.Messages.Any(m => m.Role == "tool" && m.Content?.Contains("NOT_AUTHORIZED", StringComparison.Ordinal) == true),
        "The denied invocation and model-facing tool result must be persisted");
}

static async Task PlainPermissionRecoversToProcess()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1) return new ModelReply("May I run `dotnet --version` to check the SDK?", []);
        if (rounds == 2)
        {
            Assert(messages.Any(m => m.Role == "system" &&
                m.Content?.Contains("previous visible reply was only a request for permission", StringComparison.Ordinal) == true),
                "Permission-only prose must be recovered using transient guidance");
            return new ModelReply(null, [Call("version", "process.run", new {
                executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
            })]);
        }
        return new ModelReply("The installed .NET SDK version was observed from the process output.", []);
    });
    var approvals = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Report actual dotnet SDK version using an execution tool", approvals);
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 1 &&
        result.Message.Contains("exitCode: 0", StringComparison.Ordinal) && approvals.Calls == 1,
        "Recovered permission text must lead to exactly one authorized process with host evidence");
    Assert(rounds == 3, "Permission-only text should recover without additional model loops");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(!state.Messages.Any(m => m.Role == "assistant" &&
        m.Content?.StartsWith("May I run", StringComparison.Ordinal) == true),
        "Rejected permission prose must not pollute durable user-facing transcript");
}

static async Task RepeatedPlainPermissionPauses()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(_ => {
        rounds++;
        return new ModelReply("May I execute `dotnet --version`?", []);
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Execute dotnet --version", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Paused && rounds == 2 && result.EvidenceIds.Count == 0,
        "Repeated permission prose must pause without any executed action");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Status == ConversationStatus.Paused && state.Operations.Count == 0 &&
        state.LastAnswer is null && state.Messages.All(m => m.Role == "user"),
        "Permission prose must not become a fabricated completed answer");
}

static async Task StructuredFinishPermissionRejected()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, [Call("bad-finish", "nira_finish", new {
            outcome = "answer", answer = "May I execute dotnet --version?", evidence_ids = Array.Empty<string>()
        })]),
        new ModelReply("I did not execute a process or obtain a version.", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Find dotnet version", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 0 &&
        result.Message.Contains("did not execute", StringComparison.Ordinal),
        "A corrected honest answer can finish, but a permission request cannot");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Messages.Any(m => m.Role == "tool" &&
        m.Content?.Contains("PERMISSION_QUESTION_NOT_COMPLETION", StringComparison.Ordinal) == true),
        "Structured finish permission request must receive explicit tool error");
}

static async Task DenialAfterWritePreservesReceipt()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(_ => {
        rounds++;
        if (rounds == 1) return new ModelReply(null, [Call("write", "filesystem.write_text", new {
            path = "partial.txt", content = "PARTIAL" })]);
        if (rounds == 2) return new ModelReply(null, [Call("run", "process.run", new {
            executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
        })]);
        throw new InvalidOperationException("A rejected execution must not be re-proposed automatically");
    });
    var broker = new FakeApprovals(false);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Create a file and check dotnet version", broker);
    Assert(result.Status == ConversationStatus.Paused && rounds == 2 && broker.Calls == 1 &&
        result.EvidenceIds.Count == 1, "Partial authorized actions must stay audited after later denial");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "partial.txt")) == "PARTIAL",
        "Previously committed workspace file must not be rolled back on subsequent denial");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Operations.Count == 2 && state.Operations[0].Status == OperationStatus.Succeeded &&
        state.Operations[1].Status == OperationStatus.Rejected && state.Status == ConversationStatus.Paused,
        "Partial success and refusal must be separately recorded without declaring completion");
}

static async Task ObservedProcessNotRepeatedOnPermissionReply()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1) return new ModelReply(null, [Call("run", "process.run", new {
            executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
        })]);
        if (rounds == 2) return new ModelReply("May I run dotnet --version?", []);
        Assert(messages.Any(m => m.Role == "system" &&
            m.Content?.Contains("already executed", StringComparison.Ordinal) == true),
            "Recovery should tell model to report existing receipts, not rerun an approved process");
        return new ModelReply("The previously executed command's version is recorded in stdout.", []);
    });
    var broker = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id, "Get installed dotnet version", broker);
    Assert(result.Status == ConversationStatus.Completed && rounds == 3 && broker.Calls == 1 &&
        result.EvidenceIds.Count == 1 && result.Message.Contains("stdout:", StringComparison.Ordinal),
        "An already observed invocation must not need another approval to format its final result");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Operations.Count == 1 && state.Operations[0].Status == OperationStatus.Succeeded,
        "One actual command execution expected");
}

static async Task ToolFreePermissionFallbackPauses()
{
    using var setup = new TestContext();
    var model = new FakeModel([
        new ModelReply(null, []), new ModelReply(null, []),
        new ModelReply("May I run dotnet --version?", [])
    ]);
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model).RunAsync(id,
        "Check the installed dotnet version", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Paused && result.EvidenceIds.Count == 0 &&
        result.Message.Contains("No action was verified", StringComparison.Ordinal),
        "Tool-free fallback must not turn an unexecuted permission question into Completed");
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(state.Operations.Count == 0 && state.LastAnswer is null,
        "Fallback permission prose must not be saved as a verified user answer");
}

static async Task PermissionProbeCounterResetsAfterTool()
{
    using var setup = new TestContext();
    int rounds = 0;
    var model = new FakeModel(messages => {
        rounds++;
        if (rounds == 1) return new ModelReply("May I inspect the workspace?", []);
        if (rounds == 2) return new ModelReply(null, [Call("inspect", "workspace.info", new { })]);
        if (rounds == 3) return new ModelReply("May I execute dotnet --version?", []);
        if (rounds == 4) return new ModelReply(null, [Call("version", "process.run", new {
            executable = "dotnet", arguments = new[] { "--version" }, workingDirectory = setup.Workspace
        })]);
        return new ModelReply("The command returned the installed SDK version.", []);
    });
    var approval = new FakeApprovals(true);
    var result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Inspect the workspace then execute dotnet --version", approval);
    Assert(result.Status == ConversationStatus.Completed && rounds == 5 && approval.Calls == 1 &&
        result.EvidenceIds.Count == 1 && result.Message.Contains("exitCode: 0", StringComparison.Ordinal),
        "Permission-probe recovery should reset after a valid tool observation");
}

static Task BrowserRejectsNonHttpsAndCredentials()
{
    foreach (string bad in new[] { "http://www.example.com", "https://user:secret@www.example.com/",
        "https://www.example.com/#section", "ftp://www.example.com/" })
    {
        bool rejected = false;
        try { _ = PublicBrowserUrlPolicy.Parse(bad); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Browser URL policy must reject: " + bad);
    }
    return Task.CompletedTask;
}

static Task BrowserRejectsInternalHostAndPorts()
{
    foreach (string bad in new[] { "https://localhost", "https://inside.local/",
        "https://admin.internal/", "https://host.test/", "https://10.0.0.1/",
        "https://127.0.0.1/", "https://[::1]/", "https://www.example.com:8443/" })
    {
        bool rejected = false;
        try { _ = PublicBrowserUrlPolicy.Parse(bad); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Browser URL policy must reject internal target: " + bad);
    }
    return Task.CompletedTask;
}

static Task BrowserPublicDnsAddressGuards()
{
    string[] blocked = { "10.0.1.2", "127.0.0.1", "169.254.12.2", "172.20.0.1",
        "192.168.1.2", "100.64.1.1", "198.18.0.1", "203.0.113.9", "::1", "fc00::1", "fe80::1", "2001:db8::2" };
    foreach (string bad in blocked)
        Assert(!PublicBrowserUrlPolicy.IsPublicAddress(System.Net.IPAddress.Parse(bad)),
            "Non-public address must never be allowed: " + bad);
    Assert(PublicBrowserUrlPolicy.IsPublicAddress(System.Net.IPAddress.Parse("8.8.8.8")) &&
        PublicBrowserUrlPolicy.IsPublicAddress(System.Net.IPAddress.Parse("2606:4700:4700::1111")),
        "Valid globally routable addresses should be recognized");
    bool failed = false;
    try { PublicBrowserUrlPolicy.RequirePublicDns(new[] {
        System.Net.IPAddress.Parse("8.8.8.8"), System.Net.IPAddress.Parse("192.168.1.15") }); }
    catch (InvalidOperationException) { failed = true; }
    Assert(failed, "Mixed DNS results containing private IP must fail closed");
    return Task.CompletedTask;
}

static Task BrowserSameHostAndMethodEnforced()
{
    Assert(PublicBrowserUrlPolicy.AllowsSameHostGet("https://www.example.com/path", "www.example.com", "GET"),
        "Approved host HTTPS GET should pass");
    Assert(PublicBrowserUrlPolicy.AllowsSameHostGet("https://www.example.com/a.css", "www.example.com", "HEAD"),
        "HEAD resources should pass");
    Assert(!PublicBrowserUrlPolicy.AllowsSameHostGet("https://not.example.com/", "www.example.com", "GET") &&
        !PublicBrowserUrlPolicy.AllowsSameHostGet("http://www.example.com/", "www.example.com", "GET") &&
        !PublicBrowserUrlPolicy.AllowsSameHostGet("https://www.example.com/api", "www.example.com", "POST"),
        "Other origins, insecure transport and POST must be blocked");
    return Task.CompletedTask;
}

static Task BrowserInspectSchemaIsStrict()
{
    IAgentTool tool = new PublicBrowserInspectionTool(new FakeBrowserReader());
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { url="https://www.example.com", dangerous=true }), out _),
        "Unexpected browser arguments must fail schema validation");
    Assert(tool.Descriptor.Risk == ToolRisk.ReadExternal && !tool.Descriptor.HasSideEffects,
        "Browser inspection must require the network approval path and not claim local writes");
    return Task.CompletedTask;
}

static async Task BrowserDenialPreventsVisit()
{
    using var setup = new TestContext();
    var reader = new FakeBrowserReader();
    var registry = new ToolRegistry([new PublicBrowserInspectionTool(reader)]);
    var model = new FakeModel([new ModelReply(null, [Call("browser-denied", "browser.inspect_public",
        new { url = "https://www.example.com/" })])]);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    Guid conversation = Guid.NewGuid();
    var denied = await engine.RunAsync(conversation, "Inspect a public page", new FakeApprovals(false));
    Assert(denied.Status == ConversationStatus.Paused && reader.Calls == 0,
        "Declined browser visit must pause without making any network request");
    var persisted = (await setup.Store.LoadAsync(conversation, default))!;
    Assert(persisted.Operations.Count == 1 && persisted.Operations[0].Status == OperationStatus.Rejected &&
        persisted.Operations[0].ErrorCode == "NOT_AUTHORIZED", "Denied browser visit must be audited");
}

static async Task BrowserApprovedObservationIsAudited()
{
    using var setup = new TestContext();
    var reader = new FakeBrowserReader();
    var registry = new ToolRegistry([new PublicBrowserInspectionTool(reader)]);
    var model = new FakeModel([
        new ModelReply(null, [Call("browser-approved", "browser.inspect_public", new {
            url = "https://www.example.com/" })]),
        new ModelReply("Observed a public page with title Example.", [])
    ]);
    var approvals = new FakeApprovals(true);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Inspect public example page", approvals);
    Assert(result.Status == ConversationStatus.Completed && approvals.Calls == 1 && reader.Calls == 1,
        "Approved browser visit must run once and finish from the actual observation");
    var persisted = (await setup.Store.LoadAsync(id, default))!;
    Assert(persisted.Operations.Count == 1 && persisted.Operations[0].Status == OperationStatus.Succeeded &&
        persisted.Operations[0].ResultJson!.Contains("Example Page", StringComparison.Ordinal),
        "Browser observation must be present in the durable tool journal");
}

static async Task BrowserRedirectOutsideApprovedOriginRejected()
{
    var reader = new FakeBrowserReader { FinalUrl = "https://other.example.net/" };
    var tool = new PublicBrowserInspectionTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { url = "https://www.example.com/" }), default);
    Assert(!receipt.Success && receipt.ErrorCode == "BROWSER_REDIRECT_BLOCKED" && reader.Calls == 1,
        "Tool must reject any final page outside the explicitly approved hostname");
}

static Task BrowserValidPublicAddressAndUrl()
{
    Uri uri = PublicBrowserUrlPolicy.Parse("https://www.example.com:443/path?q=1");
    Assert(uri.Scheme == "https" && uri.Port == 443 && uri.Host == "www.example.com",
        "Public HTTPS URL with standard port and query should be accepted");
    PublicBrowserUrlPolicy.RequirePublicDns(new[] { System.Net.IPAddress.Parse("8.8.8.8") });
    bool emptyRejected = false;
    try { PublicBrowserUrlPolicy.RequirePublicDns(Array.Empty<System.Net.IPAddress>()); }
    catch (InvalidOperationException) { emptyRejected = true; }
    Assert(emptyRejected, "Empty DNS reply must fail closed");
    return Task.CompletedTask;
}

static async Task BrowserHttpWireProtocolRoundTrip()
{
    using var setup = new TestContext();
    using var handler = new BrowserWireHandler();
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://api.example.invalid/v1/"),
        "mock", () => "wire-browser-key");
    var reader = new FakeBrowserReader();
    var engine = new NiraEngine(model, new ToolRegistry([new PublicBrowserInspectionTool(reader)]),
        setup.Store, new WorkspaceAuthority(setup.Workspace));
    var approved = new FakeApprovals(true);
    var result = await engine.RunAsync(Guid.NewGuid(), "Inspect https://www.example.com/", approved);
    Assert(result.Status == ConversationStatus.Completed && handler.PlanningRequests == 1 && handler.Rounds == 2 &&
        handler.ObservedReceipt && reader.Calls == 1 && approved.Calls == 1,
        "Real model HTTP protocol must carry the observed page receipt back to GPT-OSS tool transcript");
}

static ModelToolCall Call(string id, string name, object args) =>
    new(id, name, JsonSerializer.Serialize(args));
static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static Task BrowserElementsMetadataOnly()
{
    var snapshot = new BrowserSnapshot("https://www.example.com/", "https://www.example.com/",
        "Example", 200, "Public text", false, [], [
            new BrowserElement("button", "", "Search", "button", "Search", false),
            new BrowserElement("input", "", "Keyword", "text", "", false)]);
    string json = JsonSerializer.Serialize(snapshot);
    Assert(json.Contains("Keyword", StringComparison.Ordinal) && json.Contains("Search", StringComparison.Ordinal),
        "A page should report visible control labels");
    Assert(typeof(BrowserElement).GetProperties().All(p => p.Name is not ("Value" or "Password" or "Html")),
        "Public DOM element contract must not return input contents or page HTML");
    Assert(snapshot.Elements?.Count == 2, "Control metadata count incorrect");
    return Task.CompletedTask;
}

static Task BrowserLinkStrictSchema()
{
    var tool = new PublicBrowserLinkTool(new BrowserJourneyReader());
    var valid = JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=0, expectedLinkUrl="https://www.example.com/about" });
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters, valid, out _), "Valid link payload rejected");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { sourceUrl="https://www.example.com/", linkIndex=0 }), out _),
        "Missing expected exact link destination must be rejected");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { sourceUrl="https://www.example.com/", linkIndex=0,
            expectedLinkUrl="https://www.example.com/about", allowWrites=true }), out _),
        "Unexpected property must be rejected");
    return Task.CompletedTask;
}

static Task BrowserLinkTargetGuard()
{
    var tool = new PublicBrowserLinkTool(new BrowserJourneyReader());
    static object Args(string source, int index, string target) => new {
        sourceUrl=source, linkIndex=index, expectedLinkUrl=target };
    string target = tool.GetTarget(JsonSerializer.SerializeToElement(
        Args("https://www.example.com/",0,"https://www.example.com/about")));
    Assert(target.Contains("https://www.example.com/about",StringComparison.Ordinal),
        "Authorization must display exact intended destination");
    foreach (var args in new[] {
        Args("http://www.example.com/",0,"https://www.example.com/about"),
        Args("https://www.example.com/",-1,"https://www.example.com/about"),
        Args("https://www.example.com/",24,"https://www.example.com/about"),
        Args("https://www.example.com/",0,"https://iana.org/help"),
        Args("https://www.example.com/",0,"http://www.example.com/about") })
    {
        bool denied=false;
        try { _ = tool.GetTarget(JsonSerializer.SerializeToElement(args)); }
        catch (ArgumentException) { denied=true; }
        Assert(denied, "Malformed, cross-host or out-of-bounds link target must fail before approval");
    }
    return Task.CompletedTask;
}

static async Task BrowserLinkDenialNeverVisits()
{
    using var setup = new TestContext();
    var reader = new BrowserJourneyReader();
    var tools = new ToolRegistry([new PublicBrowserLinkTool(reader)]);
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null, [Call("deny-link", "browser.follow_public_link", new {
            sourceUrl="https://www.example.com/", linkIndex=0,
            expectedLinkUrl="https://www.example.com/about" })])
    ]), tools, setup.Store, new WorkspaceAuthority(setup.Workspace));
    var auth = new FakeApprovals(false);
    var result = await engine.RunAsync(Guid.NewGuid(), "Follow the public example link", auth);
    Assert(result.Status == ConversationStatus.Paused && reader.Calls == 0 && auth.Calls == 1,
        "Declined link follow must pause before opening a browser");
}

static async Task BrowserLinkApprovedTwoPageObservation()
{
    using var setup = new TestContext();
    var reader = new BrowserJourneyReader();
    var tools = new ToolRegistry([new PublicBrowserLinkTool(reader)]);
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null, [Call("follow", "browser.follow_public_link", new {
            sourceUrl="https://www.example.com/", linkIndex=0,
            expectedLinkUrl="https://www.example.com/about" })]),
        new ModelReply("Visited the About page based on the recorded destination title.", [])
    ]), tools, setup.Store, new WorkspaceAuthority(setup.Workspace));
    var auth = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Follow the observed public link", auth);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && reader.Calls == 2 && auth.Calls == 1,
        "Approved link should visit source and destination once, under one specific authorization");
    Assert(state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains("About Page",StringComparison.Ordinal),
        "Audit receipt must contain actually observed destination data");
}

static async Task BrowserLinkChangedHrefStops()
{
    var reader = new BrowserJourneyReader { SourceLinkUrl="https://www.example.com/changed" };
    var tool = new PublicBrowserLinkTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" }), default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_LINK_CHANGED" && reader.Calls==1,
        "A changed link must never cause navigation to a different approved target");
}

static async Task BrowserLinkMissingIndexStops()
{
    var reader = new BrowserJourneyReader();
    var tool = new PublicBrowserLinkTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=5,
        expectedLinkUrl="https://www.example.com/about" }), default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_LINK_MISSING" && reader.Calls==1,
        "Missing index must not navigate to another URL");
}

static async Task BrowserLinkSourceRedirectStopped()
{
    var reader = new BrowserJourneyReader { SourceFinalUrl="https://iana.org/help" };
    var tool = new PublicBrowserLinkTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" }), default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_SOURCE_CHANGED_ORIGIN" && reader.Calls==1,
        "A source redirect off the approved host must be rejected before second visit");
}

static async Task BrowserLinkDestinationRedirectStopped()
{
    var reader = new BrowserJourneyReader { DestinationFinalUrl="https://iana.org/help" };
    var tool = new PublicBrowserLinkTool(reader);
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sourceUrl="https://www.example.com/", linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" }), default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_TARGET_CHANGED_ORIGIN" && reader.Calls==2,
        "Destination redirect must never yield a successful result");
}

static async Task BrowserLinkWireReceipt()
{
    using var setup = new TestContext();
    var browser = new BrowserJourneyReader();
    using var handler = new BrowserLinkWireHandler();
    using var http = new HttpClient(handler);
    var client = new OpenAiCompatibleModel(http, new Uri("https://fake.example/v1/"),
        "test-model", () => "wire-link-key");
    var engine = new NiraEngine(client, new ToolRegistry([new PublicBrowserLinkTool(browser)]),
        setup.Store, new WorkspaceAuthority(setup.Workspace));
    var output = await engine.RunAsync(Guid.NewGuid(), "Follow an observed same-host page link", new FakeApprovals(true));
    Assert(output.Status==ConversationStatus.Completed && handler.ObservedReceipt &&
        handler.PlanningRequests==1 && handler.Rounds==2 && browser.Calls==2,
        "Model HTTP transcript must contain real link observation and use one tool invocation");
}


static async Task BrowserFollowPermissionRoutes()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    var registry = new ToolRegistry([new BrowserSessionFollowTool(sessions)]);
    var model = new FakeModel([
        new ModelReply(null, [Call("permission", "nira_ask_user", new {
            question="Shall I follow the link to https://www.example.com/about?" })]),
        new ModelReply(null, [Call("follow", "browser.session_follow", new {
            sessionId=sessions.Id, expectedSourceUrl="https://www.example.com/",
            linkIndex=0, expectedLinkUrl="https://www.example.com/about" })]),
        new ModelReply("Observed the destination page after host approval.", [])
    ]);
    var engine = new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace));
    var approvals = new FakeApprovals(true);
    var result = await engine.RunAsync(Guid.NewGuid(), "Follow an observed link using my existing public browser session", approvals);
    Assert(result.Status==ConversationStatus.Completed && sessions.FollowCalls==1 && approvals.Calls==1,
        "A 'Shall I follow' permission probe must route to host-approved action without AwaitingUser");
}

static Task SessionSchemasAndDestinations()
{
    var sessions = new FakePublicBrowserSessions();
    var open = new BrowserSessionOpenTool(sessions);
    var follow = new BrowserSessionFollowTool(sessions);
    var inspect = new BrowserSessionInspectTool(sessions);
    var close = new BrowserSessionCloseTool(sessions);
    var valid = JsonSerializer.SerializeToElement(new { sessionId=sessions.Id,
        expectedSourceUrl="https://www.example.com/", linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" });
    Assert(JsonToolSchema.Validate(follow.Descriptor.Parameters,valid,out _), "Valid session-follow schema rejected");
    Assert(!JsonToolSchema.Validate(follow.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { sessionId=sessions.Id,
            expectedSourceUrl="https://www.example.com/",linkIndex=0 }), out _),
        "Destination must be required in session follow");
    Assert(!JsonToolSchema.Validate(open.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { url="https://www.example.com/", password="secret" }),out _),
        "Session open may not accept arbitrary inputs or credentials");
    foreach (var tool in new IAgentTool[] { inspect, close })
        Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
            JsonSerializer.SerializeToElement(new { sessionId=sessions.Id, arbitrary=true }),out _),
            "Session tools must reject unexpected parameters");
    Assert(follow.GetTarget(valid).Contains("https://www.example.com/about",StringComparison.Ordinal),
        "User approval must display exact link URL");
    Assert(open.Descriptor.Risk==ToolRisk.ReadExternal && follow.Descriptor.Risk==ToolRisk.ReadExternal,
        "Navigation must require host read-external approval");
    Assert(close.Descriptor.Risk==ToolRisk.ReadLocal, "Closing a session is a local cleanup");
    return Task.CompletedTask;
}

static async Task SessionOpenDenied()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("open","browser.session_open",new {url="https://www.example.com/"})])
    ]), new ToolRegistry([new BrowserSessionOpenTool(sessions)]), setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approver = new FakeApprovals(false);
    var answer=await engine.RunAsync(Guid.NewGuid(),"Open a public browser session",approver);
    Assert(answer.Status==ConversationStatus.Paused && sessions.OpenCalls==0 && approver.Calls==1,
        "A rejected browser session must not start a browser");
}

static async Task SessionOpenApproved()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("open","browser.session_open",new {url="https://www.example.com/"})]),
        new ModelReply("Opened the temporary browser session.",[])
    ]), new ToolRegistry([new BrowserSessionOpenTool(sessions), new BrowserSessionCloseTool(sessions)]), setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid conversation=Guid.NewGuid();
    var answer=await engine.RunAsync(conversation,"Open a public browser session",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(answer.Status==ConversationStatus.Completed && sessions.OpenCalls==1 &&
        sessions.CloseCalls==1 && sessions.Closed,
        "Approved session opening must run once, then close before completion");
    Assert(state.Operations.Count==2 && state.Operations.All(o=>o.Status==OperationStatus.Succeeded) &&
        state.Operations[0].ResultJson!.Contains(sessions.Id,StringComparison.Ordinal) &&
        state.Operations[1].ToolName=="browser.session_close",
        "Session opening and guaranteed cleanup must both have durable evidence");
}

static async Task SessionInspectUsesExisting()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionInspectTool(sessions);
    var first=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new{sessionId=sessions.Id}),default);
    var next=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new{sessionId=sessions.Id}),default);
    Assert(first.Success && next.Success && sessions.InspectCalls==2 && sessions.OpenCalls==0 &&
        sessions.FollowCalls==0 && first.Data.GetProperty("FinalUrl").GetString()=="https://www.example.com/",
        "Repeated inspection should reuse the open session without navigating");
}

static async Task SessionFollowDenied()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("follow","browser.session_follow",new {
            sessionId=sessions.Id, expectedSourceUrl="https://www.example.com/",
            linkIndex=0, expectedLinkUrl="https://www.example.com/about" })])
    ]),new ToolRegistry([new BrowserSessionFollowTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approver=new FakeApprovals(false);
    var result=await engine.RunAsync(Guid.NewGuid(),"Follow a public link",approver);
    Assert(result.Status==ConversationStatus.Paused && approver.Calls==1 && sessions.FollowCalls==0,
        "Denied navigation must pause without touching the session");
}

static async Task SessionFollowApproved()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("follow","browser.session_follow",new {
            sessionId=sessions.Id, expectedSourceUrl="https://www.example.com/",
            linkIndex=0, expectedLinkUrl="https://www.example.com/about" })]),
        new ModelReply("Observed About page within the same browser session.",[])
    ]),new ToolRegistry([new BrowserSessionFollowTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid conversation=Guid.NewGuid();
    var output=await engine.RunAsync(conversation,"Follow observed public link",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(output.Status==ConversationStatus.Completed && sessions.FollowCalls==1 && sessions.Current=="https://www.example.com/about",
        "Approved exact URL should navigate once and preserve the session ID");
    Assert(state.Operations[0].ResultJson!.Contains("About Page",StringComparison.Ordinal),
        "Observed destination page must be in the audit receipt");
}

static async Task SessionChangedLinkRefused()
{
    var sessions=new FakePublicBrowserSessions { ChangedLink=true };
    var tool=new BrowserSessionFollowTool(sessions);
    var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id, expectedSourceUrl="https://www.example.com/",linkIndex=0,
        expectedLinkUrl="https://www.example.com/about" }),default);
    Assert(!result.Success && result.ErrorCode=="BROWSER_SESSION_FOLLOW_FAILED" &&
        sessions.Current=="https://www.example.com/" && sessions.SuccessfulNavigations==0,
        "Changed links must not perform navigation");
}

static Task SessionCrossHostRejected()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionFollowTool(sessions);
    foreach(var args in new object[] {
        new { sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",linkIndex=0,
            expectedLinkUrl="https://other.example.org/" },
        new { sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",linkIndex=99,
            expectedLinkUrl="https://www.example.com/about" },
        new { sessionId="not-a-real-session",expectedSourceUrl="https://www.example.com/",linkIndex=0,
            expectedLinkUrl="https://www.example.com/about" } })
    {
        bool rejected=false;
        try { _=tool.GetTarget(JsonSerializer.SerializeToElement(args)); }
        catch(ArgumentException) { rejected=true; }
        Assert(rejected,"Cross-host, out-of-range, or malformed session navigation must fail before approval");
    }
    Assert(sessions.FollowCalls==0,"Rejected inputs must not invoke browser operations");
    return Task.CompletedTask;
}

static async Task SessionCloseAndExpired()
{
    var sessions=new FakePublicBrowserSessions();
    var close=new BrowserSessionCloseTool(sessions);
    var inspect=new BrowserSessionInspectTool(sessions);
    var args=JsonSerializer.SerializeToElement(new {sessionId=sessions.Id});
    var first=await close.ExecuteAsync(args,default);
    var again=await close.ExecuteAsync(args,default);
    var dead=await inspect.ExecuteAsync(args,default);
    Assert(first.Success && !again.Success && !dead.Success && sessions.CloseCalls==2,
        "Closing a session must discard it and prevent future inspection");
}

static Task TextEntryDomIndexVisible()
{
    var field = new BrowserElement("input", "", "Keyword", "search", "", false, 3);
    Assert(field.DomIndex == 3 && BrowserSessionFillTextTool.IsAllowedControl(field),
        "Observed fields must disclose the actual DOM index without reading their values");
    Assert(!typeof(BrowserElement).GetProperties().Any(p => p.Name is "Value" or "Password"),
        "Browser observation must not include input values or password content");
    return Task.CompletedTask;
}

static Task TextEntryTargetValidation()
{
    var tool = new BrowserSessionFillTextTool(new FakePublicBrowserSessions());
    var registry = new ToolRegistry([tool]);
    var good = new {sessionId="aabbccddeeff00112233445566778899",
        expectedPageUrl="https://www.example.com/",domIndex=2,
        expectedTag="input",expectedLabel="Keyword",text="hello world"};
    Assert(registry.Validate(tool.Descriptor.Name,JsonSerializer.Serialize(good),out _,out _),
        "Valid exact-target text entry should meet the tool schema");
    foreach (string args in new [] {
        "{\"sessionId\":\"aabbccddeeff00112233445566778899\",\"expectedPageUrl\":\"https://www.example.com/\",\"domIndex\":2,\"expectedTag\":\"input\",\"expectedLabel\":\"Keyword\",\"text\":\"hello\",\"extra\":true}",
        JsonSerializer.Serialize(new {good.sessionId,good.expectedPageUrl,domIndex=24,good.expectedTag,good.expectedLabel,good.text}),
        JsonSerializer.Serialize(new {good.sessionId,expectedPageUrl="http://www.example.com/",good.domIndex,good.expectedTag,good.expectedLabel,good.text}),
        JsonSerializer.Serialize(new {good.sessionId,good.expectedPageUrl,good.domIndex,expectedTag="button",good.expectedLabel,good.text}),
        JsonSerializer.Serialize(new {good.sessionId,good.expectedPageUrl,good.domIndex,good.expectedTag,expectedLabel="",good.text}),
    })
    {
        bool refused = !registry.Validate(tool.Descriptor.Name,args,out var values,out _);
        if (!refused) { try { tool.GetTarget(values); } catch (ArgumentException) { refused = true; } }
        Assert(refused,"Malformed field, private link or out-of-range index must be rejected before authorization");
    }
    string description = tool.GetTarget(JsonSerializer.SerializeToElement(good));
    Assert(description.Contains("hello world",StringComparison.Ordinal) &&
        description.Contains("DOM index 2",StringComparison.Ordinal) &&
        description.Contains("https://www.example.com/",StringComparison.Ordinal),
        "Approval must display exact supplied text, DOM index and destination page");
    return Task.CompletedTask;
}

static Task TextEntryDangerousRisk()
{
    var descriptor = new BrowserSessionFillTextTool(new FakePublicBrowserSessions()).Descriptor;
    Assert(descriptor.Risk == ToolRisk.Dangerous && descriptor.HasSideEffects,
        "Text entry can trigger scripts and must always require explicit approval and journaling");
    using var setup = new TestContext();
    Assert(!new WorkspaceAuthority(setup.Workspace).IsPreapproved(descriptor,"workspace"),
        "No text-entry action may be preapproved inside the workspace");
    return Task.CompletedTask;
}

static async Task TextEntryDenied()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("fill","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text="nonsecret test"})])
    ]),new ToolRegistry([new BrowserSessionFillTextTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approvals = new FakeApprovals(false);
    Guid id=Guid.NewGuid();
    var output=await engine.RunAsync(id,"Fill the search field",approvals);
    Assert(output.Status==ConversationStatus.Paused && approvals.Calls==1 && sessions.FillCalls==0,
        "Denied action must pause without sending a keystroke");
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Rejected &&
        state.Operations[0].ErrorCode=="NOT_AUTHORIZED", "Denial must be durably recorded");
}

static async Task TextEntryApprovedAndRedacted()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions();
    const string value="ordinary search phrase";
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("fill","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text=value})]),
        new ModelReply("Entered text into the public search input without submitting.",[])
    ]),new ToolRegistry([new BrowserSessionFillTextTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var output=await engine.RunAsync(id,"Fill a public search field",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(output.Status==ConversationStatus.Completed && sessions.FillCalls==1,
        "An approved fill should call the selected field once and finish with observed evidence");
    Assert(state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains("CharactersEntered",StringComparison.Ordinal) &&
        !state.Operations[0].ResultJson!.Contains(value,StringComparison.Ordinal) &&
        output.Message.Contains("browser.session_fill_text",StringComparison.Ordinal),
        "Receipt must contain field verification and character count, but not entered text");
}

static async Task TextEntryStaleLabel()
{
    var sessions = new FakePublicBrowserSessions { ChangedLabel=true };
    var tool = new BrowserSessionFillTextTool(sessions);
    var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
        expectedTag="input",expectedLabel="Keyword",text="safe" }),default);
    Assert(!result.Success && result.ErrorCode=="BROWSER_TEXT_CONTROL_REFUSED" &&
        sessions.SuccessfulEntries==0,"Changed label should be refused before any fill");
}

static async Task TextEntryPasswordAndDisabled()
{
    foreach (bool password in new [] {true,false})
    {
        var sessions = new FakePublicBrowserSessions { PasswordField=password, DisabledField=!password };
        var tool = new BrowserSessionFillTextTool(sessions);
        var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text="safe" }),default);
        Assert(!result.Success && sessions.SuccessfulEntries==0,
            "Password or disabled fields must reject text entry without modifying the page");
    }
}

static async Task TextEntryAfterClose()
{
    var sessions = new FakePublicBrowserSessions();
    _=await new BrowserSessionCloseTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new {sessionId=sessions.Id}),default);
    var result=await new BrowserSessionFillTextTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            domIndex=2,expectedTag="input",expectedLabel="Keyword",text="safe"}),default);
    Assert(!result.Success && sessions.SuccessfulEntries==0,
        "Closed or expired sessions must not allow text entry");
}

static async Task TextEntryRepeatedFills()
{
    var sessions = new FakePublicBrowserSessions();
    var tool = new BrowserSessionFillTextTool(sessions);
    var first = await tool.ExecuteAsync(TextArgs(sessions, 2, "Keyword", "first"), default);
    var second = await tool.ExecuteAsync(TextArgs(sessions, 4, "Second keyword", "second"), default);
    Assert(first.Success && second.Success && sessions.SuccessfulEntries==2 &&
        sessions.Actions.VerifiedActions==2 && sessions.NetworkBlocked,
        "Two independently approved fields may be filled in one session while network stays blocked");
}

static async Task TextEntryUncertainReconciliation()
{
    using var setup = new TestContext();
    var sessions = new FakePublicBrowserSessions { ThrowUncertain=true };
    var engine = new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("fill","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text="safe" })])
    ]),new ToolRegistry([new BrowserSessionFillTextTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var answer=await engine.RunAsync(id,"Fill public text field",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(answer.Status==ConversationStatus.NeedsReconciliation &&
        state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Unknown,
        "Uncertain browser effect must not be reported as failed/no-change or silently retried");
}

static Task ButtonClickStrictSchema()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionClickButtonTool(sessions);
    var valid=ButtonArgs(sessions);
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters,valid,out _),
        "Exact native button schema should be valid");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
        expectedType="button",expectedLabel="Show more",expectedText="Expand details",script="alert(1)"}),out _),
        "Arbitrary selectors and script fields must not be allowed");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
        expectedType="submit",expectedLabel="Show more",expectedText="Expand details"}),out _),
        "Submit button type must fail schema validation");
    return Task.CompletedTask;
}

static JsonElement ButtonArgs(FakePublicBrowserSessions sessions) =>
    JsonSerializer.SerializeToElement(new { sessionId=sessions.Id,expectedPageUrl=sessions.Current,
        domIndex=3,expectedTag="button",expectedType="button",
        expectedLabel="Show more",expectedText="Expand details" });

static Task ButtonClickTargetGuards()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionClickButtonTool(sessions);
    foreach(var args in new object[] {
        new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=99,
            expectedTag="button",expectedType="button",expectedLabel="Show more",expectedText="Expand details"},
        new {sessionId=sessions.Id,expectedPageUrl="http://www.example.com/",domIndex=3,
            expectedTag="button",expectedType="button",expectedLabel="Show more",expectedText="Expand details"},
        new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,
            expectedTag="input",expectedType="button",expectedLabel="Show more",expectedText="Expand details"},
        new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,
            expectedTag="button",expectedType="submit",expectedLabel="Show more",expectedText="Expand details"},
        new {sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,
            expectedTag="button",expectedType="button",expectedLabel="",expectedText=""} })
    {
        bool failed=false;
        try { _=tool.GetTarget(JsonSerializer.SerializeToElement(args)); }
        catch (ArgumentException) { failed=true; }
        Assert(failed,"Non-public URL, absent target, submit type and invalid labels must fail before approval");
    }
    Assert(sessions.ClickCalls==0,"Invalid targets must never click");
    return Task.CompletedTask;
}

static Task ButtonClickDangerousApproval()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionClickButtonTool(sessions);
    string preview=tool.GetTarget(ButtonArgs(sessions));
    Assert(tool.Descriptor.Risk==ToolRisk.Dangerous && tool.Descriptor.HasSideEffects,
        "DOM click requires Dangerous approval and a durable effect receipt");
    Assert(preview.Contains(sessions.Current,StringComparison.Ordinal) &&
        preview.Contains("DOM index 3",StringComparison.Ordinal) &&
        preview.Contains("Show more",StringComparison.Ordinal) &&
        preview.Contains("Expand details",StringComparison.Ordinal) &&
        preview.Contains("WARNING",StringComparison.Ordinal),
        "Approval must identify exact page/button, and explain script side effects");
    Assert(BrowserSessionClickButtonTool.IsAllowedControl(new BrowserElement(
        "button","","Show more","button","Expand details",false,3)),
        "Native type=button is permitted");
    Assert(!BrowserSessionClickButtonTool.IsAllowedControl(new BrowserElement(
        "button","","Delete","submit","Delete",false,3)),
        "Submit button is not permitted");
    return Task.CompletedTask;
}

static async Task ButtonClickDenied()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("click","browser.session_click_button",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
            expectedType="button",expectedLabel="Show more",expectedText="Expand details"})])
    ]),new ToolRegistry([new BrowserSessionClickButtonTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approver=new FakeApprovals(false);
    Guid conversation=Guid.NewGuid();
    var response=await engine.RunAsync(conversation,"Click a native demo button",approver);
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(response.Status==ConversationStatus.Paused && approver.Calls==1 &&
        sessions.ClickCalls==0 && state.Operations.Count==1 &&
        state.Operations[0].Status==OperationStatus.Rejected,
        "Denied click must not touch the DOM and must preserve the denial");
}

static async Task ButtonClickApproved()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("click","browser.session_click_button",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
            expectedType="button",expectedLabel="Show more",expectedText="Expand details"})]),
        new ModelReply("Approved native demo button was clicked; no navigation observed.",[])
    ]),new ToolRegistry([new BrowserSessionClickButtonTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid conversation=Guid.NewGuid();
    var response=await engine.RunAsync(conversation,"Click a native demo button",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(response.Status==ConversationStatus.Completed && sessions.SuccessfulClicks==1 &&
        state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains("ElementText",StringComparison.Ordinal) &&
        response.Message.Contains("browser.session_click_button",StringComparison.Ordinal),
        "Approved click must produce a recorded page observation and host evidence");
}

static async Task ButtonClickStaleMetadata()
{
    var sessions=new FakePublicBrowserSessions { ChangedButtonText=true };
    var result=await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
    Assert(!result.Success && result.ErrorCode=="BROWSER_BUTTON_CLICK_REFUSED" &&
        sessions.SuccessfulClicks==0,
        "Changed text or stale metadata must be rejected before clicking");
}

static async Task ButtonClickSubmitAndDisabled()
{
    foreach (bool isSubmit in new[]{true,false})
    {
        var sessions=new FakePublicBrowserSessions { ButtonSubmit=isSubmit,ButtonDisabled=!isSubmit };
        var result=await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
        Assert(!result.Success && sessions.SuccessfulClicks==0,
            "Form submit/reset and disabled buttons are not permitted");
    }
}

static async Task ButtonClickClosedSession()
{
    var sessions=new FakePublicBrowserSessions();
    _=await new BrowserSessionCloseTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new{sessionId=sessions.Id}),default);
    var result=await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
    Assert(!result.Success && sessions.SuccessfulClicks==0,
        "Closed browser session must not be implicitly reopened");
}

static async Task ButtonClickSequential()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionClickButtonTool(sessions);
    var first=await tool.ExecuteAsync(ButtonArgs(sessions),default);
    var second=await tool.ExecuteAsync(ButtonArgs(sessions),default);
    var fillAfterClick=await new BrowserSessionFillTextTool(sessions).ExecuteAsync(
        TextArgs(sessions,2,"Keyword","safe"),default);
    Assert(first.Success && second.Success && fillAfterClick.Success &&
        sessions.SuccessfulClicks==2 && sessions.SuccessfulEntries==1 &&
        sessions.Actions.VerifiedActions==3 && sessions.NetworkBlocked,
        "Multiple verified click/fill operations are possible with distinct host approvals");
    var other=new FakePublicBrowserSessions();
    var filled=await new BrowserSessionFillTextTool(other).ExecuteAsync(
        TextArgs(other,2,"Keyword","safe"),default);
    var clicked=await new BrowserSessionClickButtonTool(other).ExecuteAsync(ButtonArgs(other),default);
    Assert(filled.Success && clicked.Success && other.SuccessfulClicks==1,
        "A verified text entry can be followed by a separately approved button click");
}

static async Task ButtonClickUncertainReconciliation()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions { ThrowClickUncertain=true };
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("click","browser.session_click_button",new{
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,expectedTag="button",
            expectedType="button",expectedLabel="Show more",expectedText="Expand details"})])
    ]),new ToolRegistry([new BrowserSessionClickButtonTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid conversation=Guid.NewGuid();
    var output=await engine.RunAsync(conversation,"Click demo button",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(conversation,default))!;
    Assert(output.Status==ConversationStatus.NeedsReconciliation &&
        state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Unknown,
        "An uncertain click is not a clean failure and must never be retried silently");
}

static JsonElement TextArgs(FakePublicBrowserSessions sessions, int index, string label, string value) =>
    JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id, expectedPageUrl=sessions.Current, domIndex=index,
        expectedTag="input", expectedLabel=label, text=value
    });

static Task MultiStateDefault()
{
    var state = new BrowserInteractionSequence();
    Assert(state.VerifiedActions==0 && !state.RequiresReconciliation,
        "Unmodified state must be empty and ready");
    state.EnsureReady();
    bool invalidVerify=false;
    try { state.Verify(); } catch (InvalidOperationException) { invalidVerify=true; }
    Assert(invalidVerify && state.VerifiedActions==0,"Cannot verify an action that was not dispatched");
    return Task.CompletedTask;
}

static Task MultiStateUncertain()
{
    var state = new BrowserInteractionSequence();
    state.Begin();
    Assert(state.RequiresReconciliation && state.VerifiedActions==0,
        "Dispatch must enter the uncertain state before effects");
    bool refused=false;
    try { state.Begin(); } catch (InvalidOperationException) { refused=true; }
    Assert(refused,"A second dispatch cannot proceed until observation confirms prior action");
    state.Verify();
    state.Begin(); state.Verify();
    Assert(!state.RequiresReconciliation && state.VerifiedActions==2,
        "Successful verification permits a subsequent separate interaction");
    return Task.CompletedTask;
}

static Task MultiStateBound()
{
    var state = new BrowserInteractionSequence();
    for (int i=0; i<BrowserInteractionSequence.MaximumVerifiedActions; i++) {
        state.Begin(); state.Verify();
    }
    bool refused=false;
    try { state.Begin(); } catch (InvalidOperationException) { refused=true; }
    Assert(refused && state.VerifiedActions==BrowserInteractionSequence.MaximumVerifiedActions,
        "The bounded session must refuse a ninth interactive action");
    return Task.CompletedTask;
}

static async Task MultiApprovalEachAction()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var actions=new FakeModel([
        new ModelReply(null,[Call("field1","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text="NIRA FIRST"})]),
        new ModelReply(null,[Call("field2","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=4,
            expectedTag="input",expectedLabel="Second keyword",text="NIRA SECOND"})]),
        new ModelReply(null,[Call("button","browser.session_click_button",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,
            expectedTag="button",expectedType="button",expectedLabel="Show more",
            expectedText="Expand details"})]),
        new ModelReply("Three distinct approved changes were observed on the same page.",[])
    ]);
    var engine=new NiraEngine(actions,new ToolRegistry([
        new BrowserSessionFillTextTool(sessions),new BrowserSessionClickButtonTool(sessions)]),
        setup.Store,new WorkspaceAuthority(setup.Workspace));
    var approval=new FakeApprovals(true);
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Fill two public fields, then toggle the demo button",approval);
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && approval.Calls==3 &&
        sessions.SuccessfulEntries==2 && sessions.SuccessfulClicks==1 &&
        state.Operations.Count==3 && state.Operations.All(o=>o.Status==OperationStatus.Succeeded),
        "Three distinct actions must produce three individual approvals and audited receipts");
}

static async Task MultiDeniedFollowup()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var model=new FakeModel([
        new ModelReply(null,[Call("one","browser.session_fill_text",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=2,
            expectedTag="input",expectedLabel="Keyword",text="approved"})]),
        new ModelReply(null,[Call("two","browser.session_click_button",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,domIndex=3,
            expectedTag="button",expectedType="button",expectedLabel="Show more",
            expectedText="Expand details"})])
    ]);
    var gate=new ScriptedApprovals([true,false]);
    var engine=new NiraEngine(model,new ToolRegistry([
        new BrowserSessionFillTextTool(sessions),new BrowserSessionClickButtonTool(sessions)]),
        setup.Store,new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Fill, then click",gate);
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Paused && gate.Calls==2 &&
        sessions.SuccessfulEntries==1 && sessions.SuccessfulClicks==0 &&
        state.Operations.Count==2 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[1].Status==OperationStatus.Rejected,
        "The second denied action must not roll back or re-authorize the first action");
}

static async Task MultiTwoFieldsAndButton()
{
    var sessions=new FakePublicBrowserSessions();
    var fill=new BrowserSessionFillTextTool(sessions);
    var click=new BrowserSessionClickButtonTool(sessions);
    Assert((await fill.ExecuteAsync(TextArgs(sessions,2,"Keyword","alpha"),default)).Success,
        "First field should be filled");
    Assert((await fill.ExecuteAsync(TextArgs(sessions,4,"Second keyword","beta"),default)).Success,
        "Second observed field should also be fillable without reopening browser");
    Assert((await click.ExecuteAsync(ButtonArgs(sessions),default)).Success,
        "Third approved button should work in the same browser session");
    Assert(sessions.Actions.VerifiedActions==3 && !sessions.Actions.RequiresReconciliation,
        "Multi-action workflow should be verified after each operation");
}

static async Task MultiStaleSecondControl()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionFillTextTool(sessions);
    Assert((await tool.ExecuteAsync(TextArgs(sessions,2,"Keyword","alpha"),default)).Success,
        "Baseline initial field should succeed");
    sessions.ChangedSecondLabel=true;
    var result=await tool.ExecuteAsync(TextArgs(sessions,4,"Second keyword","beta"),default);
    Assert(!result.Success && sessions.SuccessfulEntries==1 &&
        sessions.Actions.VerifiedActions==1 && !sessions.Actions.RequiresReconciliation,
        "A changed second control must be refused without changing verified state");
}

static async Task MultiNetworkLock()
{
    var sessions=new FakePublicBrowserSessions();
    Assert((await new BrowserSessionFillTextTool(sessions).ExecuteAsync(
        TextArgs(sessions,2,"Keyword","alpha"),default)).Success, "First entry failed");
    var follow=await new BrowserSessionFollowTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new{sessionId=sessions.Id,
            expectedSourceUrl=sessions.Current,linkIndex=0,
            expectedLinkUrl="https://www.example.com/about"}),default);
    Assert(!follow.Success && sessions.NetworkBlocked && sessions.SuccessfulNavigations==0,
        "Further HTTP navigation stays blocked, even after an observed successful interaction");
}

static async Task MultiRefuseSubmit()
{
    var sessions=new FakePublicBrowserSessions();
    var fill=new BrowserSessionFillTextTool(sessions);
    Assert((await fill.ExecuteAsync(TextArgs(sessions,2,"Keyword","alpha"),default)).Success,
        "First entry failed");
    sessions.ButtonSubmit=true;
    var result=await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
    Assert(!result.Success && sessions.SuccessfulClicks==0 && sessions.Actions.VerifiedActions==1,
        "A button becoming a submit control must be refused after previous actions");
}

static async Task MultiUncertainThenRefuse()
{
    var sessions=new FakePublicBrowserSessions();
    var fill=new BrowserSessionFillTextTool(sessions);
    Assert((await fill.ExecuteAsync(TextArgs(sessions,2,"Keyword","alpha"),default)).Success,
        "The first verified action failed");
    sessions.ThrowClickUncertain=true;
    bool uncertain=false;
    try { await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default); }
    catch (BrowserInteractionUncertainException) { uncertain=true; }
    Assert(uncertain && sessions.Actions.RequiresReconciliation,
        "An uncertain second action must lock the session");
    sessions.ThrowClickUncertain=false;
    var retry=await fill.ExecuteAsync(TextArgs(sessions,4,"Second keyword","beta"),default);
    Assert(!retry.Success && sessions.SuccessfulEntries==1,
        "No third interaction is permitted after an uncertain effect");
}

static async Task MultiCloseAndExpire()
{
    var sessions=new FakePublicBrowserSessions();
    var fill=new BrowserSessionFillTextTool(sessions);
    Assert((await fill.ExecuteAsync(TextArgs(sessions,2,"Keyword","alpha"),default)).Success,
        "First action failed");
    Assert((await new BrowserSessionCloseTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new{sessionId=sessions.Id}),default)).Success,
        "Close must succeed after a verified action");
    var denied=await fill.ExecuteAsync(TextArgs(sessions,4,"Second keyword","beta"),default);
    Assert(!denied.Success && sessions.SuccessfulEntries==1,
        "Closed sessions cannot be reused for a later action");
}

static async Task MultiReceiptsSeparate()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionFillTextTool(sessions);
    var first=await tool.ExecuteAsync(TextArgs(sessions,2,"Keyword","secretless-alpha"),default);
    var second=await tool.ExecuteAsync(TextArgs(sessions,4,"Second keyword","secretless-beta"),default);
    Assert(first.Success && second.Success &&
        !JsonSerializer.Serialize(first).Contains("secretless-alpha",StringComparison.Ordinal) &&
        !JsonSerializer.Serialize(second).Contains("secretless-beta",StringComparison.Ordinal),
        "Receipts must contain independent observations without disclosing typed values");
}

static BrowserSnapshot SnapshotWithControl(BrowserElement? element)
{
    return new BrowserSnapshot("https://www.example.com/", "https://www.example.com/",
        "Sample", 200, "Sample visible text", false, Array.Empty<BrowserLink>(),
        element is null ? Array.Empty<BrowserElement>() : new[] { element });
}

static BrowserElement AccessibleButton(bool? expanded = null, bool? pressed = null,
    bool? checkedState = null, string label = "Toggle details", string text = "Expand") =>
    new("button", "", label, "button", text, false, 3, expanded, pressed, checkedState);

static Task AccessibleExpandedTransition()
{
    var evidence = BrowserButtonStateEvidence.Compare(
        AccessibleButton(expanded: false), SnapshotWithControl(AccessibleButton(expanded: true)));
    Assert(evidence.MatchingControlObserved && evidence.VerifiedStateChange &&
        evidence.ExpandedBefore == false && evidence.ExpandedAfter == true,
        "Only an actually observed false-to-true aria-expanded transition proves expansion");
    return Task.CompletedTask;
}

static Task AccessibleUnchangedState()
{
    var evidence = BrowserButtonStateEvidence.Compare(
        AccessibleButton(expanded: false), SnapshotWithControl(AccessibleButton(expanded: false)));
    Assert(evidence.MatchingControlObserved && !evidence.VerifiedStateChange &&
        evidence.ExpandedAfter == false,
        "Click dispatch cannot imply expansion if aria-expanded remains false");
    return Task.CompletedTask;
}

static Task AccessibleMissingBefore()
{
    var evidence = BrowserButtonStateEvidence.Compare(
        AccessibleButton(), SnapshotWithControl(AccessibleButton(expanded: true)));
    Assert(evidence.MatchingControlObserved && !evidence.VerifiedStateChange &&
        evidence.ExpandedBefore is null && evidence.ExpandedAfter == true,
        "Previously unobserved aria state cannot become invented before-state evidence");
    return Task.CompletedTask;
}

static Task AccessibleIdentityChanged()
{
    var evidence = BrowserButtonStateEvidence.Compare(
        AccessibleButton(expanded: false),
        SnapshotWithControl(AccessibleButton(expanded: true, text: "Different")));
    Assert(!evidence.MatchingControlObserved && !evidence.VerifiedStateChange &&
        evidence.ExpandedAfter is null,
        "Changed control identity metadata invalidates comparable state evidence");
    return Task.CompletedTask;
}

static Task AccessiblePressedTransition()
{
    var evidence = BrowserButtonStateEvidence.Compare(
        AccessibleButton(pressed: false), SnapshotWithControl(AccessibleButton(pressed: true)));
    Assert(evidence.MatchingControlObserved && evidence.VerifiedStateChange &&
        evidence.PressedBefore == false && evidence.PressedAfter == true,
        "General controls must verify aria-pressed without an application-specific rule");
    return Task.CompletedTask;
}

static Task AccessibleCheckedTransition()
{
    var evidence = BrowserButtonStateEvidence.Compare(
        AccessibleButton(checkedState: false), SnapshotWithControl(AccessibleButton(checkedState: true)));
    Assert(evidence.MatchingControlObserved && evidence.VerifiedStateChange &&
        evidence.CheckedBefore == false && evidence.CheckedAfter == true,
        "Generic state comparison must support aria-checked independently of expansion");
    return Task.CompletedTask;
}

static Task AccessibleControlDisappeared()
{
    var evidence = BrowserButtonStateEvidence.Compare(
        AccessibleButton(expanded: false), SnapshotWithControl(null));
    Assert(!evidence.MatchingControlObserved && !evidence.VerifiedStateChange &&
        evidence.ExpandedAfter is null,
        "Removed controls do not produce fabricated post-click state");
    return Task.CompletedTask;
}

static Task AccessibleStateMetadataOnly()
{
    var value = AccessibleButton(expanded: true, pressed: false, checkedState: null);
    string json = JsonSerializer.Serialize(value);
    Assert(json.Contains("AriaExpanded",StringComparison.Ordinal) &&
        json.Contains("AriaPressed",StringComparison.Ordinal) &&
        !typeof(BrowserElement).GetProperties().Any(x => x.Name is "Value" or "Password" or "OuterHtml"),
        "Inspection must expose only bounded accessible states, not field values or markup");
    return Task.CompletedTask;
}

static async Task ButtonClickReceiptWithTransition()
{
    var sessions = new FakePublicBrowserSessions();
    var result = await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
    var evidence = result.Data.GetProperty("AccessibleState");
    Assert(result.Success && result.ChangedState &&
        evidence.GetProperty("MatchingControlObserved").GetBoolean() &&
        evidence.GetProperty("VerifiedStateChange").GetBoolean() &&
        !evidence.GetProperty("ExpandedBefore").GetBoolean() &&
        evidence.GetProperty("ExpandedAfter").GetBoolean() &&
        result.Summary.Contains("Observed an accessible-state transition",StringComparison.Ordinal),
        "A verified accessibility change must be included in the actual tool receipt");
}

static async Task ButtonClickReceiptWithoutState()
{
    var sessions = new FakePublicBrowserSessions { ButtonAriaAvailable = false };
    var result = await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
    var evidence = result.Data.GetProperty("AccessibleState");
    Assert(result.Success && !evidence.GetProperty("VerifiedStateChange").GetBoolean() &&
        evidence.GetProperty("ExpandedBefore").ValueKind == JsonValueKind.Null &&
        evidence.GetProperty("ExpandedAfter").ValueKind == JsonValueKind.Null &&
        result.Summary.Contains("No accessible state change",StringComparison.Ordinal),
        "Success means clicked, not accessibility change proven, when markup has no state");
}

static async Task ButtonClickReceiptWithoutChange()
{
    var sessions = new FakePublicBrowserSessions { ButtonChangesState = false };
    var result = await new BrowserSessionClickButtonTool(sessions).ExecuteAsync(ButtonArgs(sessions),default);
    var evidence = result.Data.GetProperty("AccessibleState");
    Assert(result.Success && !evidence.GetProperty("VerifiedStateChange").GetBoolean() &&
        result.Summary.Contains("No accessible state change",StringComparison.Ordinal),
        "The host must not falsely attest an unchanged UI after a successful click");
}

static async Task ButtonClickSequentialStateEvidence()
{
    var sessions = new FakePublicBrowserSessions();
    var tool = new BrowserSessionClickButtonTool(sessions);
    var first = await tool.ExecuteAsync(ButtonArgs(sessions),default);
    var second = await tool.ExecuteAsync(ButtonArgs(sessions),default);
    var firstState=first.Data.GetProperty("AccessibleState");
    var secondState=second.Data.GetProperty("AccessibleState");
    Assert(first.Success && second.Success && sessions.SuccessfulClicks==2 &&
        firstState.GetProperty("ExpandedBefore").GetBoolean() == false &&
        firstState.GetProperty("ExpandedAfter").GetBoolean() == true &&
        secondState.GetProperty("ExpandedBefore").GetBoolean() == true &&
        secondState.GetProperty("ExpandedAfter").GetBoolean() == false &&
        firstState.GetProperty("VerifiedStateChange").GetBoolean() &&
        secondState.GetProperty("VerifiedStateChange").GetBoolean(),
        "Each approved action must retain a separate observed before-and-after state");
}

static Task SemanticRoleMapping()
{
    Assert(BrowserSemanticControl.RoleFor("button", "button", "") == "button" &&
        BrowserSemanticControl.RoleFor("input", "search", "") == "searchbox" &&
        BrowserSemanticControl.RoleFor("input", "text", "") == "textbox" &&
        BrowserSemanticControl.RoleFor("textarea", "", "") == "textbox" &&
        BrowserSemanticControl.RoleFor("input", "password", "") == "" &&
        BrowserSemanticControl.RoleFor("button", "button", "switch") == "switch",
        "Native roles and explicit aria-role must not be confused");
    return Task.CompletedTask;
}

static Task SemanticUniqueNamedControl()
{
    var candidates = new[] {
        new BrowserElement("button", "", "", "button", "Open", false, 4,
            AccessibleName:"Open", SemanticRole:"button"),
        new BrowserElement("button", "", "", "button", "Close", false, 5,
            AccessibleName:"Close", SemanticRole:"button") };
    var match = BrowserSemanticControl.RequireUnique(candidates, "click", "button", "Close");
    Assert(match.DomIndex == 5 && match.AccessibleName == "Close",
        "Semantic name must select exact element independent of index");
    return Task.CompletedTask;
}

static Task SemanticDuplicateRefused()
{
    var candidates = new[] {
        new BrowserElement("button", "", "", "button", "Open", false, 0,
            AccessibleName:"Open", SemanticRole:"button"),
        new BrowserElement("button", "", "", "button", "Open", false, 13,
            AccessibleName:"Open", SemanticRole:"button") };
    bool rejected = false;
    try { _ = BrowserSemanticControl.RequireUnique(candidates, "click", "button", "Open"); }
    catch (InvalidOperationException) { rejected = true; }
    Assert(rejected, "Ambiguous role/name must fail closed rather than selecting the first element");
    return Task.CompletedTask;
}

static Task SemanticUnsafeTargetsRefused()
{
    var forbidden = new[] {
        new BrowserElement("input","","Password","password","",false,0,
            AccessibleName:"Password",SemanticRole:"textbox"),
        new BrowserElement("button","","Submit","submit","Submit",false,1,
            AccessibleName:"Submit",SemanticRole:"button"),
        new BrowserElement("button","","Hidden","button","Hidden",true,2,
            AccessibleName:"Hidden",SemanticRole:"button")};
    Assert(forbidden.All(e => !BrowserSemanticControl.IsAllowed(e.Tag=="input"?"fill":"click",
        e.SemanticRole,e)),"Password, submit and disabled controls must be rejected");
    return Task.CompletedTask;
}

static Task SemanticStrictSchemaAndApprovalDetails()
{
    var tool = new BrowserSessionSemanticActionTool(new FakePublicBrowserSessions());
    var registry = new ToolRegistry([tool]);
    var safe = new { sessionId="aabbccddeeff00112233445566778899",
        expectedPageUrl="https://www.example.com/", action="fill", role="searchbox",
        accessibleName="Keyword", text="safe public query" };
    Assert(registry.Validate(tool.Descriptor.Name,JsonSerializer.Serialize(safe),out _,out _),
        "Valid semantic action should pass schema");
    Assert(!registry.Validate(tool.Descriptor.Name,JsonSerializer.Serialize(new {
        safe.sessionId,safe.expectedPageUrl,safe.action,safe.role,safe.accessibleName,
        safe.text, unrestricted=true}),out _,out _),
        "Unknown arguments cannot be accepted");
    string target=tool.GetTarget(JsonSerializer.SerializeToElement(safe));
    Assert(target.Contains("safe public query",StringComparison.Ordinal) &&
        target.Contains("Keyword",StringComparison.Ordinal) &&
        target.Contains("https://www.example.com/",StringComparison.Ordinal) &&
        tool.Descriptor.Risk==ToolRisk.Dangerous && tool.Descriptor.HasSideEffects,
        "Approval must show the exact name/text/page and classify the action Dangerous");
    return Task.CompletedTask;
}

static Task SemanticInvalidActionCombinations()
{
    var tool = new BrowserSessionSemanticActionTool(new FakePublicBrowserSessions());
    object[] bad = [
        new {sessionId="aabbccddeeff00112233445566778899",expectedPageUrl="https://www.example.com/",
            action="click",role="textbox",accessibleName="Keyword"},
        new {sessionId="aabbccddeeff00112233445566778899",expectedPageUrl="https://www.example.com/",
            action="fill",role="button",accessibleName="Show more",text="x"},
        new {sessionId="aabbccddeeff00112233445566778899",expectedPageUrl="https://www.example.com/",
            action="fill",role="textbox",accessibleName="Second keyword"},
        new {sessionId="aabbccddeeff00112233445566778899",expectedPageUrl="https://www.example.com/",
            action="click",role="button",accessibleName="Show more",text="wrong"}
    ];
    foreach (object input in bad)
    {
        bool rejected=false;
        try { _=tool.GetTarget(JsonSerializer.SerializeToElement(input)); }
        catch (ArgumentException) { rejected=true; }
        Assert(rejected,"Action/role mismatch or surplus value must fail before host approval");
    }
    return Task.CompletedTask;
}

static async Task SemanticDenialPreventsDispatch()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("semantic-deny","browser.session_semantic_action",new {
            sessionId=sessions.Id, expectedPageUrl=sessions.Current,
            action="click",role="button",accessibleName="Show more"})])
    ]),new ToolRegistry([new BrowserSessionSemanticActionTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approvals=new FakeApprovals(false);
    var output=await engine.RunAsync(Guid.NewGuid(),"Click the uniquely named button",approvals);
    Assert(output.Status==ConversationStatus.Paused && approvals.Calls==1 &&
        sessions.SemanticCalls==0 && sessions.SuccessfulClicks==0,
        "Denied semantic action cannot dispatch or automatically retry");
}

static async Task SemanticApprovedClickEvidence()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("semantic-click","browser.session_semantic_action",new {
            sessionId=sessions.Id, expectedPageUrl=sessions.Current,
            action="click",role="button",accessibleName="Show more"})]),
        new ModelReply("Button state was observed.",[])
    ]),new ToolRegistry([new BrowserSessionSemanticActionTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var output=await engine.RunAsync(id,"Toggle a public button by its accessible name",new FakeApprovals(true));
    var saved=(await setup.Store.LoadAsync(id,default))!;
    Assert(output.Status==ConversationStatus.Completed && sessions.SemanticCalls==1 &&
        sessions.SuccessfulClicks==1 && saved.Operations.Count==1 &&
        saved.Operations[0].ResultJson!.Contains("VerifiedStateChange",StringComparison.Ordinal),
        "Approved semantic click must record the observed transition in the journal");
}

static async Task SemanticApprovedFillRedactedReceipt()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionSemanticActionTool(sessions);
    const string publicText="semantic field query";
    var receipt=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,
        action="fill",role="searchbox",accessibleName="Keyword",text=publicText}),default);
    Assert(receipt.Success && sessions.SemanticCalls==1 && sessions.SuccessfulEntries==1 &&
        receipt.Data.GetProperty("CharactersEntered").GetInt32()==publicText.Length &&
        !receipt.Data.GetRawText().Contains(publicText,StringComparison.Ordinal),
        "Semantic fill receipt must verify a count without echoing entered content");
}

static async Task SemanticChangedNameRejected()
{
    var sessions=new FakePublicBrowserSessions { ChangedLabel=true };
    var tool=new BrowserSessionSemanticActionTool(sessions);
    var receipt=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,
        action="fill",role="searchbox",accessibleName="Keyword",text="public"}),default);
    Assert(!receipt.Success && receipt.ErrorCode=="BROWSER_SEMANTIC_TARGET_REFUSED" &&
        sessions.SuccessfulEntries==0,"Stale semantic names must not dispatch to different fields");
}

static async Task SemanticUncertainEffectStopsAgent()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions { ThrowClickUncertain=true };
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("uncertain","browser.session_semantic_action",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            action="click",role="button",accessibleName="Show more"})])
    ]),new ToolRegistry([new BrowserSessionSemanticActionTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Try one named button",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.NeedsReconciliation &&
        state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Unknown,
        "Uncertain browser clicks require reconciliation and never complete silently");
}

static async Task SemanticMultiStepIndependentApproval()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var model=new FakeModel([
        new ModelReply(null,[Call("sem-fill","browser.session_semantic_action",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            action="fill",role="searchbox",accessibleName="Keyword",text="one"})]),
        new ModelReply(null,[Call("sem-click","browser.session_semantic_action",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            action="click",role="button",accessibleName="Show more"})]),
        new ModelReply("Both separately approved actions were observed.",[])
    ]);
    var approvals=new FakeApprovals(true);
    var engine=new NiraEngine(model,new ToolRegistry([new BrowserSessionSemanticActionTool(sessions)]),
        setup.Store,new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Fill a named searchbox and click a named button",approvals);
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && approvals.Calls==2 &&
        sessions.SemanticCalls==2 && sessions.SuccessfulEntries==1 && sessions.SuccessfulClicks==1 &&
        state.Operations.Count==2 && state.Operations.All(op=>op.Status==OperationStatus.Succeeded),
        "Independent approvals and receipts are required for each semantic action");
}

static Task NamedLinkSchema()
{
    var tool = new BrowserSessionSemanticLinkTool(new FakePublicBrowserSessions());
    var registry = new ToolRegistry([tool]);
    var good = new { sessionId="aabbccddeeff00112233445566778899",
        expectedSourceUrl="https://www.example.com/",expectedLinkName="About",
        expectedLinkUrl="https://www.example.com/about" };
    Assert(registry.Validate(tool.Descriptor.Name,JsonSerializer.Serialize(good),out _,out _),
        "All four exact navigation fields must be accepted");
    Assert(!registry.Validate(tool.Descriptor.Name,JsonSerializer.Serialize(new {
        good.sessionId,good.expectedSourceUrl,good.expectedLinkName }),out _,out _),
        "Missing destination URL must be rejected");
    Assert(!registry.Validate(tool.Descriptor.Name,JsonSerializer.Serialize(new {
        good.sessionId,good.expectedSourceUrl,good.expectedLinkName,good.expectedLinkUrl,
        unrestrictedClick=true }),out _,out _),"Unexpected interaction options must be rejected");
    return Task.CompletedTask;
}

static Task NamedLinkApprovalDetails()
{
    var sessions = new FakePublicBrowserSessions();
    var tool = new BrowserSessionSemanticLinkTool(sessions);
    string text=tool.GetTarget(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",
        expectedLinkName="About",expectedLinkUrl="https://www.example.com/about" }));
    Assert(text.Contains("https://www.example.com/about",StringComparison.Ordinal) &&
        text.Contains("About",StringComparison.Ordinal) &&
        text.Contains(sessions.Id,StringComparison.Ordinal) &&
        tool.Descriptor.Risk==ToolRisk.ReadExternal && tool.Descriptor.HasSideEffects,
        "Approval must include session, source, exact link name and destination");
    return Task.CompletedTask;
}

static Task NamedLinkInvalidTargets()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionSemanticLinkTool(sessions);
    object[] bad = [
        new {sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",
            expectedLinkName="About",expectedLinkUrl="https://different.org/page"},
        new {sessionId=sessions.Id,expectedSourceUrl="http://www.example.com/",
            expectedLinkName="About",expectedLinkUrl="https://www.example.com/about"},
        new {sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",
            expectedLinkName="  About",expectedLinkUrl="https://www.example.com/about"},
        new {sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",
            expectedLinkName="",expectedLinkUrl="https://www.example.com/about"},
        new {sessionId="invalid",expectedSourceUrl="https://www.example.com/",
            expectedLinkName="About",expectedLinkUrl="https://www.example.com/about"}
    ];
    foreach(object input in bad)
    {
        bool refused=false;
        try { _=tool.GetTarget(JsonSerializer.SerializeToElement(input)); }
        catch(ArgumentException) { refused=true; }
        Assert(refused,"Malformed name, unsafe URL or invalid session must be rejected before approval");
    }
    Assert(sessions.NamedFollowCalls==0,"Rejected targets cannot dispatch navigation");
    return Task.CompletedTask;
}

static Task NamedLinkUniqueIdentity()
{
    var actual = BrowserSemanticLink.RequireUnique([
        new BrowserLink("About us","https://www.example.com/about","About"),
        new BrowserLink("Home","https://www.example.com/","Home")
    ], "About",new Uri("https://www.example.com/about"));
    Assert(actual.AccessibleName=="About" && actual.Text=="About us",
        "Semantic selection should use accessible name instead of text or DOM index");
    return Task.CompletedTask;
}

static Task NamedLinkDuplicateRefused()
{
    bool refused=false;
    try { _=BrowserSemanticLink.RequireUnique([
        new BrowserLink("One","https://www.example.com/about","About"),
        new BrowserLink("Two","https://www.example.com/about","About")
    ],"About",new Uri("https://www.example.com/about")); }
    catch(InvalidOperationException) { refused=true; }
    Assert(refused,"Duplicate link accessible names must fail closed");
    return Task.CompletedTask;
}

static Task NamedLinkChangedHref()
{
    bool refused=false;
    try { _=BrowserSemanticLink.RequireUnique([
        new BrowserLink("About","https://www.example.com/changed","About")
    ],"About",new Uri("https://www.example.com/about")); }
    catch(InvalidOperationException) { refused=true; }
    Assert(refused,"Changed link URLs must be refused rather than followed");
    return Task.CompletedTask;
}

static async Task NamedLinkDenied()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("named-denied","browser.session_follow_named_link",new {
            sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",
            expectedLinkName="About",expectedLinkUrl="https://www.example.com/about" })])
    ]),new ToolRegistry([new BrowserSessionSemanticLinkTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var auth=new FakeApprovals(false);
    var result=await engine.RunAsync(Guid.NewGuid(),"Follow the About link",auth);
    Assert(result.Status==ConversationStatus.Paused && auth.Calls==1 &&
        sessions.NamedFollowCalls==0 && sessions.SuccessfulNavigations==0,
        "Denied named-link navigation must not execute");
}

static async Task NamedLinkApproved()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("named-yes","browser.session_follow_named_link",new {
            sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/",
            expectedLinkName="About",expectedLinkUrl="https://www.example.com/about" })]),
        new ModelReply("The observed destination title was About Page.",[])
    ]),new ToolRegistry([new BrowserSessionSemanticLinkTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();var auth=new FakeApprovals(true);
    var answer=await engine.RunAsync(id,"Navigate to About by exact accessible name",auth);
    var saved=(await setup.Store.LoadAsync(id,default))!;
    Assert(answer.Status==ConversationStatus.Completed && auth.Calls==1 &&
        sessions.NamedFollowCalls==1 && sessions.SuccessfulNavigations==1 &&
        sessions.Current=="https://www.example.com/about" &&
        saved.Operations.Count==1 && saved.Operations[0].Status==OperationStatus.Succeeded &&
        saved.Operations[0].ResultJson!.Contains("About Page",StringComparison.Ordinal),
        "Approved named navigation must preserve the same session and record the destination observation");
}

static async Task NamedLinkStaleSource()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionSemanticLinkTool(sessions);
    var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedSourceUrl="https://www.example.com/old",
        expectedLinkName="About",expectedLinkUrl="https://www.example.com/about" }),default);
    Assert(!result.Success && sessions.SuccessfulNavigations==0 &&
        result.ErrorCode=="BROWSER_NAMED_LINK_REFUSED",
        "A stale source URL must not navigate");
}

static async Task NamedLinkChangedName()
{
    var sessions=new FakePublicBrowserSessions { ChangedNamedName=true };
    var tool=new BrowserSessionSemanticLinkTool(sessions);
    var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedSourceUrl=sessions.Current,
        expectedLinkName="About",expectedLinkUrl="https://www.example.com/about" }),default);
    Assert(!result.Success && sessions.SuccessfulNavigations==0,
        "Changed accessible link names must not dispatch navigation");
}

static async Task NamedLinkAfterInteractionBlocked()
{
    var sessions=new FakePublicBrowserSessions();
    _=await sessions.FillTextAsync(sessions.Id,new Uri(sessions.Current),2,"input","Keyword","public",default);
    var tool=new BrowserSessionSemanticLinkTool(sessions);
    var result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedSourceUrl=sessions.Current,
        expectedLinkName="About",expectedLinkUrl="https://www.example.com/about" }),default);
    Assert(!result.Success && sessions.SuccessfulNavigations==0 && sessions.SuccessfulEntries==1,
        "Named navigation may not reopen network after an interactive action");
}

static async Task NamedLinkUncertainReconciliation()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions { NamedNavigationUncertain=true };
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("named-uncertain","browser.session_follow_named_link",new {
            sessionId=sessions.Id,expectedSourceUrl=sessions.Current,
            expectedLinkName="About",expectedLinkUrl="https://www.example.com/about" })])
    ]),new ToolRegistry([new BrowserSessionSemanticLinkTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();var result=await engine.RunAsync(id,"Navigate by named link",new FakeApprovals(true));
    var saved=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.NeedsReconciliation &&
        saved.Operations.Count==1 && saved.Operations[0].Status==OperationStatus.Unknown,
        "Uncertain navigation cannot be treated as safe to retry");
}

static Task SelectOptionSchema()
{
    var tool=new BrowserSessionSelectOptionTool(new FakePublicBrowserSessions());
    var registry=new ToolRegistry([tool]);
    var good=new {sessionId="aabbccddeeff00112233445566778899",
        expectedPageUrl="https://www.example.com/",accessibleName="Theme",optionLabel="Blue"};
    Assert(registry.Validate(tool.Descriptor.Name,JsonSerializer.Serialize(good),out _,out _),
        "Exact native select schema rejected");
    foreach(var args in new [] {
        JsonSerializer.Serialize(new{good.sessionId,good.expectedPageUrl,good.accessibleName,optionLabel=""}),
        JsonSerializer.Serialize(new{good.sessionId,good.expectedPageUrl,good.accessibleName,good.optionLabel,randomSelector="#item"}),
        JsonSerializer.Serialize(new{good.sessionId,good.expectedPageUrl,optionLabel="Blue"}),
        JsonSerializer.Serialize(new{good.sessionId,expectedPageUrl="http://www.example.com/",good.accessibleName,good.optionLabel}),
        JsonSerializer.Serialize(new{good.sessionId,good.expectedPageUrl,accessibleName="Theme\nInjected",good.optionLabel})
    })
    {
        bool refused=!registry.Validate(tool.Descriptor.Name,args,out JsonElement parsed,out _);
        if (!refused) { try{ _=tool.GetTarget(parsed); } catch(ArgumentException){refused=true;} }
        Assert(refused,"Unsafe, incomplete or unexpected selector arguments must fail before dispatch");
    }
    Assert(tool.Descriptor.Risk==ToolRisk.Dangerous && tool.Descriptor.HasSideEffects,
        "Dropdown selection always requires explicit per-action authorization");
    return Task.CompletedTask;
}

static Task SelectOptionApprovalExact()
{
    var tool=new BrowserSessionSelectOptionTool(new FakePublicBrowserSessions());
    var text=tool.GetTarget(JsonSerializer.SerializeToElement(new{
        sessionId="aabbccddeeff00112233445566778899",expectedPageUrl="https://www.example.com/",
        accessibleName="Theme",optionLabel="Blue"}));
    Assert(text.Contains("https://www.example.com/",StringComparison.Ordinal) &&
        text.Contains("Theme",StringComparison.Ordinal) && text.Contains("Blue",StringComparison.Ordinal) &&
        text.Contains("session",StringComparison.Ordinal),
        "Approval must identify exact URL, session ID, control and option label");
    return Task.CompletedTask;
}

static Task SelectOptionMetadata()
{
    Assert(BrowserSemanticControl.RoleFor("select","","")=="combobox",
        "Native select should expose combobox role");
    var element=new BrowserElement("select","","Theme","","",false,5,
        AccessibleName:"Theme",SemanticRole:"combobox",OptionLabels:new[]{"Choose", "Blue", "Green"});
    Assert(BrowserSemanticSelect.RequireUnique(new[]{element},"Theme","Blue").DomIndex==5,
        "Only an exact named dropdown option may be selected");
    bool noOptionsRefused = false;
    try { _ = BrowserSemanticSelect.RequireUnique(new[]{element with { OptionLabels = null }}, "Theme", "Blue"); }
    catch (InvalidOperationException) { noOptionsRefused = true; }
    Assert(noOptionsRefused, "A dropdown without observed option metadata must be refused before dispatch");
    Assert(!typeof(BrowserElement).GetProperties().Any(x => x.Name is "SelectedValue" or "InputValue"),
        "Read-only snapshots must never serialize selected form values");
    return Task.CompletedTask;
}

static Task SelectOptionDuplicateControl()
{
    var sessions=new FakePublicBrowserSessions{DuplicateSelect=true};
    bool refused=false;
    try{_ = BrowserSemanticSelect.RequireUnique(sessions.SnapshotForTests().Elements!,"Theme","Blue");}
    catch(InvalidOperationException){refused=true;}
    Assert(refused,"Duplicate same-name comboboxes must never fall back to first or DOM index");
    return Task.CompletedTask;
}

static Task SelectOptionDuplicateOption()
{
    var sessions=new FakePublicBrowserSessions{DuplicateSelectOption=true};
    bool refused=false;
    try{_ = BrowserSemanticSelect.RequireUnique(sessions.SnapshotForTests().Elements!,"Theme","Blue");}
    catch(InvalidOperationException){refused=true;}
    Assert(refused,"Duplicate option labels must be rejected as ambiguous");
    return Task.CompletedTask;
}

static async Task SelectOptionMissingOrDisabled()
{
    foreach(var settings in new []{(missing:true,disabled:false),(missing:false,disabled:true)})
    {
        var sessions=new FakePublicBrowserSessions{
            MissingSelectOption=settings.missing,DisabledSelectOption=settings.disabled};
        var tool=new BrowserSessionSelectOptionTool(sessions);
        ToolReceipt receipt=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new{
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,accessibleName="Theme",optionLabel="Blue"}),default);
        Assert(!receipt.Success && sessions.SuccessfulSelections==0,
            "Missing/disabled option must never cause a selection");
    }
}

static async Task SelectOptionDenied()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("choose","browser.session_select_option",new{
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,accessibleName="Theme",optionLabel="Blue"})])
    ]),new ToolRegistry([new BrowserSessionSelectOptionTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approvals=new FakeApprovals(false);
    var result=await engine.RunAsync(Guid.NewGuid(),"Select public dropdown",approvals);
    Assert(result.Status==ConversationStatus.Paused && approvals.Calls==1 && sessions.SelectCalls==0,
        "Denied select must never execute");
}

static async Task SelectOptionApprovedReceipt()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("choose","browser.session_select_option",new{
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,accessibleName="Theme",optionLabel="Blue"})]),
        new ModelReply("Selected the observed Blue option in the Theme dropdown.",[])
    ]),new ToolRegistry([new BrowserSessionSelectOptionTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Select dropdown",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && sessions.SelectCalls==1 && sessions.SuccessfulSelections==1,
        "Approved native select did not execute and complete");
    Assert(state.Operations.Count==1 && state.Operations[0].Status==OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains("\"SelectionVerified\":true",StringComparison.Ordinal) &&
        state.Operations[0].ResultJson!.Contains("Blue",StringComparison.Ordinal) &&
        !state.Operations[0].ResultJson!.Contains("SelectedValue",StringComparison.Ordinal),
        "Receipt must record verified label but no underlying option/input value");
}

static async Task SelectOptionChangedName()
{
    var sessions=new FakePublicBrowserSessions{ChangedSelectName=true};
    var tool=new BrowserSessionSelectOptionTool(sessions);
    ToolReceipt receipt=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new{
        sessionId=sessions.Id,expectedPageUrl=sessions.Current,accessibleName="Theme",optionLabel="Blue"}),default);
    Assert(!receipt.Success && sessions.SuccessfulSelections==0,
        "Stale accessible dropdown name must not dispatch");
}

static async Task SelectOptionIndependentApprovals()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("fill","browser.session_semantic_action",new{
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,action="fill",role="searchbox",
            accessibleName="Keyword",text="hello"})]),
        new ModelReply(null,[Call("select","browser.session_select_option",new{
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,accessibleName="Theme",optionLabel="Blue"})]),
        new ModelReply("Two verified, independently approved interactions.",[])
    ]),new ToolRegistry([new BrowserSessionSemanticActionTool(sessions),
        new BrowserSessionSelectOptionTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approvals=new FakeApprovals(true);
    var result=await engine.RunAsync(Guid.NewGuid(),"Fill and select without navigation",approvals);
    Assert(result.Status==ConversationStatus.Completed && approvals.Calls==2 &&
        sessions.SuccessfulEntries==1 && sessions.SuccessfulSelections==1 && sessions.Actions.VerifiedActions==2,
        "Every action in a session requires a separate host approval and sequential verification");
}

static async Task SelectOptionNoNavigationAfter()
{
    var sessions=new FakePublicBrowserSessions();
    var selected=await new BrowserSessionSelectOptionTool(sessions).ExecuteAsync(
        JsonSerializer.SerializeToElement(new{sessionId=sessions.Id,
            expectedPageUrl=sessions.Current,accessibleName="Theme",optionLabel="Blue"}),default);
    var namedLink = new BrowserSessionSemanticLinkTool(sessions);
    var namedLinkArguments = JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id, expectedSourceUrl=sessions.Current,
        expectedLinkName="About", expectedLinkUrl="https://www.example.com/about" });
    Assert(JsonToolSchema.Validate(namedLink.Descriptor.Parameters, namedLinkArguments,
        out string argumentError),
        "The regression must use the registered named-link schema: " + argumentError);
    var followed = await namedLink.ExecuteAsync(namedLinkArguments,default);
    Assert(selected.Success && !followed.Success &&
        followed.ErrorCode == "BROWSER_NAMED_LINK_REFUSED" &&
        sessions.NamedFollowCalls==1 && sessions.SuccessfulNavigations==0 &&
        sessions.NetworkBlocked,
        "Selection must not unlock navigation; routes remain network-blocked");
}

static async Task SelectOptionUncertain()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions{SelectUncertain=true};
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("choose","browser.session_select_option",new{
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,accessibleName="Theme",optionLabel="Blue"})])
    ]),new ToolRegistry([new BrowserSessionSelectOptionTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Select public dropdown",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.NeedsReconciliation &&
        state.Operations.Single().Status==OperationStatus.Unknown && sessions.Actions.RequiresReconciliation,
        "Uncertain selected option must stop agent and block action replay");
    bool refused=false;
    try { await sessions.SelectOptionByNameAsync(sessions.Id,new Uri(sessions.Current),"Theme","Green",default); }
    catch(InvalidOperationException){ refused=true; }
    Assert(refused,"Uncertain selection must lock further options until reconciliation");
}

static JsonElement CheckedArgs(FakePublicBrowserSessions sessions, string role="checkbox",
    string name="Accept updates", bool desired=true) => JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id, expectedPageUrl=sessions.Current,
        role, accessibleName=name, @checked=desired });

static Task CheckedSchema()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionSetCheckedTool(sessions);
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters,CheckedArgs(sessions),out _),
        "Exact checked-state tool payload must validate");
    foreach (JsonElement invalid in new [] {
        JsonSerializer.SerializeToElement(new { sessionId=sessions.Id, expectedPageUrl=sessions.Current,
            role="checkbox", accessibleName="Accept updates", @checked=true, selector="input" }),
        JsonSerializer.SerializeToElement(new { sessionId=sessions.Id, expectedPageUrl=sessions.Current,
            role="checkbox", accessibleName="Accept updates" }),
        JsonSerializer.SerializeToElement(new { sessionId=sessions.Id, expectedPageUrl=sessions.Current,
            role="checkbox", accessibleName="Accept updates", @checked="true" }) })
        Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,invalid,out _),
            "Unknown fields, missing desired state and string booleans must fail schema");
    Assert(tool.Descriptor.Risk==ToolRisk.Dangerous && tool.Descriptor.HasSideEffects,
        "Even checked-state interactions can trigger scripts and require Dangerous approval");
    return Task.CompletedTask;
}

static Task CheckedRadioUncheck()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionSetCheckedTool(sessions);
    bool refused=false;
    try { _=tool.GetTarget(CheckedArgs(sessions,"radio","Basic option",false)); }
    catch(ArgumentException) { refused=true; }
    Assert(refused && sessions.SetCheckedCalls==0,
        "Radio uncheck must be refused before approval or dispatch");
    return Task.CompletedTask;
}

static Task CheckedMetadata()
{
    var sessions=new FakePublicBrowserSessions();
    var elements=sessions.SnapshotForTests().Elements!;
    Assert(BrowserSemanticControl.RoleFor("input","checkbox","")=="checkbox" &&
        BrowserSemanticControl.RoleFor("input","radio","")=="radio",
        "Implicit roles must identify native input checkboxes and radios");
    var box=BrowserNativeCheckedControl.RequireUnique(elements,"checkbox","Accept updates");
    var radio=BrowserNativeCheckedControl.RequireUnique(elements,"radio","Basic option");
    Assert(box.NativeChecked==false && radio.NativeChecked==false && box.Tag=="input",
        "Inspection must report native checked state independently of aria-checked");
    Assert(!typeof(BrowserElement).GetProperties().Any(e => e.Name is "InputValue" or "CheckedValue"),
        "Read-only input observations must not expose underlying form values");
    return Task.CompletedTask;
}

static Task CheckedDuplicate()
{
    var sessions=new FakePublicBrowserSessions { DuplicateCheckedControl=true };
    bool refused=false;
    try { _=BrowserNativeCheckedControl.RequireUnique(
        sessions.SnapshotForTests().Elements!,"checkbox","Accept updates"); }
    catch(InvalidOperationException) { refused=true; }
    Assert(refused,"Ambiguous same-named native checkboxes must be refused");
    return Task.CompletedTask;
}

static async Task CheckedDisabledStale()
{
    foreach (var settings in new []{(disabled:true,stale:false),(disabled:false,stale:true)})
    {
        var sessions=new FakePublicBrowserSessions {
            DisableCheckedControl=settings.disabled, ChangeCheckedName=settings.stale };
        var result=await new BrowserSessionSetCheckedTool(sessions).ExecuteAsync(
            CheckedArgs(sessions),default);
        Assert(!result.Success && result.ErrorCode=="BROWSER_SET_CHECKED_REFUSED" &&
            sessions.SuccessfulCheckedChanges==0 && !sessions.NetworkBlocked,
            "Disabled or stale checked control must not dispatch or lock network");
    }
}

static async Task CheckedDenied()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("checked","browser.session_set_checked",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,role="checkbox",
            accessibleName="Accept updates",@checked=true})])
    ]),new ToolRegistry([new BrowserSessionSetCheckedTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approvals=new FakeApprovals(false);
    var result=await engine.RunAsync(Guid.NewGuid(),"Check checkbox",approvals);
    Assert(result.Status==ConversationStatus.Paused && approvals.Calls==1 &&
        sessions.SetCheckedCalls==0 && sessions.SuccessfulCheckedChanges==0,
        "Declined checked action must not reach the browser session");
}

static async Task CheckedApproved()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("checked","browser.session_set_checked",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,role="checkbox",
            accessibleName="Accept updates",@checked=true})]),
        new ModelReply("Checkbox checked after approval.",[])
    ]),new ToolRegistry([new BrowserSessionSetCheckedTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Check checkbox",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && sessions.SuccessfulCheckedChanges==1 &&
        sessions.CheckboxChecked && sessions.NetworkBlocked && sessions.Actions.VerifiedActions==1,
        "Approved checked action must record one verified interaction");
    string receipt=state.Operations.Single().ResultJson!;
    Assert(receipt.Contains("\"CheckedBefore\":false",StringComparison.Ordinal) &&
        receipt.Contains("\"CheckedAfter\":true",StringComparison.Ordinal) &&
        receipt.Contains("\"StateChangeObserved\":true",StringComparison.Ordinal) &&
        state.Operations.Single().Status==OperationStatus.Succeeded,
        "Receipt must include explicit before/after native checked-state evidence");
}

static async Task CheckedNoop()
{
    var sessions=new FakePublicBrowserSessions();
    ToolReceipt result=await new BrowserSessionSetCheckedTool(sessions).ExecuteAsync(
        CheckedArgs(sessions,desired:false),default);
    Assert(result.Success && !result.ChangedState &&
        result.Data.GetProperty("CheckedBefore").ValueKind==JsonValueKind.False &&
        result.Data.GetProperty("CheckedAfter").ValueKind==JsonValueKind.False &&
        sessions.SuccessfulCheckedChanges==0 && sessions.Actions.VerifiedActions==0 &&
        !sessions.NetworkBlocked,
        "Already-satisfied checked state must return an honest no-op without dispatch");
}

static async Task CheckedIndependent()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("select","browser.session_select_option",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            accessibleName="Theme",optionLabel="Blue"})]),
        new ModelReply(null,[Call("check","browser.session_set_checked",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            role="checkbox",accessibleName="Accept updates",@checked=true})]),
        new ModelReply("Selected and checked with independent approval.",[])
    ]),new ToolRegistry([new BrowserSessionSelectOptionTool(sessions),
        new BrowserSessionSetCheckedTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approvals=new FakeApprovals(true);
    var result=await engine.RunAsync(Guid.NewGuid(),"Select theme then check",approvals);
    Assert(result.Status==ConversationStatus.Completed && approvals.Calls==2 &&
        sessions.SuccessfulSelections==1 && sessions.SuccessfulCheckedChanges==1 &&
        sessions.Actions.VerifiedActions==2,
        "Two browser changes must each receive independent host approval and receipts");
}

static async Task CheckedNoNavigation()
{
    var sessions=new FakePublicBrowserSessions();
    var changed=await new BrowserSessionSetCheckedTool(sessions).ExecuteAsync(
        CheckedArgs(sessions),default);
    var linkTool=new BrowserSessionSemanticLinkTool(sessions);
    JsonElement args=JsonSerializer.SerializeToElement(new {
        sessionId=sessions.Id,expectedSourceUrl=sessions.Current,
        expectedLinkName="About",expectedLinkUrl="https://www.example.com/about" });
    Assert(JsonToolSchema.Validate(linkTool.Descriptor.Parameters,args,out string schemaError),
        "Named-link test input must satisfy the actual tool schema: " + schemaError);
    var followed=await linkTool.ExecuteAsync(args,default);
    Assert(changed.Success && changed.ChangedState && !followed.Success &&
        followed.ErrorCode=="BROWSER_NAMED_LINK_REFUSED" && sessions.NetworkBlocked &&
        sessions.SuccessfulNavigations==0,
        "Checked action may not unlock navigation or outbound network access");
}

static async Task CheckedUncertain()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions { CheckedUncertain=true };
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("check","browser.session_set_checked",new {
            sessionId=sessions.Id,expectedPageUrl=sessions.Current,
            role="checkbox",accessibleName="Accept updates",@checked=true})])
    ]),new ToolRegistry([new BrowserSessionSetCheckedTool(sessions)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Check checkbox",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.NeedsReconciliation &&
        state.Operations.Single().Status==OperationStatus.Unknown &&
        sessions.Actions.RequiresReconciliation && sessions.NetworkBlocked,
        "Uncertain checkbox effects must be journaled as unknown without retries");
    bool refused=false;
    try { await sessions.SetCheckedByNameAsync(sessions.Id,new Uri(sessions.Current),
        "checkbox","Accept updates",false,default); }
    catch(InvalidOperationException) { refused=true; }
    Assert(refused,"A session with uncertain effects may not execute further changes");
}

static async Task CheckedRadioApproved()
{
    var sessions=new FakePublicBrowserSessions();
    var tool=new BrowserSessionSetCheckedTool(sessions);
    var args=CheckedArgs(sessions,"radio","Basic option",true);
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters,args,out _),
        "Radio checked-state payload must match the tool schema");
    var result=await tool.ExecuteAsync(args,default);
    Assert(result.Success && result.ChangedState && sessions.RadioChecked &&
        result.Data.GetProperty("CheckedBefore").ValueKind==JsonValueKind.False &&
        result.Data.GetProperty("CheckedAfter").ValueKind==JsonValueKind.True,
        "A checked radio must have verified native before/after checked-state evidence");
}

// Phase 4I: disambiguate same-named native inputs by an observed HTML ID.
static JsonElement CheckedIdArgs(FakePublicBrowserSessions s, string id="acceptUpdates",
    string role="checkbox", string name="Accept updates", bool desired=true) =>
    JsonSerializer.SerializeToElement(new { sessionId=s.Id, expectedPageUrl=s.Current,
        role, accessibleName=name, expectedHtmlId=id, @checked=desired });

static Task ControlIdSchema()
{
    var s=new FakePublicBrowserSessions();
    var tool=new BrowserSessionSetCheckedTool(s);
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters,CheckedIdArgs(s),out _),
        "Checked tool must permit the observed HTML ID discriminator");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { sessionId=s.Id,expectedPageUrl=s.Current,
            role="checkbox",accessibleName="Accept updates",expectedHtmlId=12,@checked=true }),out _),
        "IDs must be strings in the strict tool schema");
    return Task.CompletedTask;
}
static Task ControlIdValidator()
{
    foreach(string id in new[]{"acceptUpdates","defaultCheck1","thing_2","control:name.3"})
        Assert(BrowserNativeCheckedControl.IsSafeHtmlId(id),"Expected safe HTML ID " + id);
    foreach(string id in new[]{"", "0startsWrong", "bad name", "input[id=x]", "hello\"", "bad\nline",new string('A',81)})
        Assert(!BrowserNativeCheckedControl.IsSafeHtmlId(id),"Unsafe selector-shaped ID accepted " + id);
    return Task.CompletedTask;
}
static async Task ControlIdDuplicateName()
{
    var s=new FakePublicBrowserSessions { DuplicateCheckedControl=true };
    ToolReceipt result=await new BrowserSessionSetCheckedTool(s).ExecuteAsync(
        CheckedIdArgs(s,"acceptUpdatesAlt"),default);
    Assert(result.Success && result.ChangedState && s.AlternateCheckboxChecked &&
        !s.CheckboxChecked && s.Actions.VerifiedActions==1 && s.NetworkBlocked,
        "Observed HTML ID must select only the requested same-named checkbox");
}
static async Task ControlIdNoGuess()
{
    var s=new FakePublicBrowserSessions { DuplicateCheckedControl=true };
    ToolReceipt result=await new BrowserSessionSetCheckedTool(s).ExecuteAsync(
        CheckedArgs(s),default);
    Assert(!result.Success && !s.CheckboxChecked && !s.AlternateCheckboxChecked &&
        s.Actions.VerifiedActions==0 && !s.NetworkBlocked,
        "Absent ID must never silently select first of duplicate names");
}
static async Task ControlIdUnknown()
{
    var s=new FakePublicBrowserSessions { DuplicateCheckedControl=true };
    ToolReceipt result=await new BrowserSessionSetCheckedTool(s).ExecuteAsync(
        CheckedIdArgs(s,"inventedId"),default);
    Assert(!result.Success && s.SuccessfulCheckedChanges==0 && !s.NetworkBlocked,
        "Unknown/unobserved HTML IDs must be rejected before dispatch");
}
static async Task ControlIdStale()
{
    var s=new FakePublicBrowserSessions { ChangedCheckedId=true };
    ToolReceipt result=await new BrowserSessionSetCheckedTool(s).ExecuteAsync(
        CheckedIdArgs(s,"acceptUpdates"),default);
    Assert(!result.Success && s.SuccessfulCheckedChanges==0 && !s.NetworkBlocked,
        "A control whose observed HTML ID changed must be rejected");
}
static async Task ControlIdWrongName()
{
    var s=new FakePublicBrowserSessions();
    ToolReceipt result=await new BrowserSessionSetCheckedTool(s).ExecuteAsync(
        CheckedIdArgs(s,"basicOption",name:"Accept updates"),default);
    Assert(!result.Success && s.SuccessfulCheckedChanges==0 && !s.NetworkBlocked,
        "HTML ID must not override accessible role and name matching");
}
static async Task ControlIdApproved()
{
    using var setup=new TestContext();
    var s=new FakePublicBrowserSessions { DuplicateCheckedControl=true };
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("a","browser.session_set_checked",new {
            sessionId=s.Id,expectedPageUrl=s.Current,role="checkbox",
            accessibleName="Accept updates",expectedHtmlId="acceptUpdatesAlt",@checked=true})]),
        new ModelReply("Selected exact named control",[])
    ]),new ToolRegistry([new BrowserSessionSetCheckedTool(s)]),setup.Store,
        new WorkspaceAuthority(setup.Workspace));
    var approval=new FakeApprovals(true);
    var result=await engine.RunAsync(Guid.NewGuid(),"Check second checkbox",approval);
    Assert(result.Status==ConversationStatus.Completed && approval.Calls==1 &&
        s.AlternateCheckboxChecked && !s.CheckboxChecked && s.SuccessfulCheckedChanges==1,
        "Exact HTML ID must be included in a separately approved checked operation");
}
static async Task ControlIdReceipt()
{
    var s=new FakePublicBrowserSessions { DuplicateCheckedControl=true };
    var tool=new BrowserSessionSetCheckedTool(s);
    JsonElement args=CheckedIdArgs(s,"acceptUpdatesAlt");
    Assert(tool.GetTarget(args).Contains("acceptUpdatesAlt",StringComparison.Ordinal),
        "Host approval description must disclose chosen HTML ID");
    ToolReceipt result=await tool.ExecuteAsync(args,default);
    Assert(result.Success && result.Data.GetProperty("HtmlId").GetString()=="acceptUpdatesAlt" &&
        result.Data.GetProperty("CheckedBefore").ValueKind==JsonValueKind.False &&
        result.Data.GetProperty("CheckedAfter").ValueKind==JsonValueKind.True,
        "Mutation receipt must show exact ID and native checked-state transition");
}
static async Task ControlIdRadio()
{
    var s=new FakePublicBrowserSessions { DuplicateRadioControl=true };
    ToolReceipt result=await new BrowserSessionSetCheckedTool(s).ExecuteAsync(
        CheckedIdArgs(s,"basicOptionAlt",role:"radio",name:"Basic option"),default);
    Assert(result.Success && s.AlternateRadioChecked && !s.RadioChecked &&
        result.Data.GetProperty("HtmlId").GetString()=="basicOptionAlt",
        "Same-named radio buttons must be disambiguated without an index");
}
static async Task ControlIdNoop()
{
    var s=new FakePublicBrowserSessions { DuplicateCheckedControl=true };
    ToolReceipt result=await new BrowserSessionSetCheckedTool(s).ExecuteAsync(
        CheckedIdArgs(s,"acceptUpdatesAlt",desired:false),default);
    Assert(result.Success && !result.ChangedState && !s.NetworkBlocked &&
        s.Actions.VerifiedActions==0 && result.Data.GetProperty("HtmlId").GetString()=="acceptUpdatesAlt",
        "Already matched target state must remain a no-op with truthful identity");
}
static async Task ControlIdNoNavigation()
{
    var s=new FakePublicBrowserSessions { DuplicateCheckedControl=true };
    ToolReceipt changed=await new BrowserSessionSetCheckedTool(s).ExecuteAsync(
        CheckedIdArgs(s,"acceptUpdatesAlt"),default);
    ToolReceipt follow=await new BrowserSessionSemanticLinkTool(s).ExecuteAsync(
        JsonSerializer.SerializeToElement(new{sessionId=s.Id,expectedSourceUrl=s.Current,
            expectedLinkName="About",expectedLinkUrl="https://www.example.com/about"}),default);
    Assert(changed.Success && changed.ChangedState && !follow.Success && s.NetworkBlocked &&
        s.SuccessfulNavigations==0,"ID-targeted interaction may not unlock navigation");
}


static JsonElement ControlPageArgs(FakePublicBrowserSessions s, int page) =>
    JsonSerializer.SerializeToElement(new {sessionId=s.Id,pageNumber=page});

static Task ControlPagesSchema()
{
    var s=new FakePublicBrowserSessions();
    var tool=new BrowserSessionInspectControlsTool(s);
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters,ControlPageArgs(s,2),out _),
        "Exact session and page are allowed");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new {sessionId=s.Id,pageNumber="1"}),out _),
        "String page rejected");
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new {sessionId=s.Id,pageNumber=1,unsafeSelector="input"}),out _),
        "Arbitrary selectors must be rejected");
    return Task.CompletedTask;
}
static Task ControlPagesBounds()
{
    Assert(BrowserControlPagination.OffsetForPage(0)==0 &&
        BrowserControlPagination.OffsetForPage(1)==24 &&
        BrowserControlPagination.OffsetForPage(63)==1512,"Page size/bounds mismatch");
    foreach(int page in new[]{-1,64,int.MaxValue})
    {
        bool refused=false;
        try{_ = BrowserControlPagination.OffsetForPage(page);}catch(ArgumentOutOfRangeException){refused=true;}
        Assert(refused,"Out-of-bounds page must be refused");
    }
    return Task.CompletedTask;
}
static Task ControlPagesIndexMapping()
{
    Assert(BrowserControlPagination.PageForIndex(0)==0 &&
        BrowserControlPagination.PageForIndex(23)==0 &&
        BrowserControlPagination.PageForIndex(24)==1 &&
        BrowserControlPagination.PageForIndex(47)==1 &&
        BrowserControlPagination.PageForIndex(1511)==62 &&
        BrowserControlPagination.PageForIndex(1535)==63,"Later form control mapping incorrect");
    foreach(int index in new[]{-1,1536,int.MaxValue})
    {
        bool refused=false;
        try{_ = BrowserControlPagination.PageForIndex(index);}catch(InvalidOperationException){refused=true;}
        Assert(refused,"Invalid control offset must fail closed");
    }
    return Task.CompletedTask;
}
static Task ControlPagesApprovalDescription()
{
    var s=new FakePublicBrowserSessions();
    var tool=new BrowserSessionInspectControlsTool(s);
    string target=tool.GetTarget(ControlPageArgs(s,3));
    Assert(target.Contains(s.Id,StringComparison.Ordinal) &&
        target.Contains("control page 3",StringComparison.Ordinal),
        "Approval should identify session and exact bounded page");
    return Task.CompletedTask;
}
static async Task ControlPagesReceipt()
{
    var s=new FakePublicBrowserSessions();
    var result=await new BrowserSessionInspectControlsTool(s).ExecuteAsync(ControlPageArgs(s,1),default);
    Assert(result.Success && !result.ChangedState && s.PageControlInspectCalls==1 &&
        result.Data.GetProperty("PageNumber").GetInt32()==1 &&
        result.Data.GetProperty("TotalControlCount").GetInt32()==56,
        "Control paging receipt must be read-only and bounded");
}
static async Task ControlPagesDenied()
{
    using var context=new TestContext();
    var s=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("c","browser.session_inspect_controls",new {sessionId=s.Id,pageNumber=2})])
    ]),new ToolRegistry([new BrowserSessionInspectControlsTool(s)]),context.Store,
        new WorkspaceAuthority(context.Workspace));
    var result=await engine.RunAsync(Guid.NewGuid(),"Inspect later controls",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Paused && s.PageControlInspectCalls==0,
        "Host denial must prevent any browser read");
}
static Task ControlPagesInvalidPage()
{
    var s=new FakePublicBrowserSessions();
    var tool=new BrowserSessionInspectControlsTool(s);
    bool refused=false;
    try{_ = tool.GetTarget(ControlPageArgs(s,64));}catch(ArgumentOutOfRangeException){refused=true;}
    Assert(refused && s.PageControlInspectCalls==0,"Page 64 must reject before tool dispatch");
    return Task.CompletedTask;
}
static async Task ControlPagesLaterControl()
{
    var s=new FakePublicBrowserSessions();
    var result=await new BrowserSessionInspectControlsTool(s).ExecuteAsync(ControlPageArgs(s,1),default);
    JsonElement elements=result.Data.GetProperty("Elements");
    Assert(result.Success && elements.GetArrayLength()==1 &&
        elements[0].GetProperty("DomIndex").GetInt32()==37 &&
        elements[0].GetProperty("HtmlId").GetString()=="radioDefault1",
        "Control page must expose actual later-position identity");
}
static async Task ControlPagesNoValues()
{
    var s=new FakePublicBrowserSessions();
    var result=await new BrowserSessionInspectControlsTool(s).ExecuteAsync(ControlPageArgs(s,1),default);
    string serialized=result.Data.GetRawText();
    Assert(result.Success && !serialized.Contains("secret-text",StringComparison.Ordinal) &&
        !serialized.Contains("value=",StringComparison.Ordinal),
        "Control inspection must not return field values");
}
static async Task ControlPagesClosed()
{
    var s=new FakePublicBrowserSessions();
    await s.CloseAsync(s.Id,default);
    var result=await new BrowserSessionInspectControlsTool(s).ExecuteAsync(ControlPageArgs(s,1),default);
    Assert(!result.Success && s.PageControlInspectCalls==1,
        "Closed browser session must fail to inspect");
}
static async Task ControlPagesReadNoNetworkLock()
{
    var s=new FakePublicBrowserSessions();
    var tool=new BrowserSessionInspectControlsTool(s);
    var result=await tool.ExecuteAsync(ControlPageArgs(s,1),default);
    Assert(result.Success && !s.NetworkBlocked && s.Actions.VerifiedActions==0,
        "Read-only later controls must not consume the browser mutation budget");
}
static async Task ControlPagesSeparateApprovals()
{
    using var context=new TestContext();
    var s=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("a","browser.session_inspect_controls",new {sessionId=s.Id,pageNumber=1})]),
        new ModelReply(null,[Call("b","browser.session_inspect_controls",new {sessionId=s.Id,pageNumber=2})]),
        new ModelReply("Read the authorized control pages",[])
    ]),new ToolRegistry([new BrowserSessionInspectControlsTool(s)]),context.Store,
        new WorkspaceAuthority(context.Workspace));
    var approvals=new FakeApprovals(true);
    var result=await engine.RunAsync(Guid.NewGuid(),"Inspect two later pages",approvals);
    Assert(result.Status==ConversationStatus.Completed && approvals.Calls==2 &&
        s.PageControlInspectCalls==2 && !s.NetworkBlocked,
        "Each bounded control page needs its own host permission");
}


// Phase 4K: a model's prose is never proof that its temporary browser session
// has closed. The trusted kernel must dispatch and journal local cleanup.
static async Task<(AgentRunResult Result, ConversationState State,
    FakePublicBrowserSessions Browser, FakeApprovals Approvals)> BrowserCloseScenario(
    bool structured = false, bool manual = false, bool includeCloser = true,
    bool rejectClose = false, bool throwClose = false)
{
    using var context = new TestContext();
    var browser = new FakePublicBrowserSessions {
        RejectClose = rejectClose, ThrowClose = throwClose
    };
    IAgentTool[] tools = includeCloser
        ? [new BrowserSessionOpenTool(browser), new BrowserSessionCloseTool(browser)]
        : [new BrowserSessionOpenTool(browser)];
    IChatModel model;
    if (structured)
    {
        int round = 0;
        model = new FakeModel(messages => {
            if (round++ == 0) return new ModelReply(null, [Call("open", "browser.session_open",
                new { url = "https://www.example.com/" })]);
            string observation = messages.Last(m => m.Role == "tool").Content!;
            using JsonDocument document = JsonDocument.Parse(observation);
            string receiptId = document.RootElement.GetProperty("receiptId").GetString()!;
            return new ModelReply(null, [Call("finish", "nira_finish", new {
                outcome = "action", answer = "Observed public example page.",
                evidence_ids = new[] {receiptId}
            })]);
        });
    }
    else if (manual)
        model = new FakeModel([
            new ModelReply(null, [Call("open", "browser.session_open",
                new { url = "https://www.example.com/" })]),
            new ModelReply(null, [Call("close", "browser.session_close",
                new { sessionId = browser.Id })]),
            new ModelReply("Finished the browser task.", [])
        ]);
    else
        model = new FakeModel([
            new ModelReply(null, [Call("open", "browser.session_open",
                new { url = "https://www.example.com/" })]),
            new ModelReply("Finished the browser task.", [])
        ]);
    var approvals = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    var engine = new NiraEngine(model, new ToolRegistry(tools), context.Store,
        new WorkspaceAuthority(context.Workspace));
    AgentRunResult result = await engine.RunAsync(id, "Inspect a temporary public page", approvals);
    return (result, (await context.Store.LoadAsync(id, default))!, browser, approvals);
}

static async Task BrowserAutoClosePlain()
{
    var test = await BrowserCloseScenario();
    Assert(test.Result.Status == ConversationStatus.Completed &&
        test.Browser.Closed && test.Browser.CloseCalls == 1,
        "Plain model final must trigger observed browser closure before completion");
}

static async Task BrowserAutoCloseStructured()
{
    var test = await BrowserCloseScenario(structured: true);
    Assert(test.Result.Status == ConversationStatus.Completed && test.Browser.Closed &&
        test.State.Operations.Count == 2 && test.Result.EvidenceIds.Count == 2,
        "Structured finish must preserve existing action evidence and host cleanup receipt");
}

static async Task BrowserAutoCloseApproval()
{
    var test = await BrowserCloseScenario();
    Assert(test.Result.Status == ConversationStatus.Completed && test.Approvals.Calls == 1 &&
        test.Browser.OpenCalls == 1 && test.Browser.CloseCalls == 1,
        "Host closing its own ephemeral context requires no extra network or Dangerous approval");
}

static async Task BrowserAutoCloseDurable()
{
    var test = await BrowserCloseScenario();
    AgentOperation close = test.State.Operations.Single(o => o.ToolName == "browser.session_close");
    Assert(close.Status == OperationStatus.Succeeded && close.ResultJson is not null &&
        close.ResultJson.Contains("Closed and discarded", StringComparison.Ordinal) &&
        test.Result.Message.Contains("browser.session_close", StringComparison.Ordinal) &&
        test.Result.Message.Contains("sessionId: " + test.Browser.Id, StringComparison.Ordinal),
        "Trusted cleanup must leave a user-visible host receipt and durable operation");
}

static async Task BrowserAutoCloseManual()
{
    var test = await BrowserCloseScenario(manual: true);
    Assert(test.Result.Status == ConversationStatus.Completed && test.Browser.CloseCalls == 1 &&
        test.State.Operations.Count(o => o.ToolName == "browser.session_close") == 1,
        "Explicit successful close must never be redispatched at the completion boundary");
}

static async Task BrowserAutoCloseMissingTool()
{
    var test = await BrowserCloseScenario(includeCloser: false);
    Assert(test.Result.Status == ConversationStatus.Paused &&
        test.Browser.CloseCalls == 0 && test.State.LastAnswer is null &&
        test.Result.Message.Contains("unavailable", StringComparison.Ordinal),
        "Kernel must refuse completion rather than falsely claim missing close tool worked");
}

static async Task BrowserAutoCloseCorrupted()
{
    using var context = new TestContext();
    var browser = new FakePublicBrowserSessions();
    Guid id = Guid.NewGuid();
    var state = new ConversationState { Id = id, Objective = "Close browser" };
    state.Operations.Add(new AgentOperation {
        ToolName = "browser.session_open", Status = OperationStatus.Succeeded,
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Opened", new { notASessionId = "bad" }, true))
    });
    await context.Store.SaveAsync(state, default);
    var engine = new NiraEngine(new FakeModel([new ModelReply("Done", [])]),
        new ToolRegistry([new BrowserSessionCloseTool(browser)]), context.Store,
        new WorkspaceAuthority(context.Workspace));
    AgentRunResult result = await engine.RunAsync(id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused && browser.CloseCalls == 0 &&
        result.Message.Contains("verifiable session ID", StringComparison.Ordinal),
        "Malformed or forged open receipts must not silently bypass cleanup");
}

static async Task BrowserAutoCloseFailed()
{
    var test = await BrowserCloseScenario(rejectClose: true);
    Assert(test.Result.Status == ConversationStatus.Paused && !test.Browser.Closed &&
        test.Browser.CloseCalls == 1 && test.State.LastAnswer is null &&
        test.State.Operations.Last().Status == OperationStatus.Failed,
        "An unsuccessful close must preserve the failure and refuse Completed");
}

static async Task BrowserAutoCloseUncertain()
{
    var test = await BrowserCloseScenario(throwClose: true);
    Assert(test.Result.Status == ConversationStatus.NeedsReconciliation &&
        test.Browser.CloseCalls == 1 && test.State.Operations.Last().Status == OperationStatus.Unknown &&
        test.State.Operations.Last().ErrorCode == "BROWSER_CLOSE_UNCERTAIN",
        "Uncertain close effect must pause and never automatically retry");
}

static async Task BrowserAutoCloseNoSession()
{
    using var context = new TestContext();
    var browser = new FakePublicBrowserSessions();
    var model = new FakeModel([new ModelReply("No browser was needed.", [])]);
    var engine = new NiraEngine(model, new ToolRegistry([new BrowserSessionCloseTool(browser)]),
        context.Store, new WorkspaceAuthority(context.Workspace));
    var result = await engine.RunAsync(Guid.NewGuid(), "Explain text only", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && result.EvidenceIds.Count == 0 &&
        browser.CloseCalls == 0 && result.Message == "No browser was needed.",
        "Plain informational answers must not acquire browser cleanup effects");
}

static async Task BrowserAutoClosePlanPending()
{
    using var context = new TestContext();
    var browser = new FakePublicBrowserSessions();
    Guid id = Guid.NewGuid();
    var state = new ConversationState { Id = id, Objective = "Inspect public page",
        Plan = [new PlanStep { Description = "Inspect", SuccessCriterion = "Verified", Status = "pending" }] };
    state.Operations.Add(new AgentOperation {
        ToolName = "browser.session_open", Status = OperationStatus.Succeeded,
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Opened",
            new { SessionId = browser.Id }, true))
    });
    await context.Store.SaveAsync(state, default);
    var engine = new NiraEngine(new FakeModel([new ModelReply("Done", [])]),
        new ToolRegistry([new BrowserSessionCloseTool(browser)]), context.Store,
        new WorkspaceAuthority(context.Workspace));
    var result = await engine.RunAsync(id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused && browser.CloseCalls == 0,
        "Cleanup occurs at accepted completion, not before the pending plan is resolved");
}

static async Task BrowserAutoCloseMultiple()
{
    using var context = new TestContext();
    string idA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    string idB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    var closer = new FakeMultipleBrowserCloser();
    Guid taskId = Guid.NewGuid();
    var state = new ConversationState { Id = taskId, Objective = "Close two sessions" };
    foreach (string sessionId in new[] { idB, idA })
        state.Operations.Add(new AgentOperation {
            ToolName = "browser.session_open", Status = OperationStatus.Succeeded,
            ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Opened", new { SessionId = sessionId }, true))
        });
    await context.Store.SaveAsync(state, default);
    var engine = new NiraEngine(new FakeModel([new ModelReply("Finished.", [])]),
        new ToolRegistry([closer]), context.Store, new WorkspaceAuthority(context.Workspace));
    var result = await engine.RunAsync(taskId, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && closer.Ids.SequenceEqual(new[] {idA, idB}) &&
        result.EvidenceIds.Count == 4 &&
        (await context.Store.LoadAsync(taskId, default))!.Operations.Count == 4,
        "All distinct open sessions must be closed deterministically with independent receipts");
}

static JsonElement LinkPageArgs(FakePublicBrowserSessions s,int page) =>
    JsonSerializer.SerializeToElement(new {sessionId=s.Id,pageNumber=page});
static JsonElement FollowPagedArgs(FakePublicBrowserSessions s,int page=1) =>
    JsonSerializer.SerializeToElement(new {sessionId=s.Id,expectedSourceUrl=s.Current,
        expectedLinkName="Developer guide",expectedLinkUrl="https://www.example.com/guide",
        linkPageNumber=page});
static Task LinkPagesSchema()
{
    var s=new FakePublicBrowserSessions();
    var t=new BrowserSessionInspectLinksTool(s);
    Assert(JsonToolSchema.Validate(t.Descriptor.Parameters,LinkPageArgs(s,1),out _) &&
        !JsonToolSchema.Validate(t.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
            sessionId=s.Id,pageNumber="1"}),out _) &&
        !JsonToolSchema.Validate(t.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
            sessionId=s.Id,pageNumber=1,css="a"}),out _),"Link paging schema must reject selectors");
    return Task.CompletedTask;
}
static Task LinkPagesBounds()
{
    Assert(BrowserLinkPagination.OffsetForPage(0)==0 &&
        BrowserLinkPagination.OffsetForPage(1)==24 &&
        BrowserLinkPagination.OffsetForPage(63)==1512,"Incorrect link offsets");
    foreach(int page in new[]{-1,64,int.MaxValue})
    {
        bool blocked=false;
        try{_ = BrowserLinkPagination.OffsetForPage(page);}catch(ArgumentOutOfRangeException){blocked=true;}
        Assert(blocked,"Invalid link page must be rejected");
    }
    return Task.CompletedTask;
}
static Task LinkPagesApproval()
{
    var s=new FakePublicBrowserSessions();
    string target=new BrowserSessionInspectLinksTool(s).GetTarget(LinkPageArgs(s,1));
    Assert(target.Contains(s.Id,StringComparison.Ordinal) &&
        target.Contains("link page 1",StringComparison.Ordinal),"Approval must name inspected page");
    string navigation=new BrowserSessionSemanticLinkTool(s).GetTarget(FollowPagedArgs(s));
    Assert(navigation.Contains("inspected link page 1",StringComparison.Ordinal) &&
        navigation.Contains("https://www.example.com/guide",StringComparison.Ordinal),
        "Navigation approval must disclose page and exact URL");
    return Task.CompletedTask;
}
static async Task LinkPagesReceipt()
{
    var s=new FakePublicBrowserSessions();
    ToolReceipt receipt=await new BrowserSessionInspectLinksTool(s).ExecuteAsync(LinkPageArgs(s,1),default);
    Assert(receipt.Success && !receipt.ChangedState &&
        receipt.Data.GetProperty("PageNumber").GetInt32()==1 &&
        receipt.Data.GetProperty("TotalLinkCount").GetInt32()==52 &&
        s.PageLinkInspectCalls==1 && !s.NetworkBlocked,"Link inspection must be read-only and bounded");
}
static async Task LinkPagesDenied()
{
    using var ctx=new TestContext();var s=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("read","browser.session_inspect_links",new {sessionId=s.Id,pageNumber=1})])
    ]),new ToolRegistry([new BrowserSessionInspectLinksTool(s)]),ctx.Store,
        new WorkspaceAuthority(ctx.Workspace));
    var result=await engine.RunAsync(Guid.NewGuid(),"Find later links",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Paused && s.PageLinkInspectCalls==0,
        "Denied link inspection must not read the page");
}
static async Task LinkPagesLateIdentity()
{
    var s=new FakePublicBrowserSessions();
    var result=await new BrowserSessionInspectLinksTool(s).ExecuteAsync(LinkPageArgs(s,1),default);
    JsonElement links=result.Data.GetProperty("Links");
    Assert(result.Success && links.GetArrayLength()==1 &&
        links[0].GetProperty("AccessibleName").GetString()=="Developer guide" &&
        links[0].GetProperty("Url").GetString()=="https://www.example.com/guide",
        "A link beyond the first 24 anchors must be observable");
}
static async Task LinkPagesStaleHref()
{
    var s=new FakePublicBrowserSessions { LaterLinkChangedHref=true };
    var result=await new BrowserSessionSemanticLinkTool(s).ExecuteAsync(FollowPagedArgs(s),default);
    Assert(!result.Success && s.SuccessfulNavigations==0 &&
        result.ErrorCode=="BROWSER_NAMED_LINK_REFUSED","Changed later link href must not navigate");
}
static async Task LinkPagesNoValues()
{
    var s=new FakePublicBrowserSessions();
    var result=await new BrowserSessionInspectLinksTool(s).ExecuteAsync(LinkPageArgs(s,1),default);
    string json=result.Data.GetRawText();
    Assert(result.Success && !json.Contains("secret-text",StringComparison.Ordinal) &&
        !json.Contains("value=",StringComparison.Ordinal) &&
        !json.Contains("Password",StringComparison.Ordinal),"Only link metadata should be included");
}
static async Task LinkPagesFollowApproved()
{
    var s=new FakePublicBrowserSessions();
    var tool=new BrowserSessionSemanticLinkTool(s);
    ToolReceipt result=await tool.ExecuteAsync(FollowPagedArgs(s),default);
    Assert(result.Success && result.ChangedState && s.NamedFollowCalls==1 &&
        s.SuccessfulNavigations==1 && s.Current=="https://www.example.com/guide" &&
        result.Data.GetProperty("LinkUrl").GetString()=="https://www.example.com/guide",
        "Later named-link navigation must preserve session and verify destination");
}
static async Task LinkPagesFollowDenied()
{
    using var ctx=new TestContext();var s=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("follow","browser.session_follow_named_link",new {
            sessionId=s.Id,expectedSourceUrl=s.Current,expectedLinkName="Developer guide",
            expectedLinkUrl="https://www.example.com/guide",linkPageNumber=1})])
    ]),new ToolRegistry([new BrowserSessionSemanticLinkTool(s)]),ctx.Store,
        new WorkspaceAuthority(ctx.Workspace));
    var outcome=await engine.RunAsync(Guid.NewGuid(),"Follow late link",new FakeApprovals(false));
    Assert(outcome.Status==ConversationStatus.Paused && s.NamedFollowCalls==0 &&
        s.SuccessfulNavigations==0,"Denied later-page navigation must not dispatch");
}
static async Task LinkPagesAfterInteraction()
{
    var s=new FakePublicBrowserSessions();
    _=await s.FillTextAsync(s.Id,new Uri(s.Current),2,"input","Keyword","text",default);
    ToolReceipt result=await new BrowserSessionSemanticLinkTool(s).ExecuteAsync(FollowPagedArgs(s),default);
    Assert(!result.Success && s.NetworkBlocked && s.SuccessfulNavigations==0,
        "Link paging must never unlock network requests after interaction");
}
static async Task LinkPagesIndependentApprovals()
{
    using var ctx=new TestContext();var s=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("read","browser.session_inspect_links",new {sessionId=s.Id,pageNumber=1})]),
        new ModelReply(null,[Call("follow","browser.session_follow_named_link",new {
            sessionId=s.Id,expectedSourceUrl=s.Current,expectedLinkName="Developer guide",
            expectedLinkUrl="https://www.example.com/guide",linkPageNumber=1})]),
        new ModelReply("Followed verified guide link.",[])
    ]),new ToolRegistry([new BrowserSessionInspectLinksTool(s),
        new BrowserSessionSemanticLinkTool(s)]),ctx.Store,new WorkspaceAuthority(ctx.Workspace));
    var approvals=new FakeApprovals(true);
    var result=await engine.RunAsync(Guid.NewGuid(),"Inspect then navigate",approvals);
    Assert(result.Status==ConversationStatus.Completed && approvals.Calls==2 &&
        s.PageLinkInspectCalls==1 && s.SuccessfulNavigations==1,
        "Inspected page and navigation require independent host approvals");
}


static Task ScopedLinkExactPair()
{
    BrowserLink selected=BrowserSemanticLink.RequireExactInPage([
        new BrowserLink("Vite", "https://www.example.com/vite", "Vite"),
        new BrowserLink("Vite", "https://www.example.com/vite-other", "Vite")
    ], "Vite", new Uri("https://www.example.com/vite"));
    Assert(selected.Url=="https://www.example.com/vite",
        "Exact name+URL must select the unique approved anchor despite reused names");
    return Task.CompletedTask;
}
static Task ScopedLinkDuplicatePair()
{
    bool refused=false;
    try { _=BrowserSemanticLink.RequireExactInPage([
        new BrowserLink("Images","https://www.example.com/images","Images"),
        new BrowserLink("Images","https://www.example.com/images","Images")
    ],"Images",new Uri("https://www.example.com/images")); }
    catch(InvalidOperationException) { refused=true; }
    Assert(refused,"Duplicate exact link identities in one inspected page must be refused");
    return Task.CompletedTask;
}
static Task ScopedLinkStalePair()
{
    bool refused=false;
    try { _=BrowserSemanticLink.RequireExactInPage([
        new BrowserLink("Images","https://www.example.com/changed","Images")
    ],"Images",new Uri("https://www.example.com/images")); }
    catch(InvalidOperationException) { refused=true; }
    Assert(refused,"Changed href must be refused before traffic is dispatched");
    return Task.CompletedTask;
}
static async Task ScopedLinkOtherPageDuplicate()
{
    var s=new FakePublicBrowserSessions { LaterLinkSameNameDifferentHref=true };
    ToolReceipt result=await new BrowserSessionSemanticLinkTool(s).ExecuteAsync(FollowPagedArgs(s),default);
    Assert(result.Success && s.SuccessfulNavigations==1 &&
        result.Data.GetProperty("LinkUrl").GetString()=="https://www.example.com/guide",
        "A second same-name link at a different href must not block an exact page-scoped match");
    var s2=new FakePublicBrowserSessions { LaterLinkExactDuplicate=true };
    ToolReceipt duplicate=await new BrowserSessionSemanticLinkTool(s2).ExecuteAsync(FollowPagedArgs(s2),default);
    Assert(!duplicate.Success && s2.SuccessfulNavigations==0,
        "The same exact anchor identity appearing twice must remain blocked");
}
static Task CanonicalDefaultExact()
{
    Uri observed=new("https://getbootstrap.com/docs/5.3/content/images");
    Uri unchanged=BrowserNamedLinkDestination.Resolve(observed,false);
    Assert(unchanged.AbsoluteUri==observed.AbsoluteUri,
        "Without explicit approval the exact observed link destination must be used");
    return Task.CompletedTask;
}
static Task CanonicalSlashOnly()
{
    Uri observed=new("https://getbootstrap.com/docs/5.3/content/images");
    Uri canonical=BrowserNamedLinkDestination.Resolve(observed,true);
    Assert(canonical.AbsoluteUri=="https://getbootstrap.com/docs/5.3/content/images/" &&
        canonical.Host==observed.Host && canonical.Scheme==observed.Scheme,
        "Canonical option may only append a terminal slash to the approved directory path");
    return Task.CompletedTask;
}
static Task CanonicalRejectUnsafe()
{
    foreach(string input in new[]{"https://www.example.com/",
        "https://www.example.com/guide/", "https://www.example.com/file.pdf",
        "https://www.example.com/guide?q=7", "https://www.example.com/a.b"})
    {
        bool rejected=false;
        try { _=BrowserNamedLinkDestination.Resolve(new Uri(input),true); }
        catch(ArgumentException) { rejected=true; }
        Assert(rejected,"Canonical option must reject root, already-slashed, file or query links");
    }
    return Task.CompletedTask;
}
static Task CanonicalSchema()
{
    var t=new BrowserSessionSemanticLinkTool(new FakePublicBrowserSessions());
    var s=new FakePublicBrowserSessions();
    var args=JsonSerializer.SerializeToElement(new {sessionId=s.Id,
        expectedSourceUrl=s.Current,expectedLinkName="Developer guide",
        expectedLinkUrl="https://www.example.com/guide",linkPageNumber=1,
        preferCanonicalTrailingSlash=true});
    Assert(JsonToolSchema.Validate(t.Descriptor.Parameters,args,out _) &&
        !JsonToolSchema.Validate(t.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
            sessionId=s.Id,expectedSourceUrl=s.Current,expectedLinkName="Developer guide",
            expectedLinkUrl="https://www.example.com/guide",linkPageNumber=1,
            preferCanonicalTrailingSlash="true"}),out _),
        "Canonical opt-in requires an exact boolean, never a string");
    return Task.CompletedTask;
}
static Task CanonicalApproval()
{
    var s=new FakePublicBrowserSessions();
    var t=new BrowserSessionSemanticLinkTool(s);
    string approval=t.GetTarget(JsonSerializer.SerializeToElement(new {sessionId=s.Id,
        expectedSourceUrl=s.Current,expectedLinkName="Developer guide",
        expectedLinkUrl="https://www.example.com/guide",linkPageNumber=1,
        preferCanonicalTrailingSlash=true}));
    Assert(approval.Contains("observed href https://www.example.com/guide",StringComparison.Ordinal) &&
        approval.Contains("approved navigation https://www.example.com/guide/",StringComparison.Ordinal) &&
        approval.Contains("all other redirects blocked",StringComparison.Ordinal),
        "Host approval must explicitly distinguish observed href from navigated canonical URL");
    return Task.CompletedTask;
}
static async Task CanonicalApprovedReceipt()
{
    var s=new FakePublicBrowserSessions();
    var t=new BrowserSessionSemanticLinkTool(s);
    ToolReceipt result=await t.ExecuteAsync(JsonSerializer.SerializeToElement(new {sessionId=s.Id,
        expectedSourceUrl=s.Current,expectedLinkName="Developer guide",
        expectedLinkUrl="https://www.example.com/guide",linkPageNumber=1,
        preferCanonicalTrailingSlash=true}),default);
    Assert(result.Success && result.ChangedState && s.SuccessfulNavigations==1 &&
        s.LastNamedNavigationUrl=="https://www.example.com/guide/" &&
        result.Data.GetProperty("LinkUrl").GetString()=="https://www.example.com/guide" &&
        result.Data.GetProperty("NavigationUrl").GetString()=="https://www.example.com/guide/" &&
        result.Data.GetProperty("Page").GetProperty("FinalUrl").GetString()==
            "https://www.example.com/guide/",
        "Receipt must separately report the observed link and verified canonical destination");
}
static async Task CanonicalDenied()
{
    using var ctx=new TestContext();var s=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("follow","browser.session_follow_named_link",new {
            sessionId=s.Id,expectedSourceUrl=s.Current,expectedLinkName="Developer guide",
            expectedLinkUrl="https://www.example.com/guide",linkPageNumber=1,
            preferCanonicalTrailingSlash=true})])
    ]),new ToolRegistry([new BrowserSessionSemanticLinkTool(s)]),ctx.Store,
        new WorkspaceAuthority(ctx.Workspace));
    var result=await engine.RunAsync(Guid.NewGuid(),"Follow canonical docs link",
        new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Paused && s.NamedFollowCalls==0 &&
        s.SuccessfulNavigations==0,"Denied canonical URL cannot be navigated to");
}
static async Task CanonicalAfterInteraction()
{
    var s=new FakePublicBrowserSessions();
    _=await s.FillTextAsync(s.Id,new Uri(s.Current),2,"input","Keyword","test",default);
    ToolReceipt result=await new BrowserSessionSemanticLinkTool(s).ExecuteAsync(
        JsonSerializer.SerializeToElement(new {sessionId=s.Id,
            expectedSourceUrl=s.Current,expectedLinkName="Developer guide",
            expectedLinkUrl="https://www.example.com/guide",linkPageNumber=1,
            preferCanonicalTrailingSlash=true}),default);
    Assert(!result.Success && s.SuccessfulNavigations==0 && s.NetworkBlocked,
        "Canonical navigation must respect the permanent network lock after an interaction");
}

static JsonElement VisibleTextArgs(FakePublicBrowserSessions s, int pageNumber) =>
    JsonSerializer.SerializeToElement(new {sessionId=s.Id,pageNumber});

static Task VisibleTextSchema()
{
    var s=new FakePublicBrowserSessions();
    var tool=new BrowserSessionInspectVisibleTextTool(s);
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters,VisibleTextArgs(s,2),out _) &&
        !JsonToolSchema.Validate(tool.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
            sessionId=s.Id,pageNumber="2"}),out _) &&
        !JsonToolSchema.Validate(tool.Descriptor.Parameters,JsonSerializer.SerializeToElement(new {
            sessionId=s.Id,pageNumber=2,selector="input"}),out _),
        "Text inspection accepts only session ID and numeric page index");
    return Task.CompletedTask;
}

static Task VisibleTextBounds()
{
    Assert(BrowserVisibleTextPagination.PageSize==4000 &&
        BrowserVisibleTextPagination.MaxPageIndex==15 &&
        BrowserVisibleTextPagination.OffsetForPage(0)==0 &&
        BrowserVisibleTextPagination.OffsetForPage(15)==60000,
        "Visible text pages must be bounded to 16 pages / 64k characters");
    foreach(int index in new[]{-1,16,int.MaxValue})
    {
        bool rejected=false;
        try { _=BrowserVisibleTextPagination.OffsetForPage(index); }
        catch(ArgumentOutOfRangeException) { rejected=true; }
        Assert(rejected,"Invalid text page must fail before reading the browser");
    }
    return Task.CompletedTask;
}

static Task VisibleTextFixedSlice()
{
    string text=new string('A',4000)+new string('B',4000)+"C";
    var first=BrowserVisibleTextPagination.Slice("id","https://www.example.com/","Test",text,0);
    var second=BrowserVisibleTextPagination.Slice("id","https://www.example.com/","Test",text,1);
    Assert(first.Text.Length==4000 && first.Text.All(c=>c=='A') && first.HasMore &&
        second.Text.Length==4000 && second.Text.All(c=>c=='B') && second.HasMore &&
        !first.ReadLimitReached, "Text chunks must not overlap or exceed 4k characters");
    return Task.CompletedTask;
}

static Task VisibleTextBeyondSnapshot()
{
    string text=new string('X',10000)+"LATE ARTICLE PARAGRAPH";
    var page=BrowserVisibleTextPagination.Slice("id","https://www.example.com/","Document",text,2);
    Assert(page.Text.Contains("LATE ARTICLE PARAGRAPH",StringComparison.Ordinal) &&
        page.TotalVisibleCharacters==text.Length && page.PageNumber==2,
        "Later text must be inspectable beyond the ordinary 10,000-character snapshot");
    return Task.CompletedTask;
}

static Task VisibleTextEmptyFinal()
{
    var page=BrowserVisibleTextPagination.Slice("id","https://www.example.com/","Page",
        "short text",2);
    Assert(page.Text=="" && !page.HasMore && !page.ReadLimitReached &&
        page.TotalVisibleCharacters==10,
        "Out-of-content pages must remain bounded and report no further text");
    return Task.CompletedTask;
}

static Task VisibleTextHardLimit()
{
    string longText=new string('x',70000);
    var last=BrowserVisibleTextPagination.Slice("id","https://www.example.com/","Page",
        longText,15);
    Assert(last.Text.Length==4000 && !last.HasMore && last.ReadLimitReached &&
        last.TotalVisibleCharacters==70000,
        "Final page must explicitly disclose that the 64k budget was reached");
    return Task.CompletedTask;
}

static Task VisibleTextApprovalTarget()
{
    var s=new FakePublicBrowserSessions();
    string target=new BrowserSessionInspectVisibleTextTool(s).GetTarget(VisibleTextArgs(s,3));
    Assert(target.Contains(s.Id,StringComparison.Ordinal) &&
        target.Contains("visible text page 3",StringComparison.Ordinal),
        "Approval must describe the exact independently authorized page");
    return Task.CompletedTask;
}

static async Task VisibleTextReceipt()
{
    var s=new FakePublicBrowserSessions();
    ToolReceipt receipt=await new BrowserSessionInspectVisibleTextTool(s)
        .ExecuteAsync(VisibleTextArgs(s,2),default);
    Assert(receipt.Success && !receipt.ChangedState &&
        receipt.Data.GetProperty("PageNumber").GetInt32()==2 &&
        receipt.Data.GetProperty("Text").GetString()!.Contains("LATE ARTICLE PARAGRAPH",StringComparison.Ordinal) &&
        receipt.Data.GetProperty("PageUrl").GetString()==s.Current &&
        s.PageTextInspectCalls==1 && !s.NetworkBlocked && s.SuccessfulNavigations==0,
        "Visible text receipt must contain actual bounded page content without changing state");
}

static async Task VisibleTextNoInputValues()
{
    var s=new FakePublicBrowserSessions();
    var receipt=await new BrowserSessionInspectVisibleTextTool(s)
        .ExecuteAsync(VisibleTextArgs(s,2),default);
    string json=receipt.Data.GetRawText();
    Assert(!json.Contains("PASSWORD_FIELD_VALUE",StringComparison.Ordinal) &&
        !json.Contains("input.value",StringComparison.Ordinal) &&
        !json.Contains("outerHTML",StringComparison.Ordinal),
        "Only rendered public visible text is allowed in text-page receipts");
}

static async Task VisibleTextDenied()
{
    using var ctx=new TestContext();
    var s=new FakePublicBrowserSessions();
    var engine=new NiraEngine(new FakeModel([
        new ModelReply(null,[Call("read","browser.session_inspect_text",
            new {sessionId=s.Id,pageNumber=2})])
    ]),new ToolRegistry([new BrowserSessionInspectVisibleTextTool(s)]),
        ctx.Store,new WorkspaceAuthority(ctx.Workspace));
    var outcome=await engine.RunAsync(Guid.NewGuid(),"Read later article text",
        new FakeApprovals(false));
    Assert(outcome.Status==ConversationStatus.Paused && s.PageTextInspectCalls==0,
        "Denied text inspection must not reach the browser session");
}

static async Task VisibleTextAfterInteraction()
{
    var s=new FakePublicBrowserSessions();
    _=await s.FillTextAsync(s.Id,new Uri(s.Current),2,"input","Keyword","example",default);
    var result=await new BrowserSessionInspectVisibleTextTool(s)
        .ExecuteAsync(VisibleTextArgs(s,2),default);
    Assert(result.Success && !result.ChangedState && s.NetworkBlocked &&
        s.SuccessfulNavigations==0,
        "Read-only text inspection must not re-enable network after an interaction");
}

static async Task VisibleTextClosed()
{
    var s=new FakePublicBrowserSessions();
    _=await s.CloseAsync(s.Id,default);
    var result=await new BrowserSessionInspectVisibleTextTool(s)
        .ExecuteAsync(VisibleTextArgs(s,2),default);
    Assert(!result.Success && result.ErrorCode=="BROWSER_VISIBLE_TEXT_PAGE_FAILED",
        "Closed browser sessions must refuse text reads");
}

// Phase 4O regression coverage: model text that imitates a tool call is not
// actual provider tool_calls data. No JSON string is dispatched without the
// registered tool protocol, schema validation, and independent host approval.
static string PseudoBrowserCall() => """
    {"function":"browser_session_open","arguments":{"url":"https://www.example.com/"}}
    """;

static async Task TextToolJsonBlocksFalseCompletion()
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    int rounds=0;
    var model=new FakeModel(messages => {
        rounds++;
        if (rounds==1) return new ModelReply(PseudoBrowserCall(),[]);
        Assert(messages.Any(m => m.Role=="system" && m.Content?.Contains(
            "browser.session_open",StringComparison.Ordinal)==true &&
            m.Content.Contains("NO TOOL WAS EXECUTED",StringComparison.Ordinal)),
            "Text-only JSON must cause transient guidance naming the registered tool");
        return new ModelReply(PseudoBrowserCall(),[]);
    });
    var registry=new ToolRegistry([new BrowserSessionOpenTool(sessions),new BrowserSessionCloseTool(sessions)]);
    Guid id=Guid.NewGuid();
    var result=await new NiraEngine(model,registry,setup.Store,
        new WorkspaceAuthority(setup.Workspace)).RunAsync(id,"Open a public page",new FakeApprovals(true));
    Assert(result.Status==ConversationStatus.Paused && rounds==3 &&
        sessions.OpenCalls==0 && sessions.CloseCalls==0 && result.EvidenceIds.Count==0,
        "Repeated printed JSON must pause rather than complete or launch a browser");
    var saved=(await setup.Store.LoadAsync(id,default))!;
    Assert(saved.LastAnswer is null && saved.Operations.Count==0,
        "A text-only function name cannot become an execution receipt");
}

static async Task RunTextToolApproved(string fakeJson)
{
    using var setup=new TestContext();
    var sessions=new FakePublicBrowserSessions();
    var model=new FakeModel([
        new ModelReply(fakeJson,[]),
        new ModelReply(null,[Call("real-open","browser.session_open",
            new {url="https://www.example.com/"})]),
        new ModelReply("Page opening was approved and observed.",[])
    ]);
    var approvals=new FakeApprovals(true);
    var engine=new NiraEngine(model,new ToolRegistry([
        new BrowserSessionOpenTool(sessions),new BrowserSessionCloseTool(sessions)]),
        setup.Store,new WorkspaceAuthority(setup.Workspace));
    Guid id=Guid.NewGuid();
    var result=await engine.RunAsync(id,"Open the public page",approvals);
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && approvals.Calls==1 &&
        sessions.OpenCalls==1 && sessions.CloseCalls==1 &&
        state.Operations.Count==2 &&
        state.Operations.All(x=>x.Status==OperationStatus.Succeeded) &&
        state.Operations[0].ToolName=="browser.session_open" &&
        state.Operations[1].ToolName=="browser.session_close",
        "Recovered command must run through the real approved tool and audit closure exactly once");
    Assert(!state.Messages.Any(m=>m.Role=="assistant" && m.Content==fakeJson),
        "Model text pretending to invoke a function must not pollute the transcript");
}

static Task TextToolJsonRecoversApproved() => RunTextToolApproved(PseudoBrowserCall());
static Task TextToolFencedRecovers() => RunTextToolApproved("```json\n"+PseudoBrowserCall()+"\n```");
static Task TextToolNameEnvelopeRecovers() => RunTextToolApproved(
    """{"name":"browser.session_open","arguments":{"url":"https://www.example.com/"}}""");
static Task TextToolNestedEnvelopeRecovers() => RunTextToolApproved(
    """{"type":"function","function":{"name":"browser.session_open","arguments":"{\"url\":\"https://www.example.com/\"}"}}""");
static Task TextToolCallsEnvelopeRecovers() => RunTextToolApproved(
    """{"tool_calls":[{"type":"function","function":{"name":"browser.session_open","arguments":"{}"}}]}""");

static async Task TextToolRepeatedPauses()
{
    using var setup=new TestContext();
    int rounds=0;
    var model=new FakeModel(_ => {rounds++;return new ModelReply(PseudoBrowserCall(),[]);});
    var approver=new FakeApprovals(true);
    Guid id=Guid.NewGuid();
    var result=await setup.Engine(model).RunAsync(id,"Open this page",approver);
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Paused && rounds==3 &&
        approver.Calls==0 && state.Operations.Count==0 && state.LastAnswer is null,
        "Repeated JSON imitation must not call tools or ask for their approval");
}

static async Task TextToolFakeDonePauses()
{
    using var setup=new TestContext();
    var model=new FakeModel([
        new ModelReply(PseudoBrowserCall(),[]),
        new ModelReply("I opened the page successfully.",[]),
        new ModelReply("Completed.",[])
    ]);
    var result=await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Open a real browser",new FakeApprovals(true));
    Assert(result.Status==ConversationStatus.Paused && result.EvidenceIds.Count==0 &&
        result.Message.Contains("No such action was executed",StringComparison.Ordinal),
        "Unverified prose after fake JSON must not complete the action");
}

static async Task TextToolStructuredFinishRefused()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new FakeModel([
        new ModelReply(PseudoBrowserCall(),[]),
        new ModelReply(null,[Call("finish","nira_finish",new {
            outcome="answer",answer="The browser opened.",evidence_ids=Array.Empty<string>()})]),
        new ModelReply(PseudoBrowserCall(),[]),
        new ModelReply(PseudoBrowserCall(),[])
    ]);
    var output=await setup.Engine(model).RunAsync(id,"Open this website",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(output.Status==ConversationStatus.Paused && state.LastAnswer is null &&
        state.Messages.Any(m=>m.Role=="tool" &&
            m.Content?.Contains("TEXT_TOOL_CALL_NOT_EXECUTED",StringComparison.Ordinal)==true),
        "Structured finish cannot claim a textual command was executed");
}

static async Task TextToolGuidanceTransient()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new FakeModel([
        new ModelReply(PseudoBrowserCall(),[]),
        new ModelReply(PseudoBrowserCall(),[]),
        new ModelReply(PseudoBrowserCall(),[])
    ]);
    _=await setup.Engine(model).RunAsync(id,"Open a page",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Messages.Count==1 && state.Messages[0].Role=="user" &&
        state.Messages.All(m=>m.Role!="system"),
        "Recovery guidance must remain transient; do not inject synthetic durable messages");
}

static async Task TextToolOrdinaryJsonAnswer()
{
    using var setup=new TestContext();
    const string data="""{"status":"ok","count":2,"items":["a","b"]}""";
    var result=await setup.Engine(new FakeModel([new ModelReply(data,[])]))
        .RunAsync(Guid.NewGuid(),"Format this data as JSON",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed && result.Message==data,
        "Regular JSON data without a tool invocation must remain a valid answer");
}

static async Task TextToolExampleAnswer()
{
    using var setup=new TestContext();
    string explanation="Example only: "+PseudoBrowserCall();
    var result=await setup.Engine(new FakeModel([new ModelReply(explanation,[])]))
        .RunAsync(Guid.NewGuid(),"Explain the shape of a legacy tool JSON envelope",
            new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed && result.Message==explanation,
        "A prose explanation containing JSON is not a standalone tool attempt");
}

// Milestone 1A: general-purpose, evidence-backed workflow execution.
static AgentOperation WorkflowProof(string id, bool changed = true,
    OperationStatus status = OperationStatus.Succeeded, bool receiptSuccess = true)
{
    return new AgentOperation {
        Id = id, ToolName = changed ? "filesystem.write_text" : "filesystem.read_text",
        ArgumentsJson = "{}", Status = status,
        ResultJson = JsonSerializer.Serialize(receiptSuccess
            ? ToolReceipt.Ok("Host verified", new { result = "known" }, changedState: changed)
            : ToolReceipt.Fail("FAILED", "Host rejected the action"))
    };
}

static async Task WorkflowUpdateCase(string kind, string nextStatus, string? evidenceId,
    bool shouldSucceed, string? expectedError = null, AgentOperation[]? operations = null,
    List<PlanStep>? steps = null, int stepIndex = 0)
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var plan = steps ?? [new PlanStep {
        Description = "Complete the task", SuccessCriterion = "Host evidence received", Kind = kind
    }];
    await setup.Store.SaveAsync(new ConversationState {
        Id = id, Objective = "Complete a workflow and verify the work",
        Plan = plan, Operations = operations?.ToList() ?? []
    }, default);
    object update = evidenceId is null
        ? new { index = stepIndex, status = nextStatus }
        : new { index = stepIndex, status = nextStatus, evidenceId };
    var model = new FakeModel([
        new ModelReply(null, [Call("update", "nira_plan_update", update)]),
        new ModelReply(null, [Call("ask", "nira_ask_user", new {
            question = "What should I examine next?" })])
    ]);
    AgentRunResult result = await setup.Engine(model).RunAsync(id, null, new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.AwaitingUser, "Test run must preserve its checkpoint");
    ConversationState saved = (await setup.Store.LoadAsync(id, default))!;
    bool rejected = saved.Messages.Any(m => m.Role == "tool" &&
        m.Content?.Contains("\"errorCode\":\"PLAN_", StringComparison.Ordinal) == true);
    Assert(rejected != shouldSucceed,
        "Plan update acceptance was not gated as expected for " + kind + " / " + nextStatus);
    if (shouldSucceed)
        Assert(saved.Plan[stepIndex].Status == nextStatus &&
            saved.Plan[stepIndex].EvidenceId == (nextStatus == "completed" ? evidenceId : null),
            "An approved plan step must persist the new status and exact proof ID");
    if (expectedError is not null)
        Assert(saved.Messages.Any(m => m.Role == "tool" &&
            m.Content?.Contains(expectedError, StringComparison.Ordinal) == true),
            "The provider must receive explicit workflow error " + expectedError);
}

static async Task WorkflowPlanTyped()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new FakeModel([
        new ModelReply(null, [Call("plan", "nira_plan", new { steps = new[] {
            new { description = "Create output", successCriterion = "Created", kind = "action" },
            new { description = "Read output", successCriterion = "Readback", kind = "observation" },
            new { description = "Summarize", successCriterion = "Answer", kind = "analysis" }
        } })]),
        new ModelReply(null, [Call("ask", "nira_ask_user", new { question = "What output filename should I use?" })])
    ]);
    await setup.Engine(model).RunAsync(id, "Create then verify", new FakeApprovals(true));
    ConversationState saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(saved.Plan.Select(x => x.Kind).SequenceEqual(["action", "observation", "analysis"]),
        "Typed plans must be recorded durably without site-specific intent rules");
    Assert(saved.Messages.Any(x => x.Role == "tool" && x.Content?.Contains("successCriterion", StringComparison.OrdinalIgnoreCase) == true),
        "The model should receive step definitions, not just a count");
}

static async Task WorkflowPlanNoReplace()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    await setup.Store.SaveAsync(new ConversationState {
        Id=id, Objective="Create two artifacts",
        Plan=[new PlanStep {Description="Original step",SuccessCriterion="Verified",Kind="action"},
              new PlanStep {Description="Second step",SuccessCriterion="Verified",Kind="observation"}]
    }, default);
    var model=new FakeModel([
        new ModelReply(null,[Call("replace","nira_plan",new {steps=new[] {
            new {description="Skip work",successCriterion="Say done",kind="analysis"},
            new {description="Finish",successCriterion="Done",kind="analysis"}
        }})]),
        new ModelReply(null,[Call("ask","nira_ask_user",new {question="Which artifact comes first?"})])
    ]);
    await setup.Engine(model).RunAsync(id,null,new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Plan.Count==2 && state.Plan[0].Description=="Original step", "Existing plan must not be overwritten");
    Assert(state.Messages.Any(x => x.Content?.Contains("PLAN_ALREADY_EXISTS",StringComparison.Ordinal)==true),
        "Attempted bypass needs explicit failure observation");
}

static async Task WorkflowPlanStatus()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    await setup.Store.SaveAsync(new ConversationState {Id=id,Objective="Generate report",
        Plan=[new PlanStep {Description="Inspect source",SuccessCriterion="Observed", Kind="observation"},
              new PlanStep {Description="Create report",SuccessCriterion="Saved",Kind="action"}]},default);
    var model=new FakeModel([
        new ModelReply(null,[Call("status","nira_plan_status",new {})]),
        new ModelReply(null,[Call("ask","nira_ask_user",new {question="Which report should be generated?"})])
    ]);
    await setup.Engine(model).RunAsync(id,null,new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Messages.Any(x => x.Role=="tool" && x.Content?.Contains("Generate report",StringComparison.Ordinal)==true &&
        x.Content?.Contains("Inspect source",StringComparison.Ordinal)==true),
        "Status should return the actual stored objective and steps");
}

static async Task WorkflowPlanStatusSchema()
{
    using var setup=new TestContext();
    var model=new FakeModel([
        new ModelReply(null,[Call("bad","nira_plan_status",new {invented="x"})]),
        new ModelReply(null,[Call("ask","nira_ask_user",new {question="Which report should I inspect?"})])
    ]);
    Guid id=Guid.NewGuid();
    await setup.Engine(model).RunAsync(id,"Inspect report",new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Messages.Any(x=>x.Content?.Contains("ARGUMENT_SCHEMA",StringComparison.Ordinal)==true),
        "Status inspection must reject extra fields");
}

static Task WorkflowActionValid() => WorkflowUpdateCase("action","completed","proof",true,
    operations:[WorkflowProof("proof")]);
static Task WorkflowActionReadRefused() => WorkflowUpdateCase("action","completed","read",false,
    "PLAN_EVIDENCE",[WorkflowProof("read",changed:false)]);
static Task WorkflowActionNoProof() => WorkflowUpdateCase("action","completed",null,false,"PLAN_EVIDENCE");
static Task WorkflowActionUnknownProof() => WorkflowUpdateCase("action","completed","invented",false,"PLAN_EVIDENCE");
static Task WorkflowActionFailedProof() => WorkflowUpdateCase("action","completed","failed",false,
    "PLAN_EVIDENCE",[WorkflowProof("failed",status:OperationStatus.Failed)]);
static Task WorkflowActionFalseReceipt() => WorkflowUpdateCase("action","completed","fake",false,
    "PLAN_EVIDENCE",[WorkflowProof("fake",receiptSuccess:false)]);
static Task WorkflowObservationRead() => WorkflowUpdateCase("observation","completed","read",true,
    operations:[WorkflowProof("read",changed:false)]);
static Task WorkflowObservationChanged() => WorkflowUpdateCase("observation","completed","write",true,
    operations:[WorkflowProof("write")]);
static Task WorkflowObservationNoProof() => WorkflowUpdateCase("observation","completed",null,false,"PLAN_EVIDENCE");
static Task WorkflowAnalysisNoProof() => WorkflowUpdateCase("analysis","completed",null,true);
static Task WorkflowLegacyNoProof() => WorkflowUpdateCase("general","completed",null,true);
static Task WorkflowDuplicateProof() => WorkflowUpdateCase("observation","completed","one",false,
    "PLAN_EVIDENCE",[WorkflowProof("one")],
    [new PlanStep {Description="First",SuccessCriterion="Verified",Kind="action",
        Status="completed",EvidenceId="one"},
     new PlanStep {Description="Second",SuccessCriterion="Verified",Kind="observation"}],1);
static Task WorkflowPendingProof() => WorkflowUpdateCase("action","pending","proof",false,
    "PLAN_EVIDENCE_STATUS",[WorkflowProof("proof")]);
static Task WorkflowLockedRevert() => WorkflowUpdateCase("action","blocked",null,false,
    "PLAN_COMPLETION_LOCKED",[WorkflowProof("one")],
    [new PlanStep {Description="First",SuccessCriterion="Verified",Kind="action",
        Status="completed",EvidenceId="one"}]);
static Task WorkflowLockedProof() => WorkflowUpdateCase("action","completed","two",false,
    "PLAN_COMPLETION_LOCKED",[WorkflowProof("one"),WorkflowProof("two")],
    [new PlanStep {Description="First",SuccessCriterion="Verified",Kind="action",
        Status="completed",EvidenceId="one"}]);
static Task WorkflowIdempotent() => WorkflowUpdateCase("action","completed","one",true,
    operations:[WorkflowProof("one")],
    steps:[new PlanStep {Description="First",SuccessCriterion="Verified",Kind="action",
        Status="completed",EvidenceId="one"}]);

static async Task WorkflowPlainForged()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    await setup.Store.SaveAsync(new ConversationState{Id=id,Objective="Perform step",
        Plan=[new PlanStep{Description="Create file",SuccessCriterion="Exists",Kind="action",
            Status="completed",EvidenceId="invented"}]},default);
    AgentRunResult result=await setup.Engine(new FakeModel([
        new ModelReply("Done, the file was created.",[])])).RunAsync(id,null,new FakeApprovals(true));
    Assert(result.Status==ConversationStatus.Paused,"Forged saved plan proof cannot complete a plain reply");
}

static async Task WorkflowStructuredForged()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    await setup.Store.SaveAsync(new ConversationState{Id=id,Objective="Perform step",
        Plan=[new PlanStep{Description="Create file",SuccessCriterion="Exists",Kind="action",
            Status="completed",EvidenceId="invented"}]},default);
    var model=new FakeModel([
        new ModelReply(null,[Call("finish","nira_finish",new {
            outcome="answer",answer="Done",evidence_ids=Array.Empty<string>()})]),
        new ModelReply(null,[Call("ask","nira_ask_user",new {question="What else should be checked?"})])
    ]);
    await setup.Engine(model).RunAsync(id,null,new FakeApprovals(true));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Status==ConversationStatus.AwaitingUser &&
        state.Messages.Any(x=>x.Content?.Contains("PLAN_UNFINISHED",StringComparison.Ordinal)==true),
        "Forged evidence must also block structured nira_finish");
}

static async Task WorkflowEndToEnd()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    int round=0;
    var model=new FakeModel(messages =>
    {
        int now=round++;
        if(now==0) return new ModelReply(null,[Call("plan","nira_plan",new{steps=new[] {
            new {description="Create fact file",successCriterion="Write returned hash",kind="action"},
            new {description="Verify file",successCriterion="Read back exact bytes",kind="observation"},
            new {description="Report",successCriterion="Answer names exact verified content",kind="analysis"}
        }})]);
        if(now==1) return new ModelReply(null,[Call("write","filesystem.write_text",
            new {path="workflow-m1a.txt",content="M1A_VERIFIED"})]);
        if(now==2 || now==4)
        {
            var last=messages.Last(x=>x.Role=="tool" && x.Content?.Contains("receiptId",StringComparison.Ordinal)==true);
            using var parsed=JsonDocument.Parse(last.Content!);
            string proof=parsed.RootElement.GetProperty("receiptId").GetString()!;
            return new ModelReply(null,[Call(now==2?"update-write":"update-read","nira_plan_update",
                new {index=now==2?0:1,status="completed",evidenceId=proof})]);
        }
        if(now==3) return new ModelReply(null,[Call("read","filesystem.read_text",
            new {path="workflow-m1a.txt"})]);
        if(now==5) return new ModelReply(null,[Call("update-report","nira_plan_update",
            new {index=2,status="completed"})]);
        if(now==6) return new ModelReply(null,[Call("status","nira_plan_status",new {})]);
        return new ModelReply("Created and read back M1A_VERIFIED from workflow-m1a.txt.",[]);
    });
    AgentRunResult result=await setup.Engine(model).RunAsync(id,
        "Create a text file, read it back, and confirm its exact content",new FakeApprovals(true));
    Assert(result.Status==ConversationStatus.Completed,"Complete real work should be allowed");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace,"workflow-m1a.txt"))=="M1A_VERIFIED",
        "The actual file must be verified independently");
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Plan.Count==3 && state.Plan.All(x=>x.Status=="completed"),
        "All distinct planned steps must finish");
    Assert(state.Plan[0].EvidenceId!=state.Plan[1].EvidenceId &&
        state.Plan[0].EvidenceId is not null && state.Plan[1].EvidenceId is not null,
        "Write and read require distinct actual receipts");
    Assert(state.Messages.Any(x=>x.Role=="tool" && x.Content?.Contains("\"completed\":3",StringComparison.Ordinal)==true),
        "Durable status should expose completed steps");
}

static async Task WorkflowEmptyStatus()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new FakeModel([
        new ModelReply(null,[Call("status","nira_plan_status",new {})]),
        new ModelReply("No plan has been recorded yet.",[])
    ]);
    AgentRunResult result=await setup.Engine(model).RunAsync(id,"What is the current task plan?",new FakeApprovals(true));
    Assert(result.Status==ConversationStatus.Completed,"Empty plans must not prevent ordinary responses");
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(state.Messages.Any(x=>x.Role=="tool" && x.Content?.Contains("\"total\":0",StringComparison.Ordinal)==true),
        "Empty status must be represented explicitly");
}

static ModelReply AutoPlanChoice(string mode, params (string Description, string Criterion, string Kind)[] entries)
{
    var steps = entries.Select(x => new {
        description = x.Description, successCriterion = x.Criterion, kind = x.Kind
    }).ToArray();
    return new ModelReply(null, [Call("route", "nira_decide_workflow", new { mode, steps })]);
}

static async Task AutoPlanFileWorkflow()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    int executionRound = 0;
    bool observedPlanBeforeWork = false;
    var model = new PlanningTestModel(AutoPlanChoice("plan",
        ("Create requested note", "File write receipt exists", "action"),
        ("Read note back", "Read receipt confirms exact text", "observation"),
        ("Confirm result", "Answer accurately reports verification", "analysis")),
        new FakeModel(messages => {
            if (executionRound++ == 0)
            {
                observedPlanBeforeWork = messages.Any(x => x.Role == "system" &&
                    x.Content?.Contains("A durable plan was already created", StringComparison.Ordinal) == true);
                return new ModelReply(null, [Call("write", "filesystem.write_text", new {
                    path="ordinary-request-m1b.txt", content="COMPLETED_M1B" })]);
            }
            if (executionRound == 2 || executionRound == 4)
            {
                ChatEntry result = messages.Last(x => x.Role=="tool" &&
                    x.Content?.Contains("receiptId",StringComparison.Ordinal)==true);
                using var doc = JsonDocument.Parse(result.Content!);
                string proof = doc.RootElement.GetProperty("receiptId").GetString()!;
                int index = executionRound == 2 ? 0 : 1;
                return new ModelReply(null,[Call("update"+index,"nira_plan_update", new {
                    index,status="completed",evidenceId=proof })]);
            }
            if (executionRound == 3) return new ModelReply(null,[Call("read","filesystem.read_text",new{
                path="ordinary-request-m1b.txt"})]);
            if (executionRound == 5) return new ModelReply(null,[Call("analysis","nira_plan_update",new{
                index=2,status="completed"})]);
            return new ModelReply("Created and verified ordinary-request-m1b.txt containing COMPLETED_M1B.",[]);
        }));
    AgentRunResult result = await setup.Engine(model).RunAsync(id,
        "Create a note called ordinary-request-m1b.txt containing COMPLETED_M1B, then check its contents.",
        new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Completed,"Ordinary multi-step task should complete");
    Assert(model.PlanCalls==1 && observedPlanBeforeWork,"Host must save and supply automatic plan first");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace,"ordinary-request-m1b.txt")) == "COMPLETED_M1B",
        "Only registered tool must create the file");
    var saved = (await setup.Store.LoadAsync(id,default))!;
    Assert(saved.Plan.Count==3 && saved.Plan.All(x=>x.Status=="completed"),
        "All automatically generated steps must be completed with the existing evidence rules");
    Assert(saved.Plan[0].EvidenceId is not null && saved.Plan[1].EvidenceId is not null &&
        saved.Plan[0].EvidenceId != saved.Plan[1].EvidenceId,
        "Write and read steps need separate host receipts");
}

static async Task AutoPlanDirectConversation()
{
    using var setup = new TestContext();
    var model = new PlanningTestModel(AutoPlanChoice("direct"),
        new FakeModel([new ModelReply("Hello!",[])]));
    var result=await setup.Engine(model).RunAsync(Guid.NewGuid(),"Hi NIRA",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed && result.Message=="Hello!", "Simple chat must work directly");
    Assert(model.PlanCalls==1 && model.DecisionTool?.Name=="nira_decide_workflow",
        "Planning should invoke only the declarative decision tool");
}

static async Task AutoPlanSingleAction()
{
    using var setup=new TestContext();
    var model=new PlanningTestModel(AutoPlanChoice("direct"),new FakeModel([
        new ModelReply(null,[Call("write","filesystem.write_text",new{
            path="single-action-m1b.txt",content="ONE"})]),
        new ModelReply("Created single-action-m1b.txt",[])
    ]));
    AgentRunResult result=await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Make a note called single-action-m1b.txt containing ONE",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed, "Standalone action need not force an unnecessary plan");
    Assert(await File.ReadAllTextAsync(Path.Combine(setup.Workspace,"single-action-m1b.txt"))=="ONE",
        "Existing write validation must continue to work");
}

static async Task AutoPlanTextIgnored()
{
    using var setup=new TestContext();
    var model=new PlanningTestModel(new ModelReply("{\"function\":\"filesystem.write_text\",\"arguments\":{\"path\":\"fake.txt\"}}",[]),
        new FakeModel([new ModelReply("I cannot plan this without structured tool support.",[])]));
    Guid id=Guid.NewGuid();
    var result=await setup.Engine(model).RunAsync(id,"Tell me if any note was made",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed,"Text from planning pass is not an execution event");
    Assert(!File.Exists(Path.Combine(setup.Workspace,"fake.txt")),"Planner text must never execute");
    Assert((await setup.Store.LoadAsync(id,default))!.AutoPlanningAttempted,"Planning attempt must be durable");
}

static async Task AutoPlanWrongTool()
{
    using var setup=new TestContext();
    var model=new PlanningTestModel(new ModelReply(null,[Call("bad","filesystem.write_text",new{
        path="illegal-from-planner.txt",content="X"})]),
        new FakeModel([new ModelReply("No operation was performed.",[])]));
    var result=await setup.Engine(model).RunAsync(Guid.NewGuid(),"Describe progress",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed,"Unexpected preflight tool proposal must be ignored");
    Assert(!File.Exists(Path.Combine(setup.Workspace,"illegal-from-planner.txt")),
        "Planning pass must never dispatch an executable tool");
}

static async Task AutoPlanWrongSize()
{
    using var setup=new TestContext();
    var model=new PlanningTestModel(AutoPlanChoice("plan",("Only one","Cannot be a plan","action")),
        new FakeModel([new ModelReply("I will do the one step directly.",[])]));
    Guid id=Guid.NewGuid();
    await setup.Engine(model).RunAsync(id,"One step request",new FakeApprovals(false));
    Assert((await setup.Store.LoadAsync(id,default))!.Plan.Count==0,"Too-short plans must not be saved");
}

static async Task AutoPlanInvalidKind()
{
    using var setup=new TestContext();
    var model=new PlanningTestModel(AutoPlanChoice("plan",
        ("First action","Have evidence","superuser"),
        ("Second step","Have evidence","analysis")),
        new FakeModel([new ModelReply("No steps executed.",[])]));
    Guid id=Guid.NewGuid();
    await setup.Engine(model).RunAsync(id,"An unsupported planner decision",new FakeApprovals(false));
    Assert((await setup.Store.LoadAsync(id,default))!.Plan.Count==0,"Unsafe step kinds must be rejected");
}

static async Task AutoPlanNoRepeat()
{
    using var setup=new TestContext();
    var model=new PlanningTestModel(AutoPlanChoice("direct"),new FakeModel([
        new ModelReply(null,[Call("ask","nira_ask_user",new{question="Which name should I use?"})]),
        new ModelReply("Thanks, I will use that name.",[])
    ]));
    Guid id=Guid.NewGuid();
    var engine=setup.Engine(model);
    AgentRunResult first=await engine.RunAsync(id,"Choose a project name",new FakeApprovals(false));
    AgentRunResult second=await engine.RunAsync(id,"Use NIRA",new FakeApprovals(false));
    Assert(first.Status==ConversationStatus.AwaitingUser && second.Status==ConversationStatus.Completed,
        "Questions must preserve the task and answer as normal");
    Assert(model.PlanCalls==1,"Re-answering a question must not call the planner a second time");
}

static async Task AutoPlanResume()
{
    using var setup=new TestContext();
    var model=new PlanningTestModel(AutoPlanChoice("plan",
        ("Draft a file","Write receipt exists","action"),
        ("Verify file","Read matches","observation")), new FakeModel([
        new ModelReply(null,[Call("ask","nira_ask_user",new{question="What should the file contain?"})]),
        new ModelReply(null,[Call("status","nira_plan_status",new {})]),
        new ModelReply(null,[Call("ask2","nira_ask_user",new{question="Which filename should I use?"})])
    ]));
    Guid id=Guid.NewGuid();
    var engine=setup.Engine(model);
    var first=await engine.RunAsync(id,"Prepare a file and check it",new FakeApprovals(false));
    Assert(first.Status==ConversationStatus.AwaitingUser,"First question should checkpoint");
    var second=await engine.RunAsync(id,"Use the words TEST",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(second.Status==ConversationStatus.AwaitingUser && model.PlanCalls==1,
        "Follow-up must not recreate a durable plan");
    Assert(state.Plan.Count==2 && state.AutoPlanningAttempted,
        "The auto plan and its planning marker must survive multiple turns");
    Assert(state.Messages.Any(x => x.Role=="tool" &&
        x.Content?.Contains("\"total\":2",StringComparison.Ordinal)==true),
        "Saved plan must remain inspectable using nira_plan_status");
}

static async Task AutoPlanExistingPlan()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    await setup.Store.SaveAsync(new ConversationState { Id=id,Objective="Continue old task",
        Plan=[new PlanStep {Description="Existing step",SuccessCriterion="Original evidence",Kind="analysis"}]}, default);
    var model=new PlanningTestModel(AutoPlanChoice("plan",
        ("Replace original","Not allowed","action"),("Overwrite", "Not allowed","analysis")),
        new FakeModel([new ModelReply(null,[Call("ask","nira_ask_user",new {question="What should be next?"})])]));
    var result=await setup.Engine(model).RunAsync(id,null,new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.AwaitingUser && model.PlanCalls==0,
        "Saved work must bypass planner and preserve its plan");
    Assert((await setup.Store.LoadAsync(id,default))!.Plan[0].Description=="Existing step",
        "Never replace an old plan automatically");
}

static async Task AutoPlanWireIsolation()
{
    string body=JsonSerializer.Serialize(new {choices = new[]{ new {message = new {
        role="assistant", content=(string?)null,tool_calls=new[]{new{
            id="plan-1",type="function",function=new{name="nira_decide_workflow",
                arguments=JsonSerializer.Serialize(new {mode="direct",steps=Array.Empty<object>()})}
        }}}}}});
    var handler=new FakeApiHandler(body);
    using var http=new HttpClient(handler);
    var model=new OpenAiCompatibleModel(http,new Uri("https://model.example/v1/"),"test-model",()=>null);
    var decisionTool=new ToolDescriptor("nira_decide_workflow","Planning choice only",ToolRisk.ReadLocal,
        JsonToolSchema.Schema("""{"type":"object","properties":{},"additionalProperties":false}"""));
    ModelReply proposal=await model.ProposeWorkflowAsync([new ChatEntry("user","Say hi")],decisionTool,default);
    Assert(proposal.ToolCalls.Count==1 && proposal.ToolCalls[0].Name=="nira_decide_workflow",
        "Transport must preserve the native planner call");
    using var wire=JsonDocument.Parse(handler.RequestBody!);
    JsonElement tools=wire.RootElement.GetProperty("tools");
    Assert(tools.GetArrayLength()==1 && tools[0].GetProperty("function").GetProperty("name").GetString()=="nira_decide_workflow",
        "Planning request must not advertise any executable tools");
}

static async Task AutoPlanNoApproval()
{
    using var setup=new TestContext();
    var approval=new FakeApprovals(false);
    var model=new PlanningTestModel(AutoPlanChoice("direct"),new FakeModel([
        new ModelReply("This is a direct answer.",[])
    ]));
    await setup.Engine(model).RunAsync(Guid.NewGuid(),"Explain something simple",approval);
    Assert(approval.Calls==0,"Planning decision needs no tool authorization and has no effects");
}

// Milestone 1C: general-purpose planning and autonomous continuation.
// The planning JSON fallback is purely declarative; execution still requires
// real registered tools and their normal independent host approvals.
static ModelReply M1cDecisionJson(string mode, object[] steps, bool fenced=false)
{
    string value=JsonSerializer.Serialize(new { mode,steps });
    return new ModelReply(fenced ? "```json\n"+value+"\n```" : value,[]);
}

static object M1cStep(string kind, string description, string toolName="") =>
    string.IsNullOrEmpty(toolName)
        ? new { kind, description, successCriterion="Host-confirmed completion" }
        : (object)new { kind, description, successCriterion="Host-confirmed completion", toolName };

static async Task M1cPlannerJsonPlan()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new PlanningTestModel(M1cDecisionJson("plan",[
        M1cStep("analysis","Evaluate available evidence"),
        M1cStep("analysis","Report verified outcome")]),new FakeModel([
        new ModelReply(null,[Call("a","nira_plan_update",new { index=0,status="completed" })]),
        new ModelReply(null,[Call("b","nira_plan_update",new { index=1,status="completed" })]),
        new ModelReply("Done with the verified analysis.",[])
    ]));
    var result=await setup.Engine(model).RunAsync(id,"Review and report the verified evidence",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && state.Plan.Count==2 &&
        state.Plan.All(step=>step.Status=="completed") &&
        state.AutomaticPlanningOutcome=="planned_json" && model.PlanCalls==1,
        "Strictly validated declarative JSON must create a persisted plan without execution authority");
}

static async Task M1cPlannerFencedJson()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new PlanningTestModel(M1cDecisionJson("plan",[
        M1cStep("analysis","Assess supporting facts"),
        M1cStep("analysis","Communicate reliable results")],true),new FakeModel([
        new ModelReply(null,[Call("first","nira_plan_update",new {index=0,status="completed"})]),
        new ModelReply(null,[Call("second","nira_plan_update",new {index=1,status="completed"})]),
        new ModelReply("Analysis reported.",[])
    ]));
    var result=await setup.Engine(model).RunAsync(id,"Summarize and confirm",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed &&
        (await setup.Store.LoadAsync(id,default))!.AutomaticPlanningOutcome=="planned_json",
        "A standalone fenced JSON decision is acceptable only in the non-executing planner");
}

static async Task M1cPlannerJsonDirect()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new PlanningTestModel(M1cDecisionJson("direct",[]),new FakeModel([
        new ModelReply("A direct answer.",[])
    ]));
    var result=await setup.Engine(model).RunAsync(id,"Explain a concept",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && state.Plan.Count==0 &&
        state.AutomaticPlanningOutcome=="direct_json","Text JSON direct decision must be auditable");
}

static async Task M1cPlannerMalformed()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new PlanningTestModel(new ModelReply("{\"mode\":\"plan\",\"steps\":[],\"unsafe\":1}",[]),
        new FakeModel([new ModelReply("Nothing was executed.",[])]));
    var result=await setup.Engine(model).RunAsync(id,"Explain status",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && state.Plan.Count==0 &&
        state.AutomaticPlanningOutcome=="invalid" && state.AutoPlanningAttempted,
        "Invalid or extended planner payload must be rejected and reported, not silently trusted");
}

static async Task M1cPlannerExecutableText()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new PlanningTestModel(new ModelReply(
        "{\"function\":\"filesystem.write_text\",\"arguments\":{\"path\":\"forged.txt\",\"content\":\"X\"}}",[]),
        new FakeModel([new ModelReply("No action happened.",[])]));
    var result=await setup.Engine(model).RunAsync(id,"Can you execute this?",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed &&
        !File.Exists(Path.Combine(setup.Workspace,"forged.txt")) &&
        (await setup.Store.LoadAsync(id,default))!.AutomaticPlanningOutcome=="invalid",
        "Plain JSON tool-call imitation must never be executed during planning");
}

static async Task M1cPlannerUnknownBinding()
{
    await M1cRejectedPlanBinding("action","unregistered.unsafe.tool");
}
static async Task M1cPlannerReadOnlyBinding()
{
    await M1cRejectedPlanBinding("action","filesystem.read_text");
}
static async Task M1cPlannerAnalysisBinding()
{
    await M1cRejectedPlanBinding("analysis","filesystem.read_text");
}
static async Task M1cRejectedPlanBinding(string kind,string toolName)
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new PlanningTestModel(M1cDecisionJson("plan",[
        M1cStep(kind,"Unsupported tool assignment",toolName),
        M1cStep("analysis","Report valid evidence")]),new FakeModel([
        new ModelReply("Cannot form a valid plan.",[])
    ]));
    var result=await setup.Engine(model).RunAsync(id,"Plan an example task",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && state.Plan.Count==0 &&
        state.AutomaticPlanningOutcome=="invalid" && state.Operations.Count==0,
        "The host must reject unregistered or unsafe planning tool bindings");
}

static async Task M1cNativePlanBinding()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var decision=new ModelReply(null,[Call("plan","nira_decide_workflow",new {
        mode="plan", steps = new object[] {
            M1cStep("action","Write the requested file","filesystem.write_text"),
            M1cStep("observation","Read the file back","filesystem.read_text")
        }
    })]);
    var model=new PlanningTestModel(decision,new FakeModel([
        new ModelReply(null,[Call("ask","nira_ask_user",new {question="What contents should I use?"})])
    ]));
    var result=await setup.Engine(model).RunAsync(id,"Write and verify a file",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.AwaitingUser && state.Plan.Count==2 &&
        state.AutomaticPlanningOutcome=="planned_tool" &&
        state.Plan[0].ToolName=="filesystem.write_text" &&
        state.Plan[1].ToolName=="filesystem.read_text",
        "Native workflow decisions must preserve exact registered tool bindings");
}

static async Task M1cEvidenceWrongTool() => await M1cEvidenceMatch(false);
static async Task M1cEvidenceMatchingTool() => await M1cEvidenceMatch(true);
static async Task M1cEvidenceMatch(bool matches)
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var state=new ConversationState { Id=id, Objective="Check tool-bound evidence", AutoPlanningAttempted=true,
        AutomaticPlanningOutcome="planned_tool", Plan=[new PlanStep {
            Description="Create a file", SuccessCriterion="Host receipt", Kind="action", ToolName="filesystem.write_text" }] };
    state.Operations.Add(new AgentOperation { Id="proof-m1c", ToolName=matches?"filesystem.write_text":"process.run",
        Status=OperationStatus.Succeeded,
        ResultJson=JsonSerializer.Serialize(ToolReceipt.Ok("Observed state change",changedState:true)) });
    await setup.Store.SaveAsync(state,default);
    ModelReply[] replies=matches ? [
        new ModelReply(null,[Call("update","nira_plan_update",new {index=0,status="completed", evidenceId="proof-m1c"})]),
        new ModelReply("Valid matching tool evidence.",[])
    ] : [
        new ModelReply(null,[Call("update","nira_plan_update",new {index=0,status="completed", evidenceId="proof-m1c"})]),
        new ModelReply(null,[Call("ask","nira_ask_user",new {question="Which reference should I use for the verification?"})])
    ];
    var model=new FakeModel(replies);
    var result=await setup.Engine(model).RunAsync(id,null,new FakeApprovals(false));
    var saved=(await setup.Store.LoadAsync(id,default))!;
    if (matches)
        Assert(result.Status==ConversationStatus.Completed && saved.Plan[0].EvidenceId=="proof-m1c",
            "A successful matching tool receipt must satisfy the planned action");
    else
        Assert(result.Status==ConversationStatus.AwaitingUser && saved.Plan[0].Status=="pending" &&
            saved.Plan[0].EvidenceId is null,
            "Changed-state receipts from the wrong registered tool must not satisfy the plan");
}

static async Task M1cAutoFinishRecovery()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    int turns=0;
    var model=new PlanningTestModel(M1cDecisionJson("plan",[
        M1cStep("analysis","Evaluate the outcome"),
        M1cStep("analysis","Verify the final statement")]),new FakeModel(messages=>{
        turns++;
        if (turns==1) return new ModelReply("I am finished.",[]);
        if (turns==2)
        {
            Assert(messages.Any(x=>x.Role=="system" && x.Content?.Contains("automatically saved plan still contains unfinished steps",StringComparison.Ordinal)==true),
                "Host should provide transient continuation guidance after an early final answer");
            return new ModelReply(null,[Call("a","nira_plan_update",new{index=0,status="completed"})]);
        }
        if (turns==3) return new ModelReply(null,[Call("b","nira_plan_update",new{index=1,status="completed"})]);
        return new ModelReply("The analysis plan is now fully verified.",[]);
    }));
    var result=await setup.Engine(model).RunAsync(id,"Assess two independent conclusions",new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed && turns==4 &&
        (await setup.Store.LoadAsync(id,default))!.Plan.All(x=>x.Status=="completed"),
        "Early model final must prompt autonomous continuation, not require an extra user retry");
}

static async Task M1cAutoFinishBounded()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    int turns=0;
    var model=new PlanningTestModel(M1cDecisionJson("plan",[
        M1cStep("action","Create requested state","filesystem.write_text"),
        M1cStep("observation","Verify requested state","filesystem.read_text")]),
        new FakeModel(_=>{turns++;return new ModelReply("Done without using a tool.",[]);}));
    var result=await setup.Engine(model).RunAsync(id,"Create and verify a new file",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Paused && turns==3 &&
        state.Operations.Count==0 && state.Plan.All(x=>x.Status=="pending"),
        "Unfinished automatic plan must pause after bounded guidance; no fabricated completion");
}

static async Task M1cRecoveryNoDuplicateWrite()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    int turns=0;
    var model=new PlanningTestModel(M1cDecisionJson("plan",[
        M1cStep("action","Write the file once","filesystem.write_text"),
        M1cStep("observation","Read the file back","filesystem.read_text")]),
        new FakeModel(messages=>{
            turns++;
            if (turns==1) return new ModelReply(null,[Call("write","filesystem.write_text",new{
                path="m1c-recovery.txt",content="SAFE"})]);
            if (turns==2) return new ModelReply("All done already.",[]);
            if (turns==3)
            {
                Assert(messages.Any(x=>x.Role=="system" && x.Content?.Contains("NEVER repeat",StringComparison.Ordinal)==true),
                    "Recovery must warn against repeating successful mutations");
                string proof=M1cLastProof(messages);
                return new ModelReply(null,[Call("u1","nira_plan_update",new{index=0,status="completed",evidenceId=proof})]);
            }
            if (turns==4) return new ModelReply(null,[Call("read","filesystem.read_text",new{
                path="m1c-recovery.txt"})]);
            if (turns==5)
            {
                string proof=M1cLastProof(messages);
                return new ModelReply(null,[Call("u2","nira_plan_update",new{index=1,status="completed",evidenceId=proof})]);
            }
            return new ModelReply("Verified file contents.",[]);
        }));
    var result=await setup.Engine(model).RunAsync(id,"Create and verify m1c-recovery.txt",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed &&
        state.Operations.Count(x=>x.ToolName=="filesystem.write_text" && x.Status==OperationStatus.Succeeded)==1 &&
        state.Operations.Count(x=>x.ToolName=="filesystem.read_text" && x.Status==OperationStatus.Succeeded)==1 &&
        state.Plan.All(x=>x.Status=="completed") &&
        await File.ReadAllTextAsync(Path.Combine(setup.Workspace,"m1c-recovery.txt"))=="SAFE",
        "Autonomous continuation must use existing mutation proof, read back once, and complete");
}

static string M1cLastProof(IReadOnlyList<ChatEntry> messages)
{
    string content=messages.Last(x=>x.Role=="tool" &&
        x.Content?.Contains("receiptId",StringComparison.Ordinal)==true).Content!;
    using var parsed=JsonDocument.Parse(content);
    return parsed.RootElement.GetProperty("receiptId").GetString()!;
}

static async Task M1cPlanStatusDiagnostic()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new PlanningTestModel(M1cDecisionJson("direct",[]),new FakeModel([
        new ModelReply(null,[Call("status","nira_plan_status",new {})]),
        new ModelReply("Status recorded.",[])
    ]));
    var result=await setup.Engine(model).RunAsync(id,"Inspect the workflow status",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed &&
        state.Messages.Any(x=>x.Role=="tool" && x.Content?.Contains("\"planningOutcome\":\"direct_json\"",StringComparison.Ordinal)==true),
        "Plan-status tool must expose the truthful, persisted automatic planning result");
}

static async Task M1cManualPlanBadBinding()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var model=new FakeModel([
        new ModelReply(null,[Call("plan","nira_plan",new {steps=new object[] {
            M1cStep("action","Run a fake tool","unknown.unsafe.tool"),
            M1cStep("analysis","Report a result")}})]),
        new ModelReply("No plan recorded.",[])
    ]);
    var result=await setup.Engine(model).RunAsync(id,"Make an example plan",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Completed && state.Plan.Count==0 &&
        state.Messages.Any(x=>x.Role=="tool" && x.Content?.Contains("PLAN_TOOL_BINDING",StringComparison.Ordinal)==true),
        "Manual plans must also reject unregistered tool bindings");
}

static async Task M1cManualPlanNoAutoRecovery()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    int turns=0;
    var model=new PlanningTestModel(M1cDecisionJson("direct",[]),new FakeModel(messages=>{
        turns++;
        if (turns==1) return new ModelReply(null,[Call("plan","nira_plan",new {steps=new object[]{
            M1cStep("analysis","Describe first finding"),
            M1cStep("analysis","Describe second finding")}})]);
        return new ModelReply("I'm finished too early.",[]);
    }));
    var result=await setup.Engine(model).RunAsync(id,"Make a two-part report",new FakeApprovals(false));
    var state=(await setup.Store.LoadAsync(id,default))!;
    Assert(result.Status==ConversationStatus.Paused && turns==2 && state.Plan.Count==2 &&
        state.AutomaticPlanningOutcome=="direct_json",
        "A later model-created manual plan cannot be misclassified as a saved automatic plan");
}

static async Task M1cLegacyAutoPlanRecovery()
{
    using var setup=new TestContext();
    Guid id=Guid.NewGuid();
    var state=new ConversationState { Id=id, Objective="Finish legacy automatic plan", AutoPlanningAttempted=true,
        Plan=[new PlanStep { Description="Review first result", SuccessCriterion="Checked", Kind="analysis" },
              new PlanStep { Description="Review second result", SuccessCriterion="Checked", Kind="analysis" }] };
    await setup.Store.SaveAsync(state,default);
    int turns=0;
    var model=new FakeModel(messages=>{
        turns++;
        if (turns==1)
        {
            Assert(messages.Any(x=>x.Role=="system" &&
                x.Content?.Contains("A durable plan was already created automatically",StringComparison.Ordinal)==true),
                "Previously saved M1B plans should retain their model context after upgrading");
            return new ModelReply("Not done yet.",[]);
        }
        if (turns==2) return new ModelReply(null,[Call("first","nira_plan_update",new{index=0,status="completed"})]);
        if (turns==3) return new ModelReply(null,[Call("second","nira_plan_update",new{index=1,status="completed"})]);
        return new ModelReply("Legacy automatic plan completed.",[]);
    });
    var result=await setup.Engine(model).RunAsync(id,null,new FakeApprovals(false));
    Assert(result.Status==ConversationStatus.Completed && turns==4 &&
        (await setup.Store.LoadAsync(id,default))!.Plan.All(x=>x.Status=="completed"),
        "M1B saved automatic plans without outcome field must remain compatible");
}

// Tool-free planning fallback tests: the executor cannot be invoked by the
// fallback response, and the preflight never inherits the live tool transcript.
static async Task PlanningRetryPlan()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var fallback = M1cDecisionJson("plan", [
        M1cStep("analysis", "Analyze verified observations"),
        M1cStep("analysis", "Report supported conclusion")]);
    var model = new PlanningTestModel(new ModelReply("Not valid JSON", []),
        new FakeModel([
            new ModelReply(null, [Call("p0", "nira_plan_update", new { index=0, status="completed" })]),
            new ModelReply(null, [Call("p1", "nira_plan_update", new { index=1, status="completed" })]),
            new ModelReply("Confirmed by the evidence.", [])
        ]), fallback);
    AgentRunResult result = await setup.Engine(model).RunAsync(id,
        "Analyze this task and report the outcome", new FakeApprovals(false));
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed &&
        state.AutomaticPlanningOutcome == "planned_fallback_json" &&
        state.Plan.Count == 2 && state.Plan.All(step => step.Status == "completed"),
        "Invalid initial planning response should recover with a validated, non-executing JSON plan");
    Assert(model.PlanCalls == 1 && model.FallbackCalls == 1 && model.FallbackToolCount == 0,
        "Fallback planning must expose zero executable tools and run only once");
}

static async Task PlanningRetryDirect()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new PlanningTestModel(new ModelReply("unstructured response", []),
        new FakeModel([new ModelReply("Direct answer.", [])]),
        M1cDecisionJson("direct", []));
    AgentRunResult result = await setup.Engine(model).RunAsync(id,
        "Explain a concept", new FakeApprovals(false));
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && result.Message == "Direct answer." &&
        state.Plan.Count == 0 && state.AutomaticPlanningOutcome == "direct_fallback_json" &&
        model.FallbackCalls == 1 && model.FallbackToolCount == 0,
        "Tool-free planning retry must allow valid direct task decisions");
}

static async Task PlanningRetryRejectsTool()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new PlanningTestModel(new ModelReply("not a decision", []),
        new FakeModel([new ModelReply("No tool executed.", [])]),
        new ModelReply(null, [Call("forged", "filesystem.write_text", new {
            path="planning-should-never-write.txt", content="FORGED" })]));
    AgentRunResult result = await setup.Engine(model).RunAsync(id,
        "Discuss a possible file", new FakeApprovals(false));
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && state.AutomaticPlanningOutcome == "invalid" &&
        state.Plan.Count == 0 && state.Operations.Count == 0 &&
        !File.Exists(Path.Combine(setup.Workspace, "planning-should-never-write.txt")),
        "A tool call received during fallback cannot become an executable operation");
}

static async Task PlanningRetryRejectsBinding()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var fallback = M1cDecisionJson("plan", [
        M1cStep("action", "Create a file", "filesystem.not_registered"),
        M1cStep("analysis", "Report the outcome")]);
    var model = new PlanningTestModel(new ModelReply("not JSON", []),
        new FakeModel([new ModelReply("Did not execute anything.", [])]), fallback);
    AgentRunResult result = await setup.Engine(model).RunAsync(id,
        "Discuss creating a file", new FakeApprovals(false));
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && state.Plan.Count == 0 &&
        state.AutomaticPlanningOutcome == "invalid" && state.Operations.Count == 0,
        "A fallback plan binding an unknown executable must not be accepted");
}

static async Task PlanningRetryInvalidGraceful()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new PlanningTestModel(new ModelReply("invalid", []),
        new FakeModel([
            new ModelReply(null, [Call("w", "filesystem.write_text", new {
                path="planning-fallback-normal.txt", content="NORMAL" })]),
            new ModelReply("File written under normal host controls.", [])
        ]));
    AgentRunResult result = await setup.Engine(model).RunAsync(id,
        "Write planning-fallback-normal.txt with NORMAL", new FakeApprovals(false));
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && state.AutomaticPlanningOutcome == "invalid" &&
        model.FallbackCalls == 1 && state.Operations.Any(op => op.ToolName == "filesystem.write_text" &&
            op.Status == OperationStatus.Succeeded) &&
        await File.ReadAllTextAsync(Path.Combine(setup.Workspace, "planning-fallback-normal.txt")) == "NORMAL",
        "Unusable planner output must not block the independently authorized normal executor");
}

static async Task PlanningRetryNoRepeat()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    var model = new PlanningTestModel(new ModelReply("no decision", []),
        new FakeModel([
            new ModelReply(null, [Call("question", "nira_ask_user", new { question="What should I call it?" })]),
            new ModelReply("I will call it NIRA.", [])
        ]), M1cDecisionJson("direct", []));
    var engine = setup.Engine(model);
    AgentRunResult first = await engine.RunAsync(id, "Choose a name", new FakeApprovals(false));
    AgentRunResult second = await engine.RunAsync(id, "NIRA", new FakeApprovals(false));
    Assert(first.Status == ConversationStatus.AwaitingUser &&
        second.Status == ConversationStatus.Completed && model.PlanCalls == 1 &&
        model.FallbackCalls == 1,
        "A preflight fallback must never repeat after resuming a saved task");
}

static async Task PlanningRetryNoFallbackOnValid()
{
    using var setup = new TestContext();
    var model = new PlanningTestModel(AutoPlanChoice("direct"),
        new FakeModel([new ModelReply("Hello.", [])]));
    AgentRunResult result = await setup.Engine(model).RunAsync(Guid.NewGuid(),
        "Hi", new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && model.PlanCalls == 1 &&
        model.FallbackCalls == 0,
        "Do not waste a second provider call after a valid planning decision");
}

static async Task PlanningRetryWireRoundTrip()
{
    using var setup = new TestContext();
    using var handler = new PlanningRetryWireHandler();
    using var http = new HttpClient(handler);
    var provider = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"),
        "mock", () => "mock-private-key");
    Guid id = Guid.NewGuid();
    AgentRunResult result = await setup.Engine(provider).RunAsync(id,
        "Review two observations and summarize your findings", new FakeApprovals(false));
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed &&
        state.AutomaticPlanningOutcome == "planned_fallback_json" &&
        state.Plan.Count == 2 && state.Plan.All(step => step.Status == "completed") &&
        handler.RequestCount == 5,
        "Real HTTP provider transcript must save the fallback plan and complete its evidence-free analysis steps");
    Assert(state.Operations.Count == 0,
        "Planning fallback must not dispatch an external effect or approval");
}


static ModelReply ModelCall(string name, string arguments) =>
    new(null, [new ModelToolCall(Guid.NewGuid().ToString("N"), name, arguments)]);

// Milestone 2: the same NIRA host, permission broker and journal drive desktop
// actions as file/browser actions. A fake adapter never touches the test machine.
static Task DesktopToolsRegistered()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var backend = new FakeDesktopBackend();
    ToolRegistry registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http,
        desktopBackend: backend);
    foreach (string name in new[] { "desktop.windows_list", "desktop.window_inspect",
        "desktop.window_activate", "desktop.control_invoke", "desktop.control_sequence", "desktop.control_set_text",
        "desktop.control_type_text" })
        Assert(registry.TryGet(name, out var tool) && tool is not null,
            "Missing registered desktop tool: " + name);
    return Task.CompletedTask;
}

static Task DesktopSchemaStrict()
{
    var backend = new FakeDesktopBackend();
    foreach (IAgentTool tool in new IAgentTool[] {new DesktopWindowsTool(backend),
        new DesktopInspectTool(backend), new DesktopActivateTool(backend),
        new DesktopInvokeTool(backend), new DesktopSetTextTool(backend),
        new DesktopTypeTextTool(backend)})
    {
        JsonElement args = JsonSerializer.SerializeToElement(new { unexpected="payload" });
        Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters, args, out _),
            "Desktop tools must reject unsupported argument properties");
    }
    var textTool = new DesktopSetTextTool(backend);
    Assert(textTool.Descriptor.Risk == ToolRisk.Dangerous && textTool.Descriptor.HasSideEffects,
        "Desktop text modification must always require authorization");
    return Task.CompletedTask;
}

static async Task<(AgentRunResult Result, ConversationState State, FakeDesktopBackend Backend,
    FakeApprovals Approvals)> DesktopSingleAction(string name, string arguments, bool approved)
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend();
    var model = new FakeModel(new[] { ModelCall(name, arguments), new ModelReply("Done.", []) });
    var approvals = new FakeApprovals(approved);
    Guid id = Guid.NewGuid();
    AgentRunResult result = await setup.Engine(model, backend).RunAsync(id, "Use the desktop tool", approvals);
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    return (result, state, backend, approvals);
}

static async Task DesktopListDenied()
{
    var r = await DesktopSingleAction("desktop.windows_list", "{}", false);
    Assert(r.Result.Status == ConversationStatus.Paused && r.Backend.ListCalls == 0 &&
        r.Approvals.Calls == 1, "Desktop inventory cannot bypass host approval");
}
static async Task DesktopListApproved()
{
    var r = await DesktopSingleAction("desktop.windows_list", "{}", true);
    Assert(r.Result.Status == ConversationStatus.Completed && r.Backend.ListCalls == 1 &&
        r.State.Operations.Single().Status == OperationStatus.Succeeded &&
        r.State.Operations.Single().ResultJson!.Contains("win-demo-0001", StringComparison.Ordinal),
        "Approved desktop listing must give observed stable window identity");
}
static async Task DesktopInspectDenied()
{
    var r = await DesktopSingleAction("desktop.window_inspect",
        "{\"windowId\":\"win-demo-0001\"}", false);
    Assert(r.Result.Status == ConversationStatus.Paused && r.Backend.InspectCalls == 0 &&
        r.Approvals.Calls == 1, "Desktop inspection must be independently approved");
}
static async Task DesktopInspectApproved()
{
    var r = await DesktopSingleAction("desktop.window_inspect",
        "{\"windowId\":\"win-demo-0001\"}", true);
    Assert(r.Result.Status == ConversationStatus.Completed && r.Backend.InspectCalls == 1 &&
        r.State.Operations.Single().ResultJson!.Contains("ctrl-button-01", StringComparison.Ordinal) &&
        !r.State.Operations.Single().ResultJson!.Contains("secret-existing-input", StringComparison.Ordinal),
        "Desktop inspect must provide control metadata without field values");
}
static async Task DesktopActivateDenied()
{
    var r = await DesktopSingleAction("desktop.window_activate",
        "{\"windowId\":\"win-demo-0001\"}", false);
    Assert(r.Result.Status == ConversationStatus.Paused && r.Backend.ActivateCalls == 0 &&
        r.Approvals.Calls == 1, "Host rejection must prevent window activation");
}
static async Task DesktopActivateApproved()
{
    var r = await DesktopSingleAction("desktop.window_activate",
        "{\"windowId\":\"win-demo-0001\"}", true);
    Assert(r.Result.Status == ConversationStatus.Completed && r.Backend.ActivateCalls == 1 &&
        r.State.Operations.Single().ResultJson!.Contains("ChangedState\":true", StringComparison.Ordinal),
        "Verified activation must record a changed-state host receipt");
}
static async Task DesktopInvokeDenied()
{
    var r = await DesktopSingleAction("desktop.control_invoke",
        "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-button-01\"}", false);
    Assert(r.Result.Status == ConversationStatus.Paused && r.Backend.InvokeCalls == 0,
        "Approval denial must prevent UIA invocation");
}
static async Task DesktopInvokeApproved()
{
    var r = await DesktopSingleAction("desktop.control_invoke",
        "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-button-01\"}", true);
    Assert(r.Result.Status == ConversationStatus.Completed && r.Backend.InvokeCalls == 1 &&
        r.State.Operations.Single().ResultJson!.Contains("Verified\":false", StringComparison.Ordinal),
        "A button dispatch receipt must not claim that the app outcome was verified");
}
static async Task DesktopSetTextDenied()
{
    var r = await DesktopSingleAction("desktop.control_set_text",
        "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-edit-001\",\"text\":\"Hello\"}", false);
    Assert(r.Result.Status == ConversationStatus.Paused && r.Backend.SetTextCalls == 0,
        "Text must never be entered without approval");
}
static async Task DesktopSetTextApproved()
{
    var r = await DesktopSingleAction("desktop.control_set_text",
        "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-edit-001\",\"text\":\"Hello\"}", true);
    Assert(r.Result.Status == ConversationStatus.Completed && r.Backend.SetTextCalls == 1 &&
        r.Backend.LastText == "Hello" &&
        r.State.Operations.Single().ResultJson!.Contains("Verified\":true", StringComparison.Ordinal),
        "Authorized non-password text set must be verified");
}
static async Task DesktopStaleWindow()
{
    var r = await DesktopSingleAction("desktop.window_activate",
        "{\"windowId\":\"win-stale-999\"}", true);
    Assert(r.Result.Status == ConversationStatus.Completed &&
        r.State.Operations.Single().Status == OperationStatus.Failed &&
        r.State.Operations.Single().ResultJson!.Contains("WINDOW_STALE", StringComparison.Ordinal),
        "Stale identities must not be silently retargeted");
}
static async Task DesktopStaleControl()
{
    var r = await DesktopSingleAction("desktop.control_invoke",
        "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-stale-99\"}", true);
    Assert(r.Result.Status == ConversationStatus.Completed && r.Backend.InvokeCalls == 0 &&
        r.State.Operations.Single().Status == OperationStatus.Failed,
        "Control IDs must be revalidated against the selected window");
}
static async Task DesktopEndToEnd()
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend();
    var model = new FakeModel(new[] {
        ModelCall("desktop.windows_list", "{}"),
        ModelCall("desktop.window_activate", "{\"windowId\":\"win-demo-0001\"}"),
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-demo-0001\"}"),
        ModelCall("desktop.control_invoke",
            "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-button-01\"}"),
        new ModelReply("The button was dispatched; resulting app state is not verified.", [])
    });
    var approvals = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    AgentRunResult result = await setup.Engine(model, backend).RunAsync(id,
        "Inspect my open apps, activate a window, and invoke one button", approvals);
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && approvals.Calls == 4 &&
        backend.ListCalls == 1 && backend.ActivateCalls == 1 &&
        backend.InspectCalls == 1 && backend.InvokeCalls == 1 &&
        state.Operations.Count == 4 && state.Operations.All(op => op.Status == OperationStatus.Succeeded) &&
        result.EvidenceIds.Count == 2,
        "A general-purpose desktop task must route through four separate approved/audited tools");
}

static Task DesktopRichEditorMetadata()
{
    var backend = new FakeDesktopBackend();
    DesktopControl rich = backend.Inspect("win-demo-0001").Controls.Single(c => c.ControlId == "ctrl-doc-01");
    Assert(!rich.CanSetText && rich.CanTypeText,
        "A multiline rich editor must be distinguishable from a writable ValuePattern field");
    return Task.CompletedTask;
}
static async Task DesktopTypeTextDenied()
{
    var outcome = await DesktopSingleAction("desktop.control_type_text",
        "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\",\"requireEmpty\":true}",
        false);
    Assert(outcome.Result.Status == ConversationStatus.Paused && outcome.Backend.TypeTextCalls == 0 &&
        outcome.Approvals.Calls == 1, "Separate denial must block all keyboard input");
}
static async Task DesktopTypeTextApproved()
{
    var outcome = await DesktopSingleAction("desktop.control_type_text",
        "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\",\"requireEmpty\":true}",
        true);
    Assert(outcome.Result.Status == ConversationStatus.Completed && outcome.Backend.TypeTextCalls == 1 &&
        outcome.Backend.LastText == "Hello" && outcome.Approvals.Calls == 1 &&
        outcome.State.Operations.Single().ResultJson!.Contains("\"Verified\":true", StringComparison.Ordinal),
        "Approved rich text must record only an actual successful receipt");
}
static async Task DesktopTypeTextRequiresEmpty()
{
    var backend = new FakeDesktopBackend();
    var tool = new DesktopTypeTextTool(backend);
    ToolReceipt receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        windowId="win-demo-0001", controlId="ctrl-doc-01", text="Hello", requireEmpty=false
    }), default);
    Assert(!receipt.Success && receipt.ErrorCode == "EMPTY_REQUIRED" && backend.TypeTextCalls == 0,
        "Explicit empty-editor invariant must not be bypassed");
}
static async Task DesktopTypeTextControlsRefused()
{
    var backend = new FakeDesktopBackend();
    var tool = new DesktopTypeTextTool(backend);
    ToolReceipt receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        windowId="win-demo-0001", controlId="ctrl-doc-01", text="Hello\nWorld", requireEmpty=true
    }), default);
    Assert(!receipt.Success && receipt.ErrorCode == "TEXT_CHARACTERS_UNSUPPORTED" && backend.TypeTextCalls == 0,
        "Control characters or implicit shortcuts must never be injected");
}
static async Task DesktopUnsupportedSetTextPauses()
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend();
    var model = new FakeModel(new[] {
        ModelCall("desktop.control_set_text",
            "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\"}"),
        new ModelReply("Try typing later", [])
    });
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model, backend).RunAsync(id, "Try set text in rich editor", new FakeApprovals(true));
    ConversationState saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && saved.RetryBlockCode == "CONTROL_REQUIRES_KEYBOARD" &&
        saved.Operations.Count == 1 && saved.Operations.Single().Status == OperationStatus.Failed &&
        backend.SetTextCalls == 0,
        "A rejected action with no verified alternative must remain paused");
}
static async Task DesktopBlockedRetrySafe()
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend();
    var model = new FakeModel(new[] {
        ModelCall("desktop.control_set_text",
            "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\"}"),
        new ModelReply("The requested method is unsupported.", [])
    });
    var engine = setup.Engine(model, backend);
    Guid id = Guid.NewGuid();
    var first = await engine.RunAsync(id, "Set text in a rich editor", new FakeApprovals(true));
    var second = await engine.RunAsync(id, null, new FakeApprovals(true));
    var third = await engine.RunAsync(id, "resume", new FakeApprovals(true));
    Assert(first.Status == ConversationStatus.Paused && second.Status == ConversationStatus.Paused &&
        third.Status == ConversationStatus.Paused && backend.SetTextCalls == 0,
        "Retrying blocked, unchanged input must not invoke the provider or dispatch another tool");
}
static async Task DesktopSetPermissionRoutesHost()
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend();
    var model = new FakeModel(new[] {
        ModelCall("nira_ask_user", "{\"question\":\"May I set the text in this editor?\"}"),
        ModelCall("desktop.control_type_text",
            "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\",\"requireEmpty\":true}"),
        new ModelReply("Approved operation verified.", [])
    });
    Guid id = Guid.NewGuid();
    var r = await setup.Engine(model, backend).RunAsync(id,
        "User permits foreground keyboard input in the blank editor", new FakeApprovals(true));
    Assert(r.Status == ConversationStatus.Completed && backend.TypeTextCalls == 1,
        "May I set text must be treated as a tool-approval probe, not a user clarification");
}

static async Task GenericRecoveryFreshObservation()
{
    using var setup = new TestContext();
    var action = new GenericRecoveryTool("example.change", true, args => args.GetProperty("target").GetString() == "old"
        ? ToolReceipt.Fail("ANY_NEW_PRECONDITION", "Target changed before dispatch.", recovery: ToolRecovery.Reobserve)
        : ToolReceipt.Ok("Observed new target updated.", changedState: true));
    var observation = new GenericRecoveryTool("example.observe", false, _ => ToolReceipt.Ok("New target observed."));
    var model = new FakeModel(new[] {
        ModelCall("example.change", "{\"target\":\"old\"}"),
        ModelCall("example.change", "{\"target\":\"new\"}"), // must not execute before observation
        ModelCall("example.observe", "{\"target\":\"new\"}"),
        ModelCall("example.change", "{\"target\":\"new\"}"), new ModelReply("New target updated.", [])
    });
    var approval = new FakeApprovals(true); Guid id = Guid.NewGuid();
    var engine = new NiraEngine(model, new ToolRegistry([action, observation]), setup.Store, new WorkspaceAuthority(setup.Workspace));
    var result = await engine.RunAsync(id, "Update the intended target", approval);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && action.Calls == 2 && observation.Calls == 1 &&
        approval.Calls == 3 && state.Operations.First().Recovery == ToolRecovery.Reobserve &&
        state.Messages.Any(m => m.Content?.Contains("RECOVERY_PRECONDITION", StringComparison.Ordinal) == true),
        "An unfamiliar rejection must require fresh observation and separate approval before an alternative effect.");
}

static async Task GenericRecoveryBoundsReplay()
{
    using var setup = new TestContext();
    var action = new GenericRecoveryTool("example.change", true, _ => ToolReceipt.Fail("UNKNOWN_CONDITION", "Not applied.", recovery: ToolRecovery.Reobserve));
    var observation = new GenericRecoveryTool("example.observe", false, _ => ToolReceipt.Ok("Observed."));
    var model = new FakeModel(new[] {
        ModelCall("example.change", "{\"target\":\"x\"}"), ModelCall("example.observe", "{\"target\":\"x\"}"),
        ModelCall("example.change", "{ \"target\": \"x\" }"), ModelCall("example.observe", "{\"target\":\"x\"}"),
        ModelCall("example.change", "{\"target\":\"x\"}"), new ModelReply("Done", [])
    });
    var engine = new NiraEngine(model, new ToolRegistry([action, observation]), setup.Store, new WorkspaceAuthority(setup.Workspace));
    var result = await engine.RunAsync(Guid.NewGuid(), "Change target x", new FakeApprovals(true));
    Assert(result.Status == ConversationStatus.Paused && action.Calls == 2 && observation.Calls == 2,
        "Repeated equivalent rejected operations must be bounded even after new observations.");
}

static async Task GenericRecoveryUnverified()
{
    using var setup = new TestContext();
    var action = new GenericRecoveryTool("example.change", true, _ => ToolReceipt.Ok("Effect uncertain.", changedState: true, recovery: ToolRecovery.UnverifiedEffect));
    var engine = new NiraEngine(new FakeModel(new[] { ModelCall("example.change", "{\"target\":\"x\"}") }),
        new ToolRegistry([action]), setup.Store, new WorkspaceAuthority(setup.Workspace));
    Guid id = Guid.NewGuid(); var approval = new FakeApprovals(true);
    var first = await engine.RunAsync(id, "Update x", approval);
    var second = await engine.RunAsync(id, "Try again", approval);
    Assert(first.Status == ConversationStatus.Paused && second.Status == ConversationStatus.Paused && action.Calls == 1 && approval.Calls == 1,
        "Uncertain effects must block replay for every capability, not just text entry.");
}

static async Task GenericRecoveryDenial()
{
    using var setup = new TestContext();
    var action = new GenericRecoveryTool("example.change", true, _ => ToolReceipt.Fail("UNFAMILIAR", "No effect.", recovery: ToolRecovery.Reobserve));
    var observation = new GenericRecoveryTool("example.observe", false, _ => ToolReceipt.Ok("Alternate target observed."));
    var model = new FakeModel(new[] { ModelCall("example.change", "{\"target\":\"x\"}"),
        ModelCall("example.observe", "{\"target\":\"y\"}"), ModelCall("example.change", "{\"target\":\"y\"}") });
    var engine = new NiraEngine(model, new ToolRegistry([action, observation]), setup.Store, new WorkspaceAuthority(setup.Workspace));
    var result = await engine.RunAsync(Guid.NewGuid(), "Update target", new ScriptedApprovals([true, true, false]));
    Assert(result.Status == ConversationStatus.Paused && action.Calls == 1 && observation.Calls == 1,
        "Recovery must not grant authority for an alternative rejected by the user.");
}

static string OrderedControlArgs() => JsonSerializer.Serialize(new {
    windowId = "win-seq-0001", steps = new[] {
        new { controlId = "ctrl-first-01", name = "First" },
        new { controlId = "ctrl-second-02", name = "Second" }
    }, observeAutomationId = "display", expectedName = "Final result"
});

static async Task M9DocumentCompatibility()
{
    using var setup = new TestContext(); using var http = new HttpClient();
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    const string proposal = """
        {"path":"report.docx","title":"Observations","blocks":[
          {"type":"heading","level":2,"text":"Summary"},
          {"type":"paragraph","text":"Preserve café, α and exact line\nbreaks."},
          {"type":"table","header":["Item","Value"],"rows":[["A","001"],["B","-2.50"]]},
          {"kind":"paragraph","text":"Source: https://example.org/reference"}]}
        """;
    Assert(registry.Validate("productivity.create_document", proposal, out var normalized, out _), "Document aliases must resolve to the existing canonical schema.");
    Assert(normalized.GetProperty("blocks")[2].GetProperty("rows").GetArrayLength() == 3 &&
        normalized.GetProperty("blocks")[2].GetProperty("rows")[0][0].GetString() == "Item" &&
        normalized.GetProperty("blocks")[2].GetProperty("rows")[1][1].GetString() == "001",
        "Preserve exact header and cell order; do not coerce strings to numbers.");
    var model = new FakeModel([ModelCall("productivity.create_document", proposal), new ModelReply("Created report.", [])]);
    var approvals = new FakeApprovals(true); Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true),
        enableCapabilityRouting: true, enableAdaptivePlanning: true, enableLocalSchemaRanking: true)
        .RunAsync(id, "Create a Word document with the report and its table.", approvals);
    var saved = (await setup.Store.LoadAsync(id, default))!;
    var operation = saved.Operations.Single();
    var artifact = RecordedFileArtifacts.From(saved).Single();
    Assert(result.Status == ConversationStatus.Completed && model.PromptSnapshots.Count == 2 && approvals.Calls == 1 &&
        operation.Status == OperationStatus.Succeeded && approvals.Requests.Single().ArgumentsJson == operation.ArgumentsJson &&
        await RecordedFileArtifacts.MatchesAsync(artifact, setup.Workspace),
        "Compatible document creation must approve canonical content, execute once and independently verify the artifact.");
    using var doc = System.IO.Compression.ZipFile.OpenRead(artifact.Path);
    using var reader = new StreamReader(doc.GetEntry("word/document.xml")!.Open());
    var xml = System.Xml.Linq.XDocument.Parse(await reader.ReadToEndAsync());
    System.Xml.Linq.XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    var rows = xml.Descendants(w + "tr").Select(r => r.Elements(w + "tc").Select(c => string.Concat(c.Descendants(w + "t").Select(t => t.Value))).ToArray()).ToArray();
    Assert(rows.Length == 3 && rows[0].SequenceEqual(new[] { "Item", "Value" }) && rows[1].SequenceEqual(new[] { "A", "001" }) &&
        rows[2].SequenceEqual(new[] { "B", "-2.50" }), "The actual OpenXML document must include the exact table header and all original rows.");
}

static async Task M9DocumentCompatibilitySafety()
{
    using var setup = new TestContext(); using var http = new HttpClient();
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    foreach (string block in new[] {
        """{"kind":"paragraph","type":"heading","text":"ambiguous"}""",
        """{"type":"paragraph","text":"x","header":["not a table"]}""",
        """{"type":"table","header":[1],"rows":[["x"]]}""",
        """{"type":"table","header":["x"]}""",
        """{"type":"paragraph","text":"x","overwrite":true}""",
        """{"type":"paragraph","type":"heading","text":"x"}""" })
    {
        string args = "{\"path\":\"out.docx\",\"title\":\"Title\",\"blocks\":[" + block + "]}";
        Assert(!registry.Validate("productivity.create_document", args, out _, out _), "Reject ambiguous, malformed or unsupported document aliases before dispatch.");
    }
    string canonical = """{"path":"out.docx","title":"Title","blocks":[{"kind":"table","rows":[["Header"],["Value"]]}]}""";
    Assert(registry.Validate("productivity.create_document", canonical, out var existing, out _) && existing.GetRawText() == canonical,
        "Existing canonical document contract must stay unchanged.");
    const string proposal = """{"path":"out.docx","title":"Title","blocks":[{"type":"paragraph","text":"Protected write"}]}""";
    var approvals = new FakeApprovals(false);
    var model = new FakeModel([ModelCall("productivity.create_document", proposal)]);
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true))
        .RunAsync(Guid.NewGuid(), "Create a document", approvals);
    Assert(result.Status == ConversationStatus.Paused && approvals.Calls == 1 && !File.Exists(Path.Combine(setup.Workspace, "out.docx")),
        "Normalization never grants permission or writes after denial.");
}

static async Task M9CompleteFollowupContext()
{
    using var setup = new TestContext();
    string answer = "Opening section. " + new string('x', 6700) + " Closing section.";
    var parent = new ConversationState { Status = ConversationStatus.Completed, Objective = "Explain a topic",
        LastAnswer = answer, Messages = [new ChatEntry("user", "Explain a topic"), new ChatEntry("tool", "PRIVATE_TOOL_DATA", "call")] };
    await setup.Store.SaveAsync(parent, default);
    Guid child = await setup.Store.CreateFollowupAsync(parent.Id, default);
    var saved = (await setup.Store.LoadAsync(child, default))!;
    Assert(saved.PreviousChatContext?.Contains(answer, StringComparison.Ordinal) == true &&
        saved.PreviousChatContext?.Contains("Explain a topic", StringComparison.Ordinal) == true &&
        saved.PreviousChatContext?.Contains("PRIVATE_TOOL_DATA", StringComparison.Ordinal) == false && saved.Operations.Count == 0,
        "Recent complete answer and request must survive follow-up without inheriting receipts.");
    var model = new FakeModel(messages => {
        Assert(messages.Any(m => m.Role == "system" && m.Content?.Contains(answer, StringComparison.Ordinal) == true),
            "Engine must not truncate the stored recent exchange a second time.");
        return new ModelReply("Acknowledged.", []);
    });
    await setup.Engine(model, adaptive: true).RunAsync(child, "Create a document for this", new FakeApprovals(false));
    parent.LastAnswer = "Opening section. " + new string('x', 14000) + " Closing section.";
    await setup.Store.SaveAsync(parent, default);
    var longChild = (await setup.Store.LoadAsync(await setup.Store.CreateFollowupAsync(parent.Id, default), default))!;
    Assert(longChild.PreviousChatContext is { } context && context.Length <= ConversationState.PreviousChatContextLimit &&
        context.Contains("Opening section.", StringComparison.Ordinal) && context.Contains("Closing section.", StringComparison.Ordinal) &&
        context.Contains("omitted by context limit", StringComparison.Ordinal),
        "Exceptionally long history stays bounded and explicitly incomplete.");
}

static Task M9ImageEmptyAlternative()
{
    const string url = "https://images.example.org/photo.png?variant=preview";
    var state = new ConversationState();
    state.Operations.Add(new AgentOperation { ToolName = "image.search", Status = OperationStatus.Succeeded,
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Observed", new { candidates = new[] {
            new { CandidateId = "observed-id", MediaUrl = url, ThumbnailUrl = url } } })) });
    JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
    Assert(ImageEvidenceBinding.BindRetrieval(state, Args("""{"candidateIds":["observed-id"],"urls":[]}"""), out var byId, out _) is null &&
        byId.GetProperty("urls")[0].GetString() == url, "An empty alternative cannot invalidate genuine observed IDs.");
    Assert(ImageEvidenceBinding.BindRetrieval(state, JsonSerializer.SerializeToElement(new { candidateIds = Array.Empty<string>(), urls = new[] { url } }), out var byUrl, out _) is null &&
        byUrl.GetRawText() == byId.GetRawText(), "Legacy URL selection with empty ID list binds identically.");
    foreach (string bad in new[] {
        """{"candidateIds":[],"urls":[]}""", """{"candidateIds":["observed-id"],"urls":["https://images.example.org/photo.png?variant=preview"]}""",
        """{"candidateIds":["unknown"],"urls":[]}""", """{"candidateIds":["observed-id"],"urls":null}""",
        """{"candidateIds":["observed-id"],"candidateIds":[],"urls":[]}""", """{"candidateIds":["observed-id"],"extra":[]}""" })
        Assert(ImageEvidenceBinding.BindRetrieval(state, Args(bad), out _, out _) == "IMAGE_NOT_OBSERVED", "Ambiguous or unobserved selection never dispatches.");
    Assert(ImageEvidenceBinding.IsBareRetrievalArguments(state, """{"candidateIds":["observed-id"],"urls":[]}"""),
        "Bare JSON remains a protocol error, never an executed action.");
    return Task.CompletedTask;
}

static async Task M9BrowserTextOnly()
{
    using var setup = new TestContext();
    var reader = new FakeBrowserReader();
    var tool = new PublicBrowserInspectionTool(reader);
    var text = JsonSerializer.SerializeToElement(new { url = "https://www.example.com/", textOnly = true });
    var full = JsonSerializer.SerializeToElement(new { url = "https://www.example.com/" });
    Assert(JsonToolSchema.Validate(tool.Descriptor.Parameters, text, out _) && JsonToolSchema.Validate(tool.Descriptor.Parameters, full, out _),
        "Both modes retain the registered schema.");
    var denied = await new NiraEngine(new FakeModel([new ModelReply(null, [Call("deny", "browser.inspect_public", new { url = "https://www.example.com/", textOnly = true })])]),
        new ToolRegistry([tool]), setup.Store, new WorkspaceAuthority(setup.Workspace)).RunAsync(Guid.NewGuid(), "Read this public page", new FakeApprovals(false));
    Assert(denied.Status == ConversationStatus.Paused && reader.Calls == 0 && reader.TextCalls == 0,
        "Text-only mode cannot bypass network approval.");
    var first = await tool.ExecuteAsync(text, default);
    Assert(first.Success && reader.TextCalls == 1 && reader.Calls == 0 && first.Data.GetProperty("VisibleText").GetString() == "Example body" &&
        first.Data.GetProperty("Links").GetArrayLength() == 0 && first.Data.GetProperty("Elements").GetArrayLength() == 0 &&
        first.Data.GetProperty("inspectionMode").GetString() == "text_only", "Text-only path does not fabricate control or link evidence.");
    var second = await tool.ExecuteAsync(full, default);
    Assert(second.Success && reader.Calls == 1 && second.Data.GetProperty("Links").GetArrayLength() == 1,
        "Existing full inspection remains the default and retains links.");
    reader.FinalUrl = "https://other.example.net/";
    Assert((await tool.ExecuteAsync(text, default)).ErrorCode == "BROWSER_REDIRECT_BLOCKED", "Text-only path keeps origin verification.");
}

static async Task M9CompactToolWire()
{
    using var handler = new FakeApiHandler("""{"choices":[{"message":{"content":"Done."}}]}""");
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    string content = JsonSerializer.Serialize(new { Success = true, Data = new { text = string.Concat(Enumerable.Repeat("<item> \"quoted\" & é ", 30)), number = 1.25, path = "C:\\folder\\file.txt" } });
    // Tool receipt JSON is inert data; exact values, call IDs, user text and model arguments are unchanged.
    var call = new ModelToolCall("call-1", "sample.read", """{"target":"a"}""");
    ChatEntry[] messages = [new("user", content), new("assistant", ToolCalls: [call]), new("tool", content, call.Id)];
    await model.CompleteAsync(messages, [], default);
    using var body = JsonDocument.Parse(handler.RequestBody!);
    var wire = body.RootElement.GetProperty("messages");
    string compact = wire[2].GetProperty("content").GetString()!;
    using var original = JsonDocument.Parse(content); using var projected = JsonDocument.Parse(compact);
    Assert(JsonElement.DeepEquals(original.RootElement, projected.RootElement) && compact.Length < content.Length &&
        wire[0].GetProperty("content").GetString() == content && wire[2].GetProperty("tool_call_id").GetString() == call.Id &&
        wire[1].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString() == call.ArgumentsJson &&
        messages[2].Content == content, "Only the model-facing tool JSON serialization is compacted losslessly.");
}

static Task M9ImageCandidateBinding()
{
    const string first = "https://images.example.org/a.jpg?variant=original";
    const string thumb = "https://images.example.org/a-small.jpg?variant=preview";
    const string second = "https://images.example.org/b.jpg";
    var state = new ConversationState();
    state.Operations.Add(new AgentOperation { ToolName = "image.search", Status = OperationStatus.Succeeded,
        ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Observed", new { candidates = new[] {
            new { CandidateId = "a", MediaUrl = first, ThumbnailUrl = thumb },
            new { CandidateId = "b", MediaUrl = second, ThumbnailUrl = "" } } })) });
    JsonElement Select(params string[] ids) => JsonSerializer.SerializeToElement(new { candidateIds = ids });
    Assert(ImageEvidenceBinding.BindRetrieval(state, Select("a", "b"), out var bound, out _) is null &&
        bound.GetProperty("urls").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { thumb, second }),
        "Observed candidates must bind to their exact preferred URLs.");
    Assert(ImageEvidenceBinding.BindRetrieval(state, Select("unobserved"), out _, out _) == "IMAGE_NOT_OBSERVED" &&
        ImageEvidenceBinding.BindRetrieval(state, JsonSerializer.SerializeToElement(new { urls = new[] { "https://images.example.org/a.jpg" } }), out _, out _) == "IMAGE_NOT_OBSERVED",
        "An unknown ID or changed URL must not authorize a fetch.");
    state.Operations.Add(new AgentOperation { ToolName = "image.retrieve", Status = OperationStatus.Succeeded,
        ArgumentsJson = bound.GetRawText(), ResultJson = JsonSerializer.Serialize(ToolReceipt.Ok("Partial", new {
            previews = new[] { new { url = second, path = "preview.png", sha256 = new string('A', 64) } }, failed = 1 })) });
    Assert(ImageEvidenceBinding.BindRetrieval(state, Select("a", "b"), out bound, out _) is null &&
        bound.GetProperty("urls").GetArrayLength() == 1 && bound.GetProperty("urls")[0].GetString() == first,
        "Only the unattempted alternate rendition may be selected; the successful candidate is not repeated.");
    state.Operations.Add(new AgentOperation { ToolName = "image.retrieve", Status = OperationStatus.Failed, ArgumentsJson = bound.GetRawText() });
    Assert(ImageEvidenceBinding.BindRetrieval(state, Select("a", "b"), out _, out _) == "IMAGE_ALREADY_ATTEMPTED",
        "No further dispatch when all observed renditions were attempted or retrieved.");
    Assert(ImageEvidenceBinding.BindRetrieval(state, JsonSerializer.SerializeToElement(new { urls = new[] { second } }), out _, out _) == "IMAGE_ALREADY_ATTEMPTED",
        "Legacy exact URLs must also avoid repeated retrieval.");
    return Task.CompletedTask;
}

static async Task M9UnboundPlanRoutingRepair()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var candidate = new ApplicationCandidate(@"C:\Windows\calc.exe", new string('A', 64), "fixture");
    var backend = new M7cFakeApplicationBackend(candidate);
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, applicationBackend: backend);
    // Replay the five proposals from the failed diagnostic, then correct the discovery schema.
    var model = new FakeModel(new ModelReply[] {
        ModelCall("nira_plan", """{"steps":[{"action":"Find Calculator executable","description":"Use desktop.application_find to locate calc.exe"},{"action":"Launch Calculator","description":"Launch the observed executable path for Calculator"},{"action":"List windows","description":"Get the Calculator window identifier"},{"action":"Inspect Calculator window","description":"Retrieve accessibility metadata for buttons"},{"action":"Invoke calculation sequence","description":"Press 1,2,8,*,2,4,= using desktop.control_sequence"},{"action":"Capture screenshot","description":"Capture the Calculator window showing result"}]}"""),
        ModelCall("nira_plan", """{"steps":[{"action":"Find Calculator executable","description":"Locate the Windows Calculator executable (calc.exe) using desktop.application_find","successCriterion":"Calculator executable path is returned"},{"action":"Launch Calculator","description":"Launch the found Calculator executable with desktop.application_launch","successCriterion":"Calculator process starts (receipt)"},{"action":"List windows","description":"List top‑level windows to obtain the Calculator window ID and title","successCriterion":"Calculator window appears in list"},{"action":"Inspect Calculator window","description":"Inspect the Calculator window to get accessibility IDs for number and operation buttons","successCriterion":"Button IDs for digits 1,2,8,*,2,4,= are obtained"},{"action":"Invoke calculation sequence","description":"Use desktop.control_sequence to press 1,2,8,*,2,4,= in order","successCriterion":"Calculator displays the result 3072"},{"action":"Capture screenshot","description":"Capture a screenshot of the Calculator window showing the result","successCriterion":"Screenshot image receipt is returned"}]}"""),
        ModelCall("nira_plan", """{"steps":[{"description":"Locate the Windows Calculator executable (calc.exe) using desktop.application_find","kind":"action","successCriterion":"Calculator executable path is returned","toolName":"desktop.application_find"},{"description":"Launch the found Calculator executable with desktop.application_launch","kind":"action","successCriterion":"Calculator process starts (receipt)","toolName":"desktop.application_launch"},{"description":"List top-level windows to obtain the Calculator window ID and title","kind":"action","successCriterion":"Calculator window appears in list","toolName":"desktop.windows_list"},{"description":"Inspect the Calculator window to get accessibility IDs for number and operation buttons","kind":"action","successCriterion":"Button IDs for digits 1,2,8,*,2,4,= are obtained","toolName":"desktop.window_inspect"},{"description":"Press 1,2,8,*,2,4,= using desktop.control_sequence","kind":"action","successCriterion":"Calculator displays the result 3072","toolName":"desktop.control_sequence"},{"description":"Capture a screenshot of the Calculator window showing the result","kind":"action","successCriterion":"Screenshot image receipt is returned","toolName":"desktop.window_capture"}]}"""),
        ModelCall("nira_plan", """{"steps":[{"description":"Locate the Windows Calculator executable (calc.exe) using desktop.application_find","kind":"observation","successCriterion":"Calculator executable path is returned"},{"description":"Launch the found Calculator executable with desktop.application_launch","kind":"action","successCriterion":"Calculator process starts (receipt)"},{"description":"List top‑level windows to obtain the Calculator window ID and title","kind":"observation","successCriterion":"Calculator window appears in list"},{"description":"Inspect the Calculator window to get accessibility IDs for the needed buttons","kind":"observation","successCriterion":"Button IDs for digits 1,2,8,*,2,4,= are obtained"},{"description":"Press 1,2,8,*,2,4,= using desktop.control_sequence","kind":"action","successCriterion":"Calculator displays the result 3072"},{"description":"Capture a screenshot of the Calculator window showing the result","kind":"observation","successCriterion":"Screenshot image receipt is returned"}]}"""),
        ModelCall("desktop.application_find", """{"applicationQueries":["calc.exe"]}"""),
        ModelCall("desktop.application_find", """{"executable":"calc.exe"}"""),
        new ModelReply(null, [Call("launch", "desktop.application_launch", new { executablePath = candidate.ExecutablePath, fingerprint = candidate.Fingerprint })])
    });
    var approvals = new FakeApprovals(false); Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true),
        enableCapabilityRouting: true, enableAdaptivePlanning: true, enableLocalSchemaRanking: true)
        .RunAsync(id, "Open Calculator, calculate 128 multiplied by 24 using its controls, verify the result and show me a screenshot.", approvals);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(model.ToolSnapshots.Count == 7 && model.ToolSnapshots.Take(6).All(ts => ts.Any(d => d.Name == "desktop.application_find")) &&
        state.Plan.Count == 6 && state.Operations.Any(o => o.ToolName == "desktop.application_find" && o.Status == OperationStatus.Succeeded) &&
        result.Status == ConversationStatus.Paused && backend.LaunchCalls == 0 && approvals.Calls == 1,
        "Unbound accepted plan must not erase ranked discovery; corrected schema can execute before the no-progress limit, and denied launch still cannot execute.");
}

static async Task M9ReadOnlyAliasRoutingRepair()
{
    using var setup = new TestContext();
    var read = new GenericRecoveryTool("fixture.read", false, _ => ToolReceipt.Ok("Observed fixture", new { value = 7 }));
    var fillers = Enumerable.Range(0, 8).Select(i => new GenericRecoveryTool("other.read" + i, false,
        _ => ToolReceipt.Ok("Unused", new { value = 0 }))).ToArray();
    var registry = new ToolRegistry(fillers.Cast<IAgentTool>().Append(read), fillers.Select(t => t.Descriptor.Name));
    var model = new FakeModel(new ModelReply[] {
        ModelCall("fixture_read", """{"target":"sample"}"""),
        ModelCall("fixture_read", """{"target":"sample"}""")
    });
    var approvals = new FakeApprovals(false); Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
        enableCapabilityRouting: true, enableAdaptivePlanning: true).RunAsync(id, "Inspect the fixture", approvals);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && model.ToolSnapshots.Count == 2 &&
        !model.ToolSnapshots[0].Any(d => d.Name == "fixture.read") && model.ToolSnapshots[1].Any(d => d.Name == "fixture.read") &&
        approvals.Calls == 1 && approvals.Requests.Single().ToolName == "fixture.read" && read.Calls == 0 &&
        state.Operations.Count == 1 && state.Operations.Single().ToolName == "fixture.read" &&
        state.Messages.Any(m => m.ToolCalls?.Any(c => c.Name == "fixture_read") == true),
        "Unique read-only alias stages inactive schema first; canonical approval is mandatory and original provider transcript remains intact.");

    var active = new ToolRegistry(fillers.Cast<IAgentTool>().Append(read), new[] { "fixture.read" }.Concat(fillers.Select(t => t.Descriptor.Name)));
    var approvedModel = new FakeModel(new ModelReply[] {
        ModelCall("fixture_read", """{"target":"sample"}"""), new ModelReply("Observed the fixture.", [])
    });
    var granted = new FakeApprovals(true); Guid approvedId = Guid.NewGuid();
    var approvedResult = await new NiraEngine(approvedModel, active, setup.Store, new WorkspaceAuthority(setup.Workspace),
        enableCapabilityRouting: true, enableAdaptivePlanning: true).RunAsync(approvedId, "Inspect the fixture", granted);
    var approvedState = (await setup.Store.LoadAsync(approvedId, default))!;
    Assert(approvedResult.Status == ConversationStatus.Completed && approvedModel.ToolSnapshots.Count == 2 && read.Calls == 1 &&
        granted.Calls == 1 && approvedState.Operations.Single().ToolName == "fixture.read" &&
        approvedState.Operations.Single().Status == OperationStatus.Succeeded,
        "An active unique read-only alias executes once through ordinary authorization and canonical evidence recording.");
}

static async Task M9InitialRelatedResearchRepair()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    var model = new FakeModel([new ModelReply("Fixture answer.", [])]);
    await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
        enableCapabilityRouting: true, enableAdaptivePlanning: true, enableLocalSchemaRanking: true)
        .RunAsync(Guid.NewGuid(), "Research how lithium-ion batteries work and explain their advantages and limitations using reliable sources.", new FakeApprovals(false));
    Assert(model.ToolSnapshots.Count == 1 && model.ToolSnapshots[0].Any(d => d.Name == "web.search") && model.ToolSnapshots[0].Count <= 14,
        "Initial ranked research schemas must leave room for existing catalog relationships before unrelated defaults.");
}

static async Task M9ImageCandidateRouting()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("""{"query":{"pages":[{"title":"File:Example.jpg","imageinfo":[{"mime":"image/jpeg","url":"https://upload.wikimedia.org/example.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Example.jpg"}]}]}}""");
    using var http = new HttpClient(handler);
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    var model = new FakeModel(messages => {
        var search = messages.LastOrDefault(m => m.Role == "tool" && m.Content?.Contains("CandidateId", StringComparison.Ordinal) == true);
        if (search is null) return new ModelReply(null, [Call("discover", "image.search", new { query = "example photograph", provider = "commons" })]);
        using var doc = JsonDocument.Parse(search.Content!);
        string id = doc.RootElement.GetProperty("Data").GetProperty("candidates")[0].GetProperty("CandidateId").GetString()!;
        return new ModelReply(null, [Call("retrieve", "image.retrieve", new { candidateIds = new[] { id }, urls = Array.Empty<string>() })]);
    });
    var approvals = new ScriptedApprovals([true, false]);
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace),
        enableCapabilityRouting: true, enableLocalSchemaRanking: true).RunAsync(Guid.NewGuid(), "Show me real photographs of an invention with their sources.", approvals);
    Assert(result.Status == ConversationStatus.Paused && model.ToolSnapshots.Count == 2 &&
        model.ToolSnapshots[0].Any(t => t.Name == "image.search") && model.ToolSnapshots[1].Any(t => t.Name == "image.retrieve") &&
        handler.Calls == 1 && approvals.Calls == 2 && !Directory.Exists(Path.Combine(setup.Workspace, ImagePreviewTool.CacheFolder)),
        "Image discovery and retrieval should be routed without selection calls; denial must prevent media fetching.");
}

static async Task M9RelatedResearchRouting()
{
    using var setup = new TestContext();
    using var searchHandler = new FakeApiHandler("""{"results":[{"title":"Reference","url":"https://example.org/article","content":"Observed excerpt"}]}""");
    using var imagesHandler = new FakeApiHandler("""{"query":{"pages":[{"title":"File:Reference.jpg","imageinfo":[{"mime":"image/jpeg","url":"https://upload.wikimedia.org/reference.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Reference.jpg"}]}]}}""");
    using var searchHttp = new HttpClient(searchHandler); using var imagesHttp = new HttpClient(imagesHandler);
    var catalog = NiraStandardTools.Create(setup.Workspace, setup.Store, searchHttp);
    var tools = catalog.Descriptors.Select(d => {
        if (d.Name == "web.search") return (IAgentTool)new TavilySearchTool(searchHttp, () => "fixture-key");
        if (d.Name == "image.search") return (IAgentTool)new ImageDiscoveryTool(imagesHttp);
        Assert(catalog.TryGet(d.Name, out var tool), "Registered catalog entry exists.");
        return tool!;
    }).ToArray();
    var relations = catalog.Descriptors.ToDictionary(d => d.Name, d => catalog.RelatedCapabilities(d.Name).ToArray(), StringComparer.Ordinal);
    var registry = new ToolRegistry(tools, catalog.DefaultDescriptors.Select(d => d.Name), relations);
    Assert(registry.RelatedCapabilities("web.search").Contains("browser.inspect_public") &&
        registry.RelatedCapabilities("image.search").Contains("image.retrieve") &&
        registry.RelatedCapabilities("desktop.control_sequence").Count == 0, "Catalog relationships are explicit and leave desktop actions unchanged.");
    var model = new FakeModel([
        ModelCall("web.search", """{"query":"public reference"}"""),
        ModelCall("image.search", """{"query":"reference photograph","provider":"commons"}"""),
        ModelCall("image.retrieve", """{"urls":["https://upload.wikimedia.org/reference.jpg"]}""")]);
    var approvals = new ScriptedApprovals([true, true, false]);
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace), enableCapabilityRouting: true, enableAdaptivePlanning: true)
        .RunAsync(Guid.NewGuid(), "Research and show a reference photograph", approvals);
    Assert(result.Status == ConversationStatus.Paused && model.ToolSnapshots.Count == 3 &&
        model.ToolSnapshots[1].Any(d => d.Name == "browser.inspect_public") &&
        model.ToolSnapshots[1].Any(d => d.Name == "image.search") && model.ToolSnapshots[1].Any(d => d.Name == "image.retrieve") &&
        model.ToolSnapshots[2].Any(d => d.Name == "image.retrieve") && model.ToolSnapshots.All(ts => ts.Count <= 14) &&
        searchHandler.Calls == 1 && imagesHandler.Calls == 1 && approvals.Calls == 3 &&
        !Directory.Exists(Path.Combine(setup.Workspace, ImagePreviewTool.CacheFolder)),
        "Research must expose actual related schemas without a selection round or an unauthorized download.");
}

static async Task M9ImplicitNumericSequenceAndCapture()
{
    foreach (string expected in new[] { "Total: 3192 USD", "3192" })
    {
        using var setup = new TestContext(); using var http = new HttpClient();
        var backend = new SequenceDesktopBackend { InitialDisplay = "Total: 0 USD", FinalDisplay = "Total: 3,192 USD" };
        var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, desktopBackend: backend);
        string proposal = JsonSerializer.Serialize(new { windowId = "win-seq-0001", steps = new[] { "First", "Second" },
            observeAutomationId = "display", expectedName = expected });
        var model = new FakeModel([ModelCall("desktop.window_inspect", """{"windowId":"win-seq-0001"}"""),
            ModelCall("desktop.control_sequence", proposal),
            ModelCall("desktop.window_capture", """{"windowId":"win-seq-0001","title":"Test window"}"""),
            new ModelReply("Verified and captured.", [])]);
        var approvals = new FakeApprovals(true); Guid id = Guid.NewGuid();
        var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true),
            enableAdaptivePlanning: true).RunAsync(id, "Perform the observed actions, verify the outcome and capture the window.", approvals);
        var state = (await setup.Store.LoadAsync(id, default))!;
        var operation = state.Operations.Single(o => o.ToolName == "desktop.control_sequence");
        using var args = JsonDocument.Parse(operation.ArgumentsJson);
        using var receipt = JsonDocument.Parse(operation.ResultJson!);
        Assert(result.Status == ConversationStatus.Completed && model.PromptSnapshots.Count == 4 && approvals.Calls == 2 &&
            backend.Invocations.Count == 2 && backend.CaptureCalls == 1 && RecordedFileArtifacts.From(state).Count == 1,
            "Legacy implicit numeric result must complete through capture without a repair round or extra review.");
        Assert(args.RootElement.GetProperty("expectedName").GetString() == "Total: 3192 USD" &&
            args.RootElement.GetProperty("comparisonMode").GetString() == "numeric_grouping" &&
            approvals.Requests.Any(r => r.ArgumentsJson == operation.ArgumentsJson) &&
            receipt.RootElement.GetProperty("Data").GetProperty("Verified").GetBoolean(),
            "Same observed label and comparison contract must be canonicalized before approval and recorded.");
    }
}

static async Task M9ImplicitNumericSafety()
{
    const string initial = "Total: 0 USD";
    var legacy = JsonToolSchema.Schema("""{"expectedName":"Total: 3192 USD"}""");
    Assert(DesktopDisplayExpectation.BindImplicitName(legacy, initial, out var bound) is null,
        "Legacy omission must bind successfully.");
    Assert(DesktopDisplayExpectation.TryRead(bound, out string expected, out string mode, out _) && mode == "numeric_grouping",
        "Legacy omission must resolve to a bound grouping comparison.");
    foreach (string wrong in new[] { "Total: 3,193 USD", "Total: -3,192 USD", "Total: 3,19,2 USD", "Error: 3,192 USD", "Total: 3,192 EUR", "Total: 3,192 USD (2)", "Total: −3,192 USD", "Total: 3,192.0 USD" })
        Assert(!DesktopDisplayExpectation.Matches(wrong, expected, mode, initial), "Implicit grouping must reject changed numeric digits, signs, labels or precision: " + wrong);
    foreach (string literal in new[] { "Total: 3192 USD", "Record X3192", "Order 000123" })
    {
        var text = JsonSerializer.SerializeToElement(new { expectedText = literal });
        Assert(DesktopDisplayExpectation.BindImplicitName(text, initial, out var unchanged) is null && unchanged.Equals(text) &&
            DesktopDisplayExpectation.TryRead(unchanged, out _, out string literalMode, out _) && literalMode == "exact",
            "An explicitly literal expectation never changes comparison mode.");
    }
    Assert(!DesktopDisplayExpectation.Matches("Total: 3,192 USD", "Total: 3192 USD", "exact", initial), "Explicit literal comparison remains exact.");
    foreach (string json in new[] { """{"expectedText":"x","expectedNumber":1}""", """{"expectedText":"x","expectedName":"x"}""", """{"expectedText":"x","comparisonMode":"numeric_grouping"}""" })
        Assert(!DesktopDisplayExpectation.TryRead(JsonToolSchema.Schema(json), out _, out _, out _), "Reject conflicting literal expectation fields.");
    var backend = new SequenceDesktopBackend { InitialDisplay = initial, FinalDisplay = "Total: 3,192 USD" };
    var tool = new DesktopControlSequenceTool(backend);
    string args = OrderedControlArgs().Replace("Final result", "Total: 3192 USD", StringComparison.Ordinal);
    var unbound = await tool.ExecuteAsync(JsonToolSchema.Schema(args), default);
    Assert(!unbound.Success && backend.Invocations.Count == 0, "Unbound implicit legacy numeric expectations cannot be dispatched directly by the adapter.");
    string literalArgs = args.Replace("expectedName", "expectedText", StringComparison.Ordinal);
    var mismatch = await tool.ExecuteAsync(JsonToolSchema.Schema(literalArgs), default);
    Assert(backend.Invocations.Count == 2 && mismatch.Recovery == ToolRecovery.UnverifiedEffect &&
        !mismatch.Data.GetProperty("Verified").GetBoolean(), "Literal mismatch remains a safe stop; no replay or false success.");
}

static async Task M9TypedNumericSequenceAndCapture()
{
    using var setup = new TestContext(); using var http = new HttpClient();
    var backend = new SequenceDesktopBackend { InitialDisplay = "Balance: 0 USD", FinalDisplay = "Balance: 12,345.60 USD" };
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, desktopBackend: backend);
    const string proposal = """{"windowId":"win-seq-0001","steps":["First","Second"],"observeAutomationId":"display","expectedNumber":12345.6}""";
    var model = new FakeModel([ModelCall("desktop.window_inspect", """{"windowId":"win-seq-0001"}"""),
        ModelCall("desktop.control_sequence", proposal),
        ModelCall("desktop.window_capture", """{"windowId":"win-seq-0001","title":"Test window"}"""),
        new ModelReply("Verified and captured.", [])]);
    var approvals = new FakeApprovals(true); Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true),
        enableAdaptivePlanning: true).RunAsync(id, "Perform ordered actions, verify the numeric outcome, and capture the window.", approvals);
    var state = (await setup.Store.LoadAsync(id, default))!;
    var operation = state.Operations.Single(o => o.ToolName == "desktop.control_sequence");
    using var args = JsonDocument.Parse(operation.ArgumentsJson);
    using var receipt = JsonDocument.Parse(operation.ResultJson!);
    Assert(result.Status == ConversationStatus.Completed && model.PromptSnapshots.Count == 4 &&
        backend.Invocations.SequenceEqual(new[] { "ctrl-first-01", "ctrl-second-02" }) && backend.CaptureCalls == 1 &&
        RecordedFileArtifacts.From(state).Count == 1 && approvals.Calls == 2,
        "Typed numeric shorthand must complete and capture without correction or review inference.");
    Assert(args.RootElement.GetProperty("steps")[0].GetProperty("controlId").GetString() == "ctrl-first-01" &&
        approvals.Requests.Any(r => r.ArgumentsJson == operation.ArgumentsJson) &&
        receipt.RootElement.GetProperty("Data").GetProperty("Verified").GetBoolean(), "Canonical controls must be approved and observed value verified.");
}

static async Task M9LegacyNumericPreflight()
{
    foreach (string expected in new[] { "12345", "Balance: 12345 USD" })
    {
        using var setup = new TestContext(); var backend = new SequenceDesktopBackend { InitialDisplay = "Balance: 0 USD", FinalDisplay = "Balance: 12,345 USD" };
        string proposal = JsonSerializer.Serialize(new { windowId = "win-seq-0001", steps = new[] { "First", "Second" },
            observeAutomationId = "display", expectedName = expected, comparisonMode = "exact" });
        var model = new FakeModel([ModelCall("desktop.window_inspect", """{"windowId":"win-seq-0001"}"""),
            ModelCall("desktop.control_sequence", proposal), new ModelReply("Invalid expectation.", [])]);
        var approvals = new FakeApprovals(true); Guid id = Guid.NewGuid();
        await setup.Engine(model, backend).RunAsync(id, "Perform ordered actions", approvals);
        var state = (await setup.Store.LoadAsync(id, default))!;
        Assert(backend.Invocations.Count == 0 && approvals.Calls == 1 && state.Operations.Count == 1 &&
            state.Messages.Any(m => m.Content?.Contains("legacy exact mode", StringComparison.Ordinal) == true),
            "An ambiguous legacy numeric expectation must be rejected before action approval or dispatch.");
    }
}

static async Task M9TypedNumericSafety()
{
    const string initial = "Balance: 0 USD";
    Assert(DesktopDisplayExpectation.Matches("Balance: 12,345.600 USD", "12345.6", "numeric_value", initial), "Exact decimal equivalence.");
    foreach (string wrong in new[] { "Balance: -12,345.6 USD", "Balance: 12,345.61 USD", "Balance: 12,34.56 USD", "Balance: 12,345.6 EUR", "Error: 12,345.6 USD", "12345.6", "Balance: 12,345.6 USD (2)" })
        Assert(!DesktopDisplayExpectation.Matches(wrong, "12345.6", "numeric_value", initial), "Reject wrong value, label, ambiguous or malformed output: " + wrong);
    Assert(!DesktopDisplayExpectation.Matches("Balance: 12,345.6 USD", "Balance: 12345.6 USD", "exact", initial), "Literal exact mode never becomes numeric.");
    var backend = new SequenceDesktopBackend { InitialDisplay = initial };
    var tool = new DesktopControlSequenceTool(backend);
    foreach (string suffix in new[] { "", ",\"expectedNumber\":1,\"expectedName\":\"one\"", ",\"expectedNumber\":\"wrong\"", ",\"expectedNumber\":1,\"comparisonMode\":\"exact\"" })
    {
        string args = OrderedControlArgs().Replace(",\"expectedName\":\"Final result\"", suffix, StringComparison.Ordinal);
        var result = await tool.ExecuteAsync(JsonToolSchema.Schema(args), default);
        Assert(!result.Success && backend.Invocations.Count == 0, "Invalid or conflicting expectations never dispatch.");
    }
    Assert(!JsonToolSchema.Validate(tool.Descriptor.Parameters, JsonToolSchema.Schema("""{"windowId":"win-seq-0001","steps":["First",42],"observeAutomationId":"display","expectedNumber":1}"""), out _), "Union schema rejects unknown step shapes.");
    using var setup = new TestContext(); var ambiguous = new SequenceDesktopBackend { DuplicateFirstName = true };
    var model = new FakeModel([ModelCall("desktop.window_inspect", """{"windowId":"win-seq-0001"}"""),
        ModelCall("desktop.control_sequence", """{"windowId":"win-seq-0001","steps":["First","Second"],"observeAutomationId":"display","expectedName":"Final result"}"""),
        new ModelReply("Ambiguous target.", [])]);
    var approvals = new FakeApprovals(true);
    await setup.Engine(model, ambiguous).RunAsync(Guid.NewGuid(), "Perform observed ordered actions", approvals);
    Assert(ambiguous.Invocations.Count == 0 && approvals.Calls == 1, "String shorthand must reject duplicate names before approval.");
}

static async Task M9EfficiencyNameBinding()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend();
    string proposal = """{"windowId":"win-seq-0001","steps":[{"name":"First"},{"name":"Second"}],"observeAutomationId":"display","expectedName":"Final result"}""";
    var model = new FakeModel([ModelCall("desktop.window_inspect", """{"windowId":"win-seq-0001"}"""),
        ModelCall("desktop.control_sequence", proposal), new ModelReply("Verified.", [])]);
    var approvals = new FakeApprovals(true); Guid id = Guid.NewGuid();
    var result = await setup.Engine(model, backend).RunAsync(id, "Perform the observed ordered actions", approvals);
    var saved = (await setup.Store.LoadAsync(id, default))!;
    var op = saved.Operations.Single(o => o.ToolName == "desktop.control_sequence");
    using var args = JsonDocument.Parse(op.ArgumentsJson);
    Assert(result.Status == ConversationStatus.Completed && backend.Invocations.SequenceEqual(new[] { "ctrl-first-01", "ctrl-second-02" }) &&
        approvals.Calls == 2 && model.PromptSnapshots.Count == 3, "Name binding uses the same validated adapter and does not add inference.");
    Assert(args.RootElement.GetProperty("steps")[0].GetProperty("controlId").GetString() == "ctrl-first-01" &&
        approvals.Requests.Last().ArgumentsJson == op.ArgumentsJson, "Exact resolved IDs must be recorded and shown in approval before dispatch.");
}

static async Task M9EfficiencyAmbiguousSelector()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend { DuplicateFirstName = true };
    var model = new FakeModel([ModelCall("desktop.window_inspect", """{"windowId":"win-seq-0001"}"""),
        ModelCall("desktop.control_invoke", """{"windowId":"win-seq-0001","controlId":"name://First"}"""),
        new ModelReply("No unique target.", [])]);
    Guid id = Guid.NewGuid(); var approval = new FakeApprovals(true);
    await setup.Engine(model, backend).RunAsync(id, "Invoke the first observed control", approval);
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(approval.Calls == 1 && backend.Invocations.Count == 0 && saved.Operations.Count == 1 &&
        saved.Messages.Any(m => m.Content?.Contains("DESKTOP_SELECTOR_NOT_OBSERVED", StringComparison.Ordinal) == true),
        "Ambiguous name must never become an approval or dispatch.");
}

static async Task M9EfficiencyEditorOccupancy()
{
    var backend = new SequenceDesktopBackend { NonemptyFirst = true };
    var tool = new DesktopInspectTool(backend);
    using var plain = JsonDocument.Parse("""{"windowId":"win-seq-0001"}""");
    using var requested = JsonDocument.Parse("""{"windowId":"win-seq-0001","checkEditorEmpty":true}""");
    var before = await tool.ExecuteAsync(plain.RootElement, default);
    var after = await tool.ExecuteAsync(requested.RootElement, default);
    Assert(!before.Data.TryGetProperty("EditorEmptyByControlId", out _) &&
        after.Data.GetProperty("EditorEmptyByControlId").GetProperty("ctrl-editor-01").ValueKind == JsonValueKind.False &&
        backend.OccupancyChecks == 1 && backend.Typed is null && backend.Invocations.Count == 0,
        "Only explicitly requested occupancy metadata is returned, with no text or mutation.");
}

static Task M9EfficiencyFastInput()
{
    string value = ""; int waits = 0; int sends = 0;
    var result = DesktopVerifiedTextInput.Write("A😀B", () => true, () => value,
        unit => { value += unit; sends++; return true; }, () => waits++, default);
    Assert(result.Verified == true && value == "A😀B" && sends == 3 && waits == 2,
        "Immediate readback avoids per-character delay; final independent observations still wait.");
    return Task.CompletedTask;
}

static async Task M9EfficiencyRepeatedRead()
{
    using var setup = new TestContext();
    var observation = new GenericRecoveryTool("sample.observe", false, _ => ToolReceipt.Ok("Unchanged", new { value = 1 }));
    int rounds = 0;
    var model = new FakeModel(_ => new ModelReply(null, [new ModelToolCall("observe-" + ++rounds, "sample.observe", """{"target":"same"}""")]));
    var engine = new NiraEngine(model, new ToolRegistry([observation]), setup.Store, new WorkspaceAuthority(setup.Workspace, true));
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Find the changed state", new FakeApprovals(true));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && saved.RetryBlockCode == "NO_PROGRESS" && rounds == 7 &&
        observation.Calls == 7 && saved.Operations.Count == 7, "Repeated identical reads cannot reset progress indefinitely; retain every real receipt.");
}

static async Task M9EfficiencyWire()
{
    using var handler = new FakeApiHandler("""{"choices":[{"message":{"content":"Hello."}}]}""");
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    const string content = """{"name":"<editor>","text":"é"}""";
    await model.CompleteAsync([new ChatEntry("user", content)], [], default);
    using var body = JsonDocument.Parse(handler.RequestBody!);
    Assert(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() == content &&
        handler.RequestBody!.Length < JsonSerializer.Serialize(new { model = "mock", messages = new[] { new { role = "user", content } }, stream = false }).Length,
        "Transport removes unnecessary escaping without altering decoded content or tool protocol.");
}

static async Task M9StabilityCompact()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend { AdditionalControls = 56 };
    var model = new FakeModel(new[] {
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_sequence", OrderedControlArgs()), new ModelReply("Verified result.", []) });
    Guid id = Guid.NewGuid(); var approval = new FakeApprovals(true);
    var result = await setup.Engine(model, backend).RunAsync(id, "Perform and verify the ordered actions", approval);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && approval.Calls == 2 && backend.Invocations.Count == 2,
        "Compaction cannot change approvals or verified action execution.");
    using var original = JsonDocument.Parse(state.Messages.First(m => m.Role == "tool").Content!);
    using var compact = JsonDocument.Parse(model.PromptSnapshots[1].First(m => m.Role == "tool").Content!);
    var a = original.RootElement.GetProperty("Data"); var b = compact.RootElement.GetProperty("Data");
    var columns = b.GetProperty("ControlColumns").EnumerateArray().Select(x => x.GetString()!).ToArray();
    var rows = b.GetProperty("ControlRows"); var controls = a.GetProperty("Controls");
    Assert(rows.GetArrayLength() == controls.GetArrayLength() && rows.GetArrayLength() == 60 && columns.Length == 9,
        "All 60 observed controls and 9 original fields must remain available.");
    for (int i = 0; i < controls.GetArrayLength(); i++)
        for (int j = 0; j < columns.Length; j++)
            Assert(controls[i].GetProperty(columns[j]).GetRawText() == rows[i][j].GetRawText(), "Exact control field must survive projection.");
    Assert(a.GetProperty("Window").GetRawText() == b.GetProperty("Window").GetRawText() &&
        original.RootElement.GetProperty("receiptId").GetString() == compact.RootElement.GetProperty("receiptId").GetString() &&
        compact.RootElement.GetRawText().Length < original.RootElement.GetRawText().Length * 0.8,
        "Preserve target, receipt identity and reduce the observation payload.");
    using var receipt = JsonDocument.Parse(state.Operations.First().ResultJson!);
    Assert(receipt.RootElement.GetProperty("Data").GetProperty("Controls").GetArrayLength() == 60,
        "Original durable operation evidence cannot be replaced by the model-facing projection.");
}

static async Task M9StabilitySequenceFeedback()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend();
    string invalid = OrderedControlArgs().Replace("\"name\":\"First\"", "\"name\":\"Wrong label\"", StringComparison.Ordinal)
        .Replace("\"observeAutomationId\":\"display\"", "\"observeAutomationId\":\"ctrl-display-01\"", StringComparison.Ordinal);
    var model = new FakeModel(new[] {
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_sequence", invalid), ModelCall("desktop.control_sequence", OrderedControlArgs()),
        new ModelReply("Verified result.", []) });
    var approval = new FakeApprovals(true); Guid id = Guid.NewGuid();
    var result = await setup.Engine(model, backend).RunAsync(id, "Perform the two actions", approval);
    var state = (await setup.Store.LoadAsync(id, default))!;
    var error = state.Messages.Where(m => m.Role == "tool").Select(m => m.Content ?? "")
        .Single(x => x.Contains("DESKTOP_SEQUENCE_NOT_OBSERVED", StringComparison.Ordinal));
    Assert(error.Contains("observeAutomationId is not ControlId", StringComparison.Ordinal) &&
        error.Contains("steps[0].name", StringComparison.Ordinal) && error.Contains("First", StringComparison.Ordinal) &&
        error.Contains("display", StringComparison.Ordinal), "One rejection must identify all invalid fields from genuine evidence.");
    Assert(result.Status == ConversationStatus.Completed && approval.Calls == 2 && backend.Invocations.Count == 2 &&
        state.Operations.Count == 2 && model.PromptSnapshots.Count == 4,
        "The malformed proposal must never dispatch or request approval; a corrected one uses the existing observation.");
}

static async Task M9StabilityMetrics()
{
    using var setup = new TestContext();
    using var handler = new FakeApiHandler("""{"choices":[{"message":{"content":"Hello."}}],"usage":{"prompt_tokens":120,"completion_tokens":8}}""");
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var progress = new M9MetricsProgress();
    var result = await setup.Engine(model, adaptive: true).RunAsync(Guid.NewGuid(), "Hello", new FakeApprovals(false), progress);
    Assert(result.Status == ConversationStatus.Completed && handler.Calls == 1, "Telemetry cannot add an inference.");
    string usage = progress.Events.Single(e => e.Kind == "model-usage").Summary;
    Assert(usage.Contains("HTTP requests: 1;", StringComparison.Ordinal) &&
        usage.Contains("provider usage available for 1/1", StringComparison.Ordinal) &&
        usage.Contains("reported input tokens: 120; reported output tokens: 8", StringComparison.Ordinal) &&
        usage.Contains("logical model calls: 1; model elapsed ms (sum, including retries): ", StringComparison.Ordinal) &&
        usage.Contains("request UTF-8 bytes: " + System.Text.Encoding.UTF8.GetByteCount(handler.RequestBody!), StringComparison.Ordinal),
        "Scope counts actual serialized HTTP attempts and provider-reported tokens only.");
    var outside = new M9MetricsProgress();
    using (model.BeginRequestScope(outside))
    {
        await model.CompleteAsync([new ChatEntry("user", "One")], [], default);
        await model.CompleteAsync([new ChatEntry("user", "Two")], [], default);
    }
    Assert(outside.Events.Single().Summary.Contains("HTTP requests: 2;", StringComparison.Ordinal) &&
        outside.Events.Single().Summary.Contains("reported input tokens: 240; reported output tokens: 16", StringComparison.Ordinal),
        "A separate scope must count both calls without inheriting the previous task's usage.");
}

static async Task M9StabilityStreamUsage()
{
    using var handler = new M4b3StreamHandler(M4b3Sse(
        """{"choices":[{"delta":{"content":"Visible"},"finish_reason":null}]}""",
        """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
        """{"choices":[],"usage":{"prompt_tokens":44,"completion_tokens":6}}""", "[DONE]"));
    using var http = new HttpClient(handler);
    var model = new OpenAiCompatibleModel(http, new Uri("https://example.invalid/v1/"), "mock", () => null);
    var preview = new List<string?>(); var progress = new M9MetricsProgress();
    using (model.BeginRequestScope(progress))
    {
        var reply = await model.CompleteStreamingAsync([new ChatEntry("user", "Hello")], [], preview.Add, default);
        Assert(reply.Content == "Visible" && reply.DeliveryMode == "event-stream" &&
            reply.Usage == new ModelTokenUsage(44, 6) && preview.SequenceEqual(new string?[] { null, "Visible" }),
            "A usage-only packet must never become text or change stream completion.");
    }
    Assert(progress.Events.Single().Summary.Contains("reported input tokens: 44; reported output tokens: 6", StringComparison.Ordinal),
        "Stream usage is counted when present.");
}

static async Task OrderedControlsVerified()
{
    using var setup = new TestContext();
    var backend = new SequenceDesktopBackend();
    var model = new FakeModel(new[] {
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_sequence", OrderedControlArgs()), new ModelReply("Observed final result.", [])
    });
    Guid id = Guid.NewGuid(); var approval = new FakeApprovals(true);
    var result = await setup.Engine(model, backend).RunAsync(id, "Perform the two ordered actions and verify", approval);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && approval.Calls == 2 &&
        backend.Invocations.SequenceEqual(new[] { "ctrl-first-01", "ctrl-second-02" }) &&
        state.Operations.Last().ResultJson!.Contains("\"Verified\":true", StringComparison.Ordinal),
        "One exact sequence approval must preserve order and independently observe the final result.");
}
static async Task OrderedControlsDenied()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend();
    var model = new FakeModel(new[] { ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_sequence", OrderedControlArgs()) });
    var result = await setup.Engine(model, backend).RunAsync(Guid.NewGuid(), "Perform two actions", new ScriptedApprovals(new[] { true, false }));
    Assert(result.Status == ConversationStatus.Paused && backend.Invocations.Count == 0, "Denied sequence cannot invoke any control.");
}
static async Task OrderedControlsUnobserved()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend(); var approval = new FakeApprovals(true);
    var model = new FakeModel(new[] { ModelCall("desktop.control_sequence", OrderedControlArgs()), new ModelReply("No observation.", []) });
    await setup.Engine(model, backend).RunAsync(Guid.NewGuid(), "Perform two actions", approval);
    Assert(approval.Calls == 0 && backend.Invocations.Count == 0, "Invented control IDs cannot be approved or executed.");
}
static async Task OrderedControlsStale()
{
    var backend = new SequenceDesktopBackend { StaleAfterFirst = true };
    using var args = JsonDocument.Parse(OrderedControlArgs());
    var receipt = await new DesktopControlSequenceTool(backend).ExecuteAsync(args.RootElement, default);
    Assert(receipt.ChangedState && !receipt.Data.GetProperty("Verified").GetBoolean() && backend.Invocations.Count == 1,
        "Stale target after first action must stop without replaying or dispatching later actions.");
}
static async Task OrderedControlsMismatch()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend { WrongDisplay = true };
    var model = new FakeModel(new[] { ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_sequence", OrderedControlArgs()) });
    var engine = setup.Engine(model, backend); Guid id = Guid.NewGuid(); var approval = new FakeApprovals(true);
    var result = await engine.RunAsync(id, "Perform two actions and verify", approval);
    var retry = await engine.RunAsync(id, null, approval);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && retry.Status == ConversationStatus.Paused &&
        state.RetryBlockCode == ToolRecoveryPolicy.UnverifiedEffectCode && backend.Invocations.Count == 2,
        "A mismatch must block automatic replay and preserve the actual display observation.");
}
static async Task OrderedControlsNoExtraInference()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend();
    var model = new FakeModel(new[] {
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_sequence", OrderedControlArgs()),
        new ModelReply("Observed final result.", []) });
    var approval = new FakeApprovals(true);
    var result = await setup.Engine(model, backend).RunAsync(Guid.NewGuid(), "Perform the two ordered actions and verify", approval);
    Assert(result.Status == ConversationStatus.Completed && approval.Calls == 2 && backend.Invocations.Count == 2 &&
        model.PromptSnapshots.Count == 3, "An observed, user-approved sequence must execute and deliver without an extra model review.");
}
static async Task OrderedControlsBareNumericCompletion()
{
    using var setup = new TestContext();
    var backend = new SequenceDesktopBackend { InitialDisplay = "Display is 0", FinalDisplay = "Display is 1,472" };
    string args = OrderedControlArgs().Replace("Final result", "1472", StringComparison.Ordinal)
        .Replace("\"expectedName\":", "\"comparisonMode\":\"numeric_grouping\",\"expectedName\":", StringComparison.Ordinal);
    var model = new FakeModel(new[] {
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_sequence", args), new ModelReply("Verified result.", []) });
    Guid id = Guid.NewGuid(); var result = await setup.Engine(model, backend).RunAsync(id, "Perform the observed actions and verify the numeric outcome", new FakeApprovals(true));
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && model.PromptSnapshots.Count == 3 && backend.Invocations.Count == 2 &&
        state.Operations.Last().ResultJson!.Contains("\"Verified\":true", StringComparison.Ordinal),
        "A bare numeric expectation must verify a labelled grouped value and permit the ordinary next step.");
    foreach (string invalid in new[] { "Display is 1,473", "Display is -1,472", "Error 1,472", "Display is 1,472 (2)", "Display is 14,72" })
        Assert(!DesktopControlSequenceTool.DisplayMatches(invalid, "1472", "numeric_grouping", backend.InitialDisplay), "Reject a wrong value or changed label: " + invalid);
    Assert(!DesktopControlSequenceTool.DisplayMatches(backend.FinalDisplay, "1472", "numeric_grouping"), "Bare expected values cannot match arbitrary labels without a pre-action observation.");
    Assert(!DesktopControlSequenceTool.DisplayMatches(backend.FinalDisplay, "1472", "exact", backend.InitialDisplay), "Exact mode cannot ignore labels.");
}
static async Task OrderedControlsThenCapture()
{
    using var setup = new TestContext(); using var http = new HttpClient();
    var backend = new SequenceDesktopBackend { InitialDisplay = "Display is 0", FinalDisplay = "Display is 1,472" };
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http, desktopBackend: backend);
    string args = OrderedControlArgs().Replace("Final result", "1472", StringComparison.Ordinal)
        .Replace("\"expectedName\":", "\"comparisonMode\":\"numeric_grouping\",\"expectedName\":", StringComparison.Ordinal);
    var model = new FakeModel(new[] {
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_sequence", args),
        ModelCall("desktop.window_capture", "{\"windowId\":\"win-seq-0001\",\"title\":\"Test window\"}"),
        new ModelReply("Verified numeric outcome and captured the window.", []) });
    var approval = new FakeApprovals(true); Guid id = Guid.NewGuid();
    var result = await new NiraEngine(model, registry, setup.Store, new WorkspaceAuthority(setup.Workspace, developmentMode: true),
        enableAdaptivePlanning: true).RunAsync(id, "Perform the two observed actions, verify the number and capture the window.", approval);
    var state = (await setup.Store.LoadAsync(id, default))!;
    var artifacts = RecordedFileArtifacts.From(state);
    Assert(result.Status == ConversationStatus.Completed && model.PromptSnapshots.Count == 4 && approval.Calls == 2 &&
        backend.Invocations.Count == 2 && backend.CaptureCalls == 1 && artifacts.Count == 1 &&
        await RecordedFileArtifacts.MatchesAsync(artifacts.Single(), setup.Workspace),
        "Verified actions must continue to the separately approved real artifact stage and finish without a separate reviewer inference.");
}
static async Task M9LocalCapabilityRanking()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var tools = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    var ranked = tools.RankInitialCapabilities("Open an installed desktop application, inspect its accessible controls and invoke the ordered buttons.");
    Assert(ranked.Count <= 6 && ranked.Contains("desktop.application_find") && ranked.Contains("desktop.window_inspect"), "Relevant registered schemas are found locally without hardcoded application names.");
    var captureRank = tools.RankInitialCapabilities("Operate the desktop controls, verify the displayed result and capture a screenshot.");
    Assert(captureRank.Contains("desktop.application_find") && captureRank.Contains("desktop.window_capture"), "Multi-step related schema retrieval covers discovery and capture.");
    Assert(tools.RankInitialCapabilities("Hello").Count == 0, "Unmatched conversation retains the existing starter catalog.");
    var model = new FakeModel(new[] { new ModelReply("No action requested in this fixture.", []) });
    await new NiraEngine(model, tools, setup.Store, new WorkspaceAuthority(setup.Workspace), enableCapabilityRouting: true,
        enableAdaptivePlanning: true, enableLocalSchemaRanking: true).RunAsync(Guid.NewGuid(),
        "Open an installed desktop application, inspect its accessible controls and invoke the ordered buttons.", new FakeApprovals(false));
    Assert(model.ToolSnapshots.Count == 1 && model.ToolSnapshots[0].Count <= 14 && model.ToolSnapshots[0].Any(d => d.Name == "desktop.application_find"),
        "Initial schema selection requires no additional inference and respects the existing bounded catalog.");
}
static async Task OrderedDisplayNumericGrouping()
{
    Assert(DesktopControlSequenceTool.DisplayMatches("Total: 12,345.67 USD", "Total: 12345.67 USD", "numeric_grouping"), "Equivalent grouping allowed.");
    Assert(!DesktopControlSequenceTool.DisplayMatches("Total: 12,345.67 USD", "Total: 12345.67 USD", "exact"), "Exact text remains exact.");
    foreach (string invalid in new[] { "Total: 12,346.67 USD", "Total: 1,23,45.67 USD", "Total: -12,345.67 USD", "Total: 12,345.67 EUR", "Total: 12,345.76 USD", "Total: 12,345.67 USD (2)" })
        Assert(!DesktopControlSequenceTool.DisplayMatches(invalid, "Total: 12345.67 USD", "numeric_grouping"), "Do not accept different or malformed display: " + invalid);
    Assert(!DesktopControlSequenceTool.DisplayMatches("Ref X1,234", "Ref X1234", "numeric_grouping"), "Do not normalize embedded identifiers.");
    var backend = new SequenceDesktopBackend { FinalDisplay = "Total: 12,345.67 USD" };
    using var args = JsonDocument.Parse(OrderedControlArgs().Replace("Final result", "Total: 12345.67 USD", StringComparison.Ordinal).Replace("\"expectedName\":", "\"comparisonMode\":\"numeric_grouping\",\"expectedName\":", StringComparison.Ordinal));
    var receipt = await new DesktopControlSequenceTool(backend).ExecuteAsync(args.RootElement, default);
    Assert(receipt.Data.GetProperty("Verified").GetBoolean() && receipt.Data.GetProperty("ObservedName").GetString() == backend.FinalDisplay && receipt.Recovery == ToolRecovery.None,
        "Formatted read-back verifies the actual live value without rewriting the receipt.");
}
static async Task M9InterruptedIsolation()
{
    using var setup = new TestContext();
    var original = new ConversationState { Status = ConversationStatus.Running, Objective = "Original work",
        Messages = [new ChatEntry("user", "Original work")],
        Operations = [new AgentOperation { ToolName = "desktop.control_invoke", Status = OperationStatus.Succeeded }] };
    await setup.Store.SaveAsync(original, default);
    var model = new FakeModel(_ => new ModelReply("Unexpected invocation", []));
    await setup.Engine(model).RunAsync(original.Id, "Different work", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(original.Id, default))!;
    Assert(model.PromptSnapshots.Count == 0 && saved.Messages.Count == 1 && saved.Operations.Count == 1,
        "New message must not execute inside an interrupted task.");
    Guid next = await setup.Store.CreateFollowupAsync(original.Id, default, startNewAfterPause: true);
    var child = (await setup.Store.LoadAsync(next, default))!;
    Assert(child.PreviousTaskId == original.Id && child.Messages.Count == 0 && child.Operations.Count == 0 &&
        child.PreviousChatContext?.Contains("Original work", StringComparison.Ordinal) != true,
        "An isolated new task cannot receive interrupted instructions or operation receipts.");
    Assert((await setup.Store.LoadAsync(original.Id, default))!.Operations.Count == 1, "Original journal preserved.");
}

static async Task M9ProviderInterruption()
{
    using var setup = new TestContext();
    var model = new FakeModel(_ => throw new AgentException("Model stream ended without a complete response; no partial tool was dispatched."));
    Guid id = Guid.NewGuid();
    var result = await setup.Engine(model, adaptive: true).RunAsync(id, "Explain a concept", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused && saved.Status == ConversationStatus.Paused &&
        saved.Operations.Count == 0 && model.PromptSnapshots.Count == 1 && saved.Messages.Count == 1,
        "Provider failure preserves one checkpoint without replay or extra inference.");
    Assert(saved.LastAnswer is not null && saved.LastAnswer.Contains("incomplete model stream", StringComparison.Ordinal),
        "Known interruption category and answer must survive reopening the saved task.");
    var next = await setup.Engine(new FakeModel(_ => new ModelReply("Recovered answer", [])), adaptive: true)
        .RunAsync(id, null, new FakeApprovals(false));
    Assert(next.Status == ConversationStatus.Completed, "Explicit retry of the same objective is available.");
}

static async Task M9ObservedMethodPreflight()
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend();
    var model = new FakeModel(new[] {
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-demo-0001\"}"),
        ModelCall("desktop.control_set_text", "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\"}"),
        ModelCall("desktop.control_type_text", "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\",\"requireEmpty\":true}"),
        new ModelReply("Verified text.", []) });
    Guid id = Guid.NewGuid(); var approval = new FakeApprovals(true);
    var outcome = await setup.Engine(model, backend).RunAsync(id, "Type Hello in a blank editor", approval);
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(outcome.Status == ConversationStatus.Completed && approval.Calls == 2 && backend.TypeTextCalls == 1 &&
        state.Operations.Count == 2 && state.Messages.Any(m => m.Role == "tool" && m.Content?.Contains("DESKTOP_CONTROL_UNSUPPORTED", StringComparison.Ordinal) == true),
        "Known unsupported method is rejected before approval or dispatch; supported method retains its own approval.");
}

static Task M9LongEditorInput()
{
    string text = new('a', DesktopVerifiedTextInput.MaximumCharacters);
    string current = "";
    var result = DesktopVerifiedTextInput.Write(text, () => true, () => current,
        unit => { current += unit; return true; }, () => { }, default);
    Assert(result.Verified == true && current == text, "Longer text uses the same exact read-back algorithm.");
    var tool = new DesktopTypeTextTool(new SequenceDesktopBackend());
    var schema = tool.Descriptor.Parameters;
    Assert(schema.GetProperty("properties").GetProperty("text").GetProperty("maxLength").GetInt32() == DesktopVerifiedTextInput.MaximumCharacters,
        "Schema and host text bound agree.");
    return Task.CompletedTask;
}

static async Task PausedTaskIsolation()
{
    using var setup = new TestContext(); var prior = new ConversationState { Status = ConversationStatus.Paused,
        RetryBlockCode = "EDITOR_NOT_EMPTY", Objective = "First objective", LastAnswer = "Original failure" };
    await setup.Store.SaveAsync(prior, default);
    Guid next = await setup.Store.CreateFollowupAsync(prior.Id, default, startNewAfterPause: true);
    var fresh = (await setup.Store.LoadAsync(next, default))!; var saved = (await setup.Store.LoadAsync(prior.Id, default))!;
    Assert(fresh.PreviousTaskId == prior.Id && fresh.Operations.Count == 0 && fresh.Objective == "" &&
        saved.Objective == "First objective" && saved.RetryBlockCode == "EDITOR_NOT_EMPTY" && saved.Status == ConversationStatus.Paused,
        "A new chat request must not rewrite a paused objective or its receipts.");
}
static async Task NonemptyEditorRecovery()
{
    using var setup = new TestContext(); var backend = new SequenceDesktopBackend { NonemptyFirst = true };
    var model = new FakeModel(new[] {
        ModelCall("desktop.control_type_text", "{\"windowId\":\"win-seq-0001\",\"controlId\":\"ctrl-editor-01\",\"text\":\"Hello.\",\"requireEmpty\":true}"),
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_invoke", "{\"windowId\":\"win-seq-0001\",\"controlId\":\"ctrl-first-01\"}"),
        ModelCall("desktop.window_inspect", "{\"windowId\":\"win-seq-0001\"}"),
        ModelCall("desktop.control_type_text", "{\"windowId\":\"win-seq-0001\",\"controlId\":\"ctrl-editor-02\",\"text\":\"Hello.\",\"requireEmpty\":true}"),
        new ModelReply("Typed Hello. in a new blank editor.", []) });
    Guid id = Guid.NewGuid(); var result = await setup.Engine(model, backend).RunAsync(id, "Type exactly Hello. in a new blank editor", new FakeApprovals(true));
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && state.Operations.First().ErrorCode == "EDITOR_NOT_EMPTY" &&
        state.Operations.Last().Status == OperationStatus.Succeeded && backend.Invocations.Count == 1 && backend.Typed == "Hello.",
        "Known nonempty editor rejection can recover through a fresh approved blank editor without overwriting the first.");
}

static async Task VerifiedInputFollowup()
{
    using var setup = new TestContext();
    var prior = new ConversationState { Status = ConversationStatus.Paused,
        RetryBlockCode = "DESKTOP_TEXT_NOT_VERIFIED", Objective = "Write a paragraph",
        LastAnswer = "Text entry not verified", Operations = [new AgentOperation {
            ToolName = "desktop.control_type_text", Status = OperationStatus.Succeeded }] };
    await setup.Store.SaveAsync(prior, default);
    Guid childId = await setup.Store.CreateFollowupAsync(prior.Id, default);
    var child = (await setup.Store.LoadAsync(childId, default))!;
    var original = (await setup.Store.LoadAsync(prior.Id, default))!;
    Assert(child.PreviousTaskId == prior.Id && child.Operations.Count == 0 && child.RetryBlockCode is null &&
        child.PreviousChatContext?.Contains("Text entry not verified", StringComparison.Ordinal) == true &&
        original.Status == ConversationStatus.Paused && original.RetryBlockCode == "DESKTOP_TEXT_NOT_VERIFIED",
        "New requests must not turn an unverified write into a completed task.");
}
static Task VerifiedInputPaced()
{
    string value = "";
    string pending = "";
    int sends = 0;
    var result = DesktopVerifiedTextInput.Write("abc", () => true, () => value, unit =>
    {
        Assert(pending.Length == 0, "Must observe the previous input before dispatching another.");
        pending = unit; sends++; return true;
    }, () => { value += pending; pending = ""; }, default);
    Assert(result.Verified == true && sends == 3 && value == "abc", "Every character must be observed before continuing.");
    return Task.CompletedTask;
}
static Task VerifiedInputCorruption()
{
    string value = ""; int sends = 0;
    var result = DesktopVerifiedTextInput.Write("abc", () => true, () => value,
        _ => { sends++; value += "s"; return true; }, () => { }, default);
    Assert(result.Dispatched && result.Verified == false && sends == 1 && result.VerificationStatus == "mismatch", "Corruption must stop subsequent dispatch.");
    return Task.CompletedTask;
}
static Task VerifiedInputFocusLoss()
{
    bool focused = true; int sends = 0;
    var result = DesktopVerifiedTextInput.Write("abc", () => focused, () => "",
        _ => { sends++; focused = false; return true; }, () => { }, default);
    Assert(result.Dispatched && result.Verified == false && sends == 1, "Lost focus must stop typing without replay.");
    return Task.CompletedTask;
}
static Task VerifiedInputTimeout()
{
    int sends = 0, waits = 0;
    var result = DesktopVerifiedTextInput.Write("abc", () => true, () => "",
        _ => { sends++; return true; }, () => waits++, default);
    Assert(result.Dispatched && result.Verified == false && sends == 1 && waits == 20, "Read-back timeout must not retype.");
    return Task.CompletedTask;
}
static Task VerifiedInputUnicode()
{
    string value = ""; int sends = 0;
    var result = DesktopVerifiedTextInput.Write("A😀B", () => true, () => value,
        unit => { value += unit; sends++; return true; }, () => { }, default);
    Assert(result.Verified == true && sends == 3 && value == "A😀B", "Surrogate pairs must be sent together.");
    return Task.CompletedTask;
}
static Task DesktopExactTextComparison()
{
    string expected = "NIRA desktop control test successful.";
    string wrong = "NIRA " + new string('.', expected.Length - 5);
    Assert(wrong.Length == expected.Length &&
        !DesktopTextVerification.Exact(wrong, expected) &&
        DesktopTextVerification.Exact(expected + "\r\n", expected) &&
        !DesktopTextVerification.Exact(expected + "?", expected),
        "Character count and SendInput success do not prove the actual text.");
    return Task.CompletedTask;
}
static async Task DesktopUnverifiedTypingPauses()
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend { UnverifiedTypeText = true };
    var model = new FakeModel(new[] {
        ModelCall("desktop.control_type_text",
            "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\",\"requireEmpty\":true}"),
        new ModelReply("I successfully typed it.", [])
    });
    Guid id = Guid.NewGuid();
    AgentRunResult result = await setup.Engine(model, backend).RunAsync(id,
        "Type Hello in an empty editor with approval", new FakeApprovals(true));
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused &&
        state.RetryBlockCode == ToolRecoveryPolicy.UnverifiedEffectCode &&
        backend.TypeTextCalls == 1 && state.Operations.Count == 1 &&
        state.Operations[0].Status == OperationStatus.Succeeded &&
        state.Operations[0].ResultJson!.Contains("\"Verified\":false", StringComparison.Ordinal) &&
        state.Operations[0].ResultJson!.Contains("\"ChangedState\":true", StringComparison.Ordinal) &&
        result.Message.Contains("could NOT verify", StringComparison.Ordinal),
        "Dispatched, unverified typing must preserve evidence but never mark the task complete.");
}
static async Task DesktopUnverifiedTypingRetryBlocked()
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend { UnverifiedTypeText = true };
    var model = new FakeModel(new[] {
        ModelCall("desktop.control_type_text",
            "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-doc-01\",\"text\":\"Hello\",\"requireEmpty\":true}")
    });
    var engine = setup.Engine(model, backend);
    Guid id = Guid.NewGuid();
    AgentRunResult first = await engine.RunAsync(id, "Type Hello", new FakeApprovals(true));
    AgentRunResult second = await engine.RunAsync(id, null, new FakeApprovals(true));
    AgentRunResult third = await engine.RunAsync(id, "resume", new FakeApprovals(true));
    AgentRunResult fourth = await engine.RunAsync(id, "Try writing it again", new FakeApprovals(true));
    Assert(fourth.Status == ConversationStatus.Paused && first.Status == ConversationStatus.Paused &&
        second.Status == ConversationStatus.Paused &&
        third.Status == ConversationStatus.Paused && backend.TypeTextCalls == 1,
        "A dispatched unverified write must not be retried by an unchanged resume.");
}
static async Task DesktopUnverifiedSetTextPauses()
{
    using var setup = new TestContext();
    var backend = new FakeDesktopBackend { UnverifiedSetText = true };
    var model = new FakeModel(new[] {
        ModelCall("desktop.control_set_text",
            "{\"windowId\":\"win-demo-0001\",\"controlId\":\"ctrl-edit-001\",\"text\":\"Hello\"}"),
        new ModelReply("It is set.", [])
    });
    Guid id = Guid.NewGuid();
    AgentRunResult result = await setup.Engine(model, backend).RunAsync(id,
        "Set edit field to Hello", new FakeApprovals(true));
    ConversationState state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Paused &&
        state.RetryBlockCode == ToolRecoveryPolicy.UnverifiedEffectCode &&
        backend.SetTextCalls == 1 && state.Operations.Count == 1 &&
        state.Operations[0].Status == OperationStatus.Succeeded,
        "Unverified ValuePattern writes must not become completed tasks.");
}
static Task DesktopWin32InputLayout()
{
    Type? input = typeof(WindowsDesktopAutomationBackend).GetNestedType("NativeInput",
        System.Reflection.BindingFlags.NonPublic);
    Assert(input is not null, "Win32 input payload layout must remain inspectable.");
    if (input is null) return Task.CompletedTask;
    int expected = IntPtr.Size == 8 ? 40 : 28;
    int padding = IntPtr.Size == 8 ? 8 : 4;
    Assert(System.Runtime.InteropServices.Marshal.SizeOf(input) == expected &&
        System.Runtime.InteropServices.Marshal.OffsetOf(input, "Data").ToInt32() == padding,
        "INPUT P/Invoke layout must agree with native architecture.");
    return Task.CompletedTask;
}

static Task DesktopForegroundAlreadyFocused()
{
    int activates = 0, waits = 0;
    bool result = DesktopForegroundAcquisition.TryAcquire(
        () => true, () => { activates++; return true; }, () => waits++);
    Assert(result && activates == 0 && waits == 0,
        "Already foreground must not cause a second activation or an artificial delay");
    return Task.CompletedTask;
}
static Task DesktopForegroundRequestsOnce()
{
    bool foreground = false;
    int activates = 0;
    bool result = DesktopForegroundAcquisition.TryAcquire(
        () => foreground,
        () => { activates++; foreground = true; return true; }, () => { });
    Assert(result && activates == 1,
        "NIRA should activate the observed target once and recheck foreground");
    return Task.CompletedTask;
}
static Task DesktopForegroundDenial()
{
    int attempts = 0, waits = 0;
    bool result = DesktopForegroundAcquisition.TryAcquire(
        () => false, () => { attempts++; return false; }, () => waits++, checks: 3);
    Assert(!result && attempts == 1 && waits == 3,
        "Denied activation must stay denied after a bounded observation interval");
    return Task.CompletedTask;
}
static Task DesktopForegroundInvalidChecks()
{
    bool rejected = false;
    try { DesktopForegroundAcquisition.TryAcquire(() => false, () => false,
        () => { }, checks: 0); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Assert(rejected, "Activation observation checks must be bounded");
    return Task.CompletedTask;
}
static Task DesktopTypeToolAutonomousDescriptor()
{
    var tool = new DesktopTypeTextTool(new FakeDesktopBackend());
    Assert(tool.Descriptor.Description.Contains("NIRA itself activates", StringComparison.Ordinal) &&
        !tool.Descriptor.Description.Contains("switch manually", StringComparison.Ordinal) &&
        tool.Descriptor.Risk == ToolRisk.Dangerous,
        "Approved typing must own activation without downgrading host risk");
    return Task.CompletedTask;
}
static Task TaskAbandonTwoPresses()
{
    var gate = new TaskAbandonConfirmation();
    DateTimeOffset t = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
    Assert(!gate.ConfirmOrArm(t) && gate.IsArmed &&
        gate.ConfirmOrArm(t.AddSeconds(2)) && !gate.IsArmed,
        "One accidental click must never abandon an agent task");
    return Task.CompletedTask;
}
static Task TaskAbandonExpiryAndReset()
{
    var gate = new TaskAbandonConfirmation();
    DateTimeOffset t = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
    Assert(!gate.ConfirmOrArm(t) && !gate.ConfirmOrArm(t.AddSeconds(20)) &&
        gate.IsArmed, "Expired confirmation must not silently abandon");
    gate.Reset();
    Assert(!gate.IsArmed && !gate.ConfirmOrArm(t.AddSeconds(21)),
        "Reset requires a new first click");
    return Task.CompletedTask;
}
static Task TaskAbandonUncertainEffects()
{
    var task = new ConversationState { Status = ConversationStatus.Paused,
        RetryBlockCode = "DESKTOP_TEXT_NOT_VERIFIED" };
    task.Operations.Add(new AgentOperation { Status = OperationStatus.Succeeded });
    TaskAbandonConfirmation.MarkAbandoned(task);
    Assert(task.Status == ConversationStatus.NeedsReconciliation &&
        task.RetryBlockCode == "USER_ABANDONED_WITH_UNCERTAIN_ACTION" &&
        task.Operations.Count == 1,
        "Abandon cannot erase evidence of a possibly altered desktop document");
    var dispatched = new ConversationState { Status = ConversationStatus.Running };
    dispatched.Operations.Add(new AgentOperation { Status = OperationStatus.Dispatched });
    TaskAbandonConfirmation.MarkAbandoned(dispatched);
    Assert(dispatched.Status == ConversationStatus.NeedsReconciliation,
        "Abandon must preserve unknown in-flight tool effects");
    return Task.CompletedTask;
}
static Task TaskAbandonSafeTask()
{
    var task = new ConversationState { Status = ConversationStatus.AwaitingUser };
    TaskAbandonConfirmation.MarkAbandoned(task);
    Assert(task.Status == ConversationStatus.Failed && task.RetryBlockCode == "USER_ABANDONED",
        "Abandon should terminate an idle safe task without deleting its history");
    return Task.CompletedTask;
}

static Task ProductivityToolsAreRegistered()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    ToolRegistry tools = NiraStandardTools.Create(setup.Workspace, setup.Store, http,
        desktopBackend: new FakeDesktopBackend());
    foreach (string name in new[] { "productivity.inspect", "productivity.read",
        "productivity.create_document", "productivity.create_workbook" })
        Assert(tools.TryGet(name, out _) , "Missing general productivity capability " + name);
    Assert(tools.TryGet("productivity.create_document", out IAgentTool? writer) &&
        writer!.Descriptor.HasSideEffects && writer.Descriptor.Risk == ToolRisk.WriteWorkspace,
        "Office document writes must be scoped and authorized by the existing host policy.");
    return Task.CompletedTask;
}

static Task ProductivitySchemaRejectsUnknownArguments()
{
    var schema = new ProductivityCreateWorkbookTool(new WorkspacePathResolver(Path.GetTempPath())).Descriptor.Parameters;
    Assert(!JsonToolSchema.Validate(schema, JsonSerializer.SerializeToElement(new {
        path = "out.xlsx", sheets = Array.Empty<object>(), overwrite = true }), out _),
        "Unknown mutation switches must be rejected before any disk operation.");
    return Task.CompletedTask;
}

static Task ProductivityCreateReadWorkbook()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "report.xlsx");
    ToolReceipt created = ProductivityDocuments.CreateWorkbook(path,
        JsonSerializer.SerializeToElement(new[] { new {
            name = "Data", rows = new object?[][] {
                new object?[] { "Name", "Amount", "Active" },
                new object?[] { "Alpha", 42.25, true },
                new object?[] { "Beta", -17, false }
            } }
        }));
    Assert(created.Success && created.ChangedState && File.Exists(path),
        "Creating a workbook must publish a genuine new file with a changed-state receipt.");
    Assert(created.Data.GetProperty("contentVerified").GetBoolean(),
        "Created Excel must be independently reopened.");
    ToolReceipt inspected = ProductivityDocuments.Inspect(path);
    Assert(inspected.Success && inspected.Data.GetProperty("totalSheets").GetInt32() == 1,
        "Workbook inspection must report real sheets.");
    ToolReceipt read = ProductivityDocuments.Read(path, "Data", 1, 1, 10, 4, 1, 10);
    Assert(read.Success && read.Data.GetProperty("rows").GetArrayLength() == 3,
        "Excel reader must enumerate the actual saved rows.");
    JsonElement cells = read.Data.GetProperty("rows")[1].GetProperty("cells");
    Assert(cells[0].GetProperty("value").GetString() == "Alpha" &&
        cells[1].GetProperty("value").GetString() == "42.25" &&
        string.Equals(cells[2].GetProperty("value").GetString(), "TRUE", StringComparison.OrdinalIgnoreCase),
        "Excel must preserve string, number and boolean values.");
    return Task.CompletedTask;
}

static Task ProductivityWorkbookPaginates()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "paged.xlsx");
    var rows = Enumerable.Range(1, 7).Select(x => new object[] { x, "row-" + x }).ToArray();
    Assert(ProductivityDocuments.CreateWorkbook(path, JsonSerializer.SerializeToElement(new[] {
        new { name = "Values", rows } })).Success, "Creation must succeed");
    ToolReceipt page1 = ProductivityDocuments.Read(path, "Values", 1, 1, 3, 2, 1, 10);
    ToolReceipt page2 = ProductivityDocuments.Read(path, "Values", 1, 4, 3, 2, 1, 10);
    Assert(page1.Success && page2.Success &&
        page1.Data.GetProperty("hasMore").GetBoolean() &&
        page1.Data.GetProperty("rows")[0].GetProperty("row").GetInt32() == 1 &&
        page2.Data.GetProperty("rows")[0].GetProperty("row").GetInt32() == 4,
        "Real Excel pagination must yield distinct row ranges.");
    return Task.CompletedTask;
}

static Task ProductivityWorkbookPreservesLiteralFormula()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "literal.xlsx");
    Assert(ProductivityDocuments.CreateWorkbook(path, JsonSerializer.SerializeToElement(new[] {
        new { name = "Text", rows = new[] { new [] { "=1+1", "@user" } } }
    })).Success, "Workbook must be created");
    ToolReceipt read = ProductivityDocuments.Read(path, "Text", 1, 1, 2, 2, 1, 10);
    Assert(read.Success && !read.Data.GetProperty("rows")[0].GetProperty("cells")[0]
        .GetProperty("formula").GetBoolean(), "Literal strings must not become formulas.");
    return Task.CompletedTask;
}

static Task ProductivityWorkbookNeverOverwrites()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "existing.xlsx");
    File.WriteAllText(path, "ORIGINAL");
    ToolReceipt receipt = ProductivityDocuments.CreateWorkbook(path,
        JsonSerializer.SerializeToElement(new [] { new { name = "Data", rows = new [] { new [] { "x" } } } }));
    Assert(!receipt.Success && receipt.ErrorCode == "ALREADY_EXISTS" &&
        File.ReadAllText(path) == "ORIGINAL", "Workbook creation must never replace existing bytes.");
    return Task.CompletedTask;
}

static Task ProductivityWorkbookRejectsNestedObjects()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "unsupported.xlsx");
    var invalid = JsonSerializer.SerializeToElement(new[] { new {
        name = "Data", rows = new object[][] { new object[] { new { formula = "=NOW()" } } } } });
    ToolReceipt created = ProductivityDocuments.CreateWorkbook(path, invalid);
    Assert(!created.Success && created.ErrorCode == "CELL_TYPE" && !File.Exists(path),
        "Unsupported cell types must not result in a partial deliverable.");
    return Task.CompletedTask;
}

static Task ProductivityCreateReadDocument()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "memo.docx");
    JsonElement blocks = JsonSerializer.SerializeToElement(new object[] {
        new {kind="heading", text="Overview", level=2},
        new {kind="paragraph", text="The report is grounded in source data."}
    });
    ToolReceipt created = ProductivityDocuments.CreateDocument(path, "Report", blocks);
    Assert(created.Success && created.ChangedState && created.Data.GetProperty("contentVerified").GetBoolean(),
        "Word creation must publish and independently reopen its deliverable.");
    ToolReceipt inspected = ProductivityDocuments.Inspect(path);
    ToolReceipt read = ProductivityDocuments.Read(path, null, 1, 1, 1, 1, 1, 20);
    string contents = JsonSerializer.Serialize(read.Data);
    Assert(inspected.Success && read.Success && contents.Contains("Overview", StringComparison.Ordinal) &&
        contents.Contains("source data", StringComparison.Ordinal),
        "Word reader must recover real generated paragraphs.");
    return Task.CompletedTask;
}

static Task ProductivityWordCreatesTables()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "table.docx");
    var blocks = JsonSerializer.SerializeToElement(new object[] {
        new { kind="table", rows=new[] { new[] { "Account", "Balance" }, new[] { "A1", "750" } } }
    });
    Assert(ProductivityDocuments.CreateDocument(path, "Balances", blocks).Success, "Word table creation must succeed.");
    ToolReceipt inspected = ProductivityDocuments.Inspect(path);
    Assert(inspected.Success && inspected.Data.GetProperty("tables").GetInt32() == 1,
        "Word table must be a real OpenXML table, not tab-separated prose.");
    return Task.CompletedTask;
}

static Task ProductivityDocumentNeverOverwrites()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "existing.docx");
    File.WriteAllText(path, "ORIGINAL");
    ToolReceipt result = ProductivityDocuments.CreateDocument(path, "Hello",
        JsonSerializer.SerializeToElement(Array.Empty<object>()));
    Assert(!result.Success && result.ErrorCode == "ALREADY_EXISTS" &&
        File.ReadAllText(path) == "ORIGINAL", "Word creation must preserve existing content.");
    return Task.CompletedTask;
}

static Task ProductivityRejectsMissingParent()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "not-exists", "a.docx");
    ToolReceipt result = ProductivityDocuments.CreateDocument(path, "Hello",
        JsonSerializer.SerializeToElement(Array.Empty<object>()));
    Assert(!result.Success && result.ErrorCode == "PARENT_NOT_FOUND" && !File.Exists(path),
        "Missing parent is a safe failure with no stray output.");
    return Task.CompletedTask;
}

static Task ProductivityReadBoundsAndUnsupportedFiles()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "notes.txt");
    File.WriteAllText(path, "hello");
    Assert(ProductivityDocuments.Inspect(path).ErrorCode == "FORMAT_UNSUPPORTED", "Wrong format must be rejected.");
    Assert(ProductivityDocuments.Read(path, null, 1, 1, 10, 4, 1, 10).ErrorCode == "FORMAT_UNSUPPORTED",
        "Unsupported input must not be treated as a PDF/Office file.");
    return Task.CompletedTask;
}

static string ProductivityTestWorkbook(TestContext setup, int count = 3)
{
    string path = Path.Combine(setup.Workspace, "safe-read-" + Guid.NewGuid().ToString("N") + ".xlsx");
    object[][] rows = Enumerable.Range(1, count)
        .Select(i => new object[] { "Category " + i, i * 12.5, "Ref" + i }).ToArray();
    ToolReceipt created = ProductivityDocuments.CreateWorkbook(path,
        JsonSerializer.SerializeToElement(new[] { new { name = "Expenses", rows } }));
    Assert(created.Success && File.Exists(path), "Test prerequisite: actual Excel workbook creation must succeed.");
    return path;
}
static async Task ProductivityReadNormalizesZero()
{
    using var setup = new TestContext();
    string path = ProductivityTestWorkbook(setup);
    var reader = new ProductivityReadTool(new WorkspacePathResolver(setup.Workspace));
    var args = JsonSerializer.SerializeToElement(new { path, sheet = "Expenses",
        page = 0, startRow = 0, maxRows = 0, maxColumns = 0,
        startParagraph = 0, maxParagraphs = 0 });
    Assert(JsonToolSchema.Validate(reader.Descriptor.Parameters, args, out _),
        "Out-of-range optional integer read arguments should be validated by the read-only adapter.");
    ToolReceipt receipt = await reader.ExecuteAsync(args, default);
    Assert(receipt.Success && receipt.Data.GetProperty("startRow").GetInt32() == 1 &&
        receipt.Data.GetProperty("maxRows").GetInt32() == 1 &&
        receipt.Data.GetProperty("maxColumns").GetInt32() == 1 &&
        receipt.Data.GetProperty("rows").GetArrayLength() == 1,
        "Model-supplied zero-based/zero-sized bounds must become a single safe read.");
}
static async Task ProductivityReadClampsLimits()
{
    using var setup = new TestContext();
    string path = ProductivityTestWorkbook(setup, 90);
    var reader = new ProductivityReadTool(new WorkspacePathResolver(setup.Workspace));
    var args = JsonSerializer.SerializeToElement(new { path, startRow = 1,
        maxRows = 5000, maxColumns = 9000 });
    ToolReceipt receipt = await reader.ExecuteAsync(args, default);
    Assert(receipt.Success && receipt.Data.GetProperty("maxRows").GetInt32() == 80 &&
        receipt.Data.GetProperty("maxColumns").GetInt32() == 40 &&
        receipt.Data.GetProperty("rows").GetArrayLength() == 80 &&
        receipt.Data.GetProperty("hasMore").GetBoolean(),
        "Large safe reads must clamp to host limits and preserve pagination.");
}
static async Task ProductivityReadClampsNegative()
{
    using var setup = new TestContext();
    string path = ProductivityTestWorkbook(setup, 3);
    var reader = new ProductivityReadTool(new WorkspacePathResolver(setup.Workspace));
    var args = JsonSerializer.SerializeToElement(new { path, startRow = -90,
        maxRows = -5, maxColumns = -7, page = -3,
        startParagraph = -1, maxParagraphs = -200 });
    ToolReceipt receipt = await reader.ExecuteAsync(args, default);
    Assert(receipt.Success && receipt.Data.GetProperty("startRow").GetInt32() == 1 &&
        receipt.Data.GetProperty("rows").GetArrayLength() == 1,
        "Negative read-only pagination values must not cause an invalid-bounds loop.");
}
static Task ProductivityReadReportsBounds()
{
    using var setup = new TestContext();
    string path = ProductivityTestWorkbook(setup);
    ToolReceipt read = ProductivityDocuments.Read(path, null, 1, 2, 2, 3, 1, 20);
    Assert(read.Success && read.Data.GetProperty("startRow").GetInt32() == 2 &&
        read.Data.GetProperty("maxRows").GetInt32() == 2 &&
        read.Data.GetProperty("maxColumns").GetInt32() == 3 &&
        read.Data.GetProperty("rows")[0].GetProperty("row").GetInt32() == 2,
        "Read receipts must show the effective limits used for the actual result.");
    return Task.CompletedTask;
}
static Task ProductivityExcelIgnoresUnrelatedBounds()
{
    using var setup = new TestContext();
    string path = ProductivityTestWorkbook(setup);
    ToolReceipt read = ProductivityDocuments.Read(path, null,
        page: -99, startRow: 1, maxRows: 10, maxColumns: 4,
        startParagraph: -14, maxParagraphs: -3);
    Assert(read.Success && read.Data.GetProperty("rows").GetArrayLength() == 3,
        "Excel must ignore PDF/Word fields rather than rejecting useful reads.");
    return Task.CompletedTask;
}
static Task ProductivityWordIgnoresUnrelatedBounds()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "word-source.docx");
    var blocks = JsonSerializer.SerializeToElement(new[] {
        new { kind = "paragraph", text = "The workbook is ready." }
    });
    Assert(ProductivityDocuments.CreateDocument(path, "Work Summary", blocks).Success,
        "Test prerequisite: Word document creation failed.");
    ToolReceipt read = ProductivityDocuments.Read(path, null,
        page: -20, startRow: -5, maxRows: -99, maxColumns: 0,
        startParagraph: 1, maxParagraphs: 10);
    Assert(read.Success && read.Data.GetProperty("paragraphs").GetArrayLength() >= 2,
        "Word must not reject unrelated spreadsheet/PDF bounds.");
    return Task.CompletedTask;
}
static async Task ProductivityWordZeroIndex()
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "word-zero.docx");
    Assert(ProductivityDocuments.CreateDocument(path, "Summary",
        JsonSerializer.SerializeToElement(new[] { new { kind="paragraph", text="Line" } })).Success,
        "Word creation prerequisite failed.");
    var reader = new ProductivityReadTool(new WorkspacePathResolver(setup.Workspace));
    ToolReceipt receipt = await reader.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        path, startParagraph = 0, maxParagraphs = 10000, page = 0,
        startRow = 0, maxRows = 0, maxColumns = 0
    }), default);
    Assert(receipt.Success && receipt.Data.GetProperty("startParagraph").GetInt32() == 1 &&
        receipt.Data.GetProperty("maxParagraphs").GetInt32() == 80 &&
        receipt.Data.GetProperty("paragraphs").GetArrayLength() >= 2,
        "Word must accept a zero-based/oversized read-only model request.");
}
static async Task ProductivityReadOmittedDefaults()
{
    using var setup = new TestContext();
    string path = ProductivityTestWorkbook(setup);
    var reader = new ProductivityReadTool(new WorkspacePathResolver(setup.Workspace));
    ToolReceipt receipt = await reader.ExecuteAsync(JsonSerializer.SerializeToElement(new { path }), default);
    Assert(receipt.Success && receipt.Data.GetProperty("startRow").GetInt32() == 1 &&
        receipt.Data.GetProperty("maxRows").GetInt32() == 30 &&
        receipt.Data.GetProperty("maxColumns").GetInt32() == 24 &&
        receipt.Data.GetProperty("rows").GetArrayLength() == 3,
        "A bare path must be sufficient to read an ordinary workbook.");
}
static async Task ProductivityReadPagesAfterClamp()
{
    using var setup = new TestContext();
    string path = ProductivityTestWorkbook(setup, 82);
    var reader = new ProductivityReadTool(new WorkspacePathResolver(setup.Workspace));
    ToolReceipt first = await reader.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        path, startRow = 0, maxRows = 999
    }), default);
    ToolReceipt second = await reader.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        path, startRow = 81, maxRows = 999
    }), default);
    Assert(first.Success && second.Success && first.Data.GetProperty("hasMore").GetBoolean() &&
        first.Data.GetProperty("rows").GetArrayLength() == 80 &&
        second.Data.GetProperty("rows").GetArrayLength() == 2 &&
        second.Data.GetProperty("rows")[0].GetProperty("row").GetInt32() == 81 &&
        !second.Data.GetProperty("hasMore").GetBoolean(),
        "Bounded pagination must be non-overlapping and must stop at the actual end.");
}
static Task ProductivityReadSchemaStillStrict()
{
    var reader = new ProductivityReadTool(new WorkspacePathResolver(Path.GetTempPath()));
    Assert(!JsonToolSchema.Validate(reader.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { path = "a.xlsx", maxRows = "many" }), out _),
        "Non-integer pagination arguments must remain invalid.");
    Assert(!JsonToolSchema.Validate(reader.Descriptor.Parameters,
        JsonSerializer.SerializeToElement(new { path = "a.xlsx", overwrite = true }), out _),
        "Read tool must reject unexpected arguments such as overwrite.");
    return Task.CompletedTask;
}
static async Task ProductivityReadThenCreateWord()
{
    using var setup = new TestContext();
    string excel = ProductivityTestWorkbook(setup, 4);
    var reader = new ProductivityReadTool(new WorkspacePathResolver(setup.Workspace));
    ToolReceipt read = await reader.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        path = excel, maxRows = 10000, maxColumns = 10000, page = 0,
        startRow = 0, startParagraph = 0, maxParagraphs = 0
    }), default);
    Assert(read.Success && read.Data.GetProperty("rows").GetArrayLength() == 4,
        "The model must receive the saved workbook data even after bad optional bounds.");
    string word = Path.Combine(setup.Workspace, "completed-summary.docx");
    string row = read.Data.GetProperty("rows")[0].GetProperty("cells")[0]
        .GetProperty("value").GetString()!;
    ToolReceipt created = ProductivityDocuments.CreateDocument(word, "Expense Summary",
        JsonSerializer.SerializeToElement(new[] { new { kind="paragraph", text="First: " + row } }));
    Assert(created.Success && created.ChangedState && File.Exists(word) &&
        created.Data.GetProperty("contentVerified").GetBoolean(),
        "After an automatically recovered Excel read, Word creation must still work.");
    ToolReceipt doc = ProductivityDocuments.Read(word, null, 1, 1, 2, 2, 1, 10);
    Assert(doc.Success && doc.Data.ToString().Contains("First: Category 1", StringComparison.Ordinal),
        "Word must contain content derived from the real workbook read-back.");
}
static async Task ProductivityReadNoFileMutation()
{
    using var setup = new TestContext();
    string path = ProductivityTestWorkbook(setup);
    string before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    var reader = new ProductivityReadTool(new WorkspacePathResolver(setup.Workspace));
    ToolReceipt receipt = await reader.ExecuteAsync(JsonSerializer.SerializeToElement(new {
        path, page = 0, startRow = 0, maxRows = 10000,
        maxColumns = 10000, startParagraph = 0, maxParagraphs = 0
    }), default);
    string after = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    Assert(receipt.Success && !receipt.ChangedState && before == after,
        "Host normalization of a read must never modify or overwrite the source file.");
}

static Task ProductivityOutsideWorkspaceNeedsApproval()
{
    using var setup = new TestContext();
    var policy = new WorkspaceAuthority(setup.Workspace);
    var tool = new ProductivityCreateWorkbookTool(new WorkspacePathResolver(setup.Workspace));
    string target = Path.Combine(setup.Root, "outside.xlsx");
    Assert(!policy.IsPreapproved(tool.Descriptor, target),
        "Deliverables outside authorized workspace need separate host approval.");
    return Task.CompletedTask;
}


static Task M3b1RejectFormulaValue()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup), hash = M3bHash(source);
    string output = Path.Combine(setup.Workspace, "fake-formula.xlsx");
    ToolReceipt result = ProductivityRevisions.ReviseWorkbook(source, output, hash,
        JsonSerializer.SerializeToElement(new[] { new { sheet = "Expenses", cell = "B4", value = "=SUM(B2:B3)" } }));
    Assert(!result.Success && result.ErrorCode == "FORMULA_AS_TEXT" &&
        !File.Exists(output) && M3bHash(source) == hash &&
        result.Summary.Contains("'formula'", StringComparison.Ordinal),
        "Text pretending to be a formula must be rejected; no false verification receipt or output.");
    return Task.CompletedTask;
}

static Task M3b1RejectWhitespaceFormulaValue()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup);
    string output = Path.Combine(setup.Workspace, "fake-spaced.xlsx");
    ToolReceipt receipt = ProductivityRevisions.ReviseWorkbook(source, output, M3bHash(source),
        JsonSerializer.SerializeToElement(new[] { new { sheet = "Expenses", cell = "B4", value = "  =SUM(B2:B3)" } }));
    Assert(!receipt.Success && receipt.ErrorCode == "FORMULA_AS_TEXT" && !File.Exists(output),
        "A leading space cannot bypass formula-like text validation.");
    return Task.CompletedTask;
}

static Task M3b1AllowExplicitLiteralFormulaText()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup), hash = M3bHash(source);
    string output = Path.Combine(setup.Workspace, "intentional-literal.xlsx");
    ToolReceipt receipt = ProductivityRevisions.ReviseWorkbook(source, output, hash,
        JsonSerializer.SerializeToElement(new[] { new {
            sheet = "Expenses", cell = "C2", value = "=SAMPLE(1)", literalFormulaText = true
        } }));
    using var book = new ClosedXML.Excel.XLWorkbook(output);
    Assert(receipt.Success && !book.Worksheet("Expenses").Cell("C2").HasFormula &&
        book.Worksheet("Expenses").Cell("C2").GetString() == "=SAMPLE(1)" &&
        receipt.Data.GetProperty("verifiedFormulaCells").GetInt32() == 0 &&
        M3bHash(source) == hash,
        "A deliberately literal formula-looking string stays text with explicit opt-in.");
    return Task.CompletedTask;
}

static Task M3b1RepairFakeFormula()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup), hash = M3bHash(source);
    // Simulate the user's actual uploaded bug: the source has the formula text,
    // not an executable cell formula. Revise it without overwriting the source.
    string bad = Path.Combine(setup.Workspace, "bad-formula.xlsx");
    ToolReceipt badWrite = ProductivityRevisions.ReviseWorkbook(source, bad, hash,
        JsonSerializer.SerializeToElement(new[] { new {
            sheet = "Expenses", cell = "B4", value = "=SUM(B2:B3)", literalFormulaText = true
        } }));
    Assert(badWrite.Success, "Explicit test fixture must contain a literal fake formula.");
    string badHash = M3bHash(bad), corrected = Path.Combine(setup.Workspace, "real-formula.xlsx");
    ToolReceipt fixedWrite = ProductivityRevisions.ReviseWorkbook(bad, corrected, badHash,
        JsonSerializer.SerializeToElement(new[] { new { sheet = "Expenses", cell = "B4", formula = "SUM(B2:B3)" } }));
    using var fake = new ClosedXML.Excel.XLWorkbook(bad);
    using var real = new ClosedXML.Excel.XLWorkbook(corrected);
    Assert(fixedWrite.Success && !fake.Worksheet("Expenses").Cell("B4").HasFormula &&
        real.Worksheet("Expenses").Cell("B4").HasFormula &&
        real.Worksheet("Expenses").Cell("B4").FormulaA1 == "SUM(B2:B3)" &&
        fixedWrite.Data.GetProperty("verifiedFormulaCells").GetInt32() == 1 &&
        fixedWrite.Data.GetProperty("formulaTypeVerified").GetBoolean() &&
        M3bHash(bad) == badHash && M3bHash(source) == hash,
        "A fake text formula must become a real formula, independently reopened, while both originals stay intact.");
    return Task.CompletedTask;
}

static Task M3b1RejectLiteralFlagWithNumber()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup), output = Path.Combine(setup.Workspace, "flag-number.xlsx");
    ToolReceipt result = ProductivityRevisions.ReviseWorkbook(source, output, M3bHash(source),
        JsonSerializer.SerializeToElement(new[] { new { sheet = "Expenses", cell = "B4", value = 1200,
            literalFormulaText = true } }));
    Assert(!result.Success && result.ErrorCode == "CELL_LITERAL_MODE" && !File.Exists(output),
        "Literal override must not apply to numeric cell values.");
    return Task.CompletedTask;
}

static Task M3b1RejectLiteralFlagWithFormula()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup), output = Path.Combine(setup.Workspace, "flag-formula.xlsx");
    ToolReceipt result = ProductivityRevisions.ReviseWorkbook(source, output, M3bHash(source),
        JsonSerializer.SerializeToElement(new[] { new { sheet = "Expenses", cell = "B4",
            formula = "SUM(B2:B3)", literalFormulaText = true } }));
    Assert(!result.Success && result.ErrorCode == "CELL_LITERAL_MODE" && !File.Exists(output),
        "Literal override must not combine with the executable formula property.");
    return Task.CompletedTask;
}

static Task M3b1RejectExpressionText()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup), output = Path.Combine(setup.Workspace, "expression.xlsx");
    ToolReceipt result = ProductivityRevisions.ReviseWorkbook(source, output, M3bHash(source),
        JsonSerializer.SerializeToElement(new[] { new { sheet = "Expenses", cell = "B4", value = "=1+1" } }));
    Assert(!result.Success && result.ErrorCode == "FORMULA_AS_TEXT" && !File.Exists(output),
        "The formula-intent guard cannot be restricted to specific SUM functions.");
    return Task.CompletedTask;
}


// M3B.4: execution completion and bounded checkpoint review. Real workbook
// receipts are used in the exact six-of-seven restart regression.
static AgentOperation M3b4Operation(string id, string name, ToolReceipt receipt) => new()
{
    Id = id, ToolName = name, Status = receipt.Success ? OperationStatus.Succeeded : OperationStatus.Failed,
    ResultJson = JsonSerializer.Serialize(receipt), CompletedUtc = DateTimeOffset.UtcNow
};

static PlanStep M3b4Completed(string description, string kind, AgentOperation op) => new()
{
    Description = description, SuccessCriterion = "Host receipt confirms the result", Kind = kind,
    Status = "completed", ToolName = op.ToolName, EvidenceId = op.Id
};

static ConversationState M3b4Checkpoint(string lastDescription, string lastKind = "analysis",
    string? lastTool = null)
{
    const string objective = "Create a file, verify its contents, and give the result.";
    var changed = WorkflowProof("saved-write");
    var observed = WorkflowProof("saved-read", changed: false);
    return new ConversationState {
        Id = Guid.NewGuid(), Objective = objective,
        Messages = [new ChatEntry("user", objective)],
        Status = ConversationStatus.Paused, RetryBlockCode = "NO_PROGRESS",
        AutoPlanningAttempted = true, AutomaticPlanningOutcome = "planned_fallback_json",
        Operations = [changed, observed],
        Plan = [M3b4Completed("Create requested file", "action", changed),
            M3b4Completed("Read saved file", "observation", observed),
            new PlanStep { Description = lastDescription, SuccessCriterion = "Deliver the requested result and verification evidence",
                Kind = lastKind, ToolName = lastTool }]
    };
}

static async Task M3b4ResumeWorkbookReport()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup), originalHash = M3bHash(source);
    string output = Path.Combine(setup.Workspace, "corrected-real.xlsx");
    var a = M3b4Operation("inspect-source", "productivity.inspect", ProductivityDocuments.Inspect(source));
    var b = M3b4Operation("read-source", "productivity.read", ProductivityDocuments.Read(source, "Expenses", 1, 1, 5, 3, 1, 5));
    var c = M3b4Operation("source-hash", "filesystem.sha256", ToolReceipt.Ok("File hash", new { path = source, sha256 = originalHash }));
    var d = M3b4Operation("corrected-write", "productivity.revise_workbook", ProductivityRevisions.ReviseWorkbook(source,
        output, originalHash, JsonSerializer.SerializeToElement(new [] { new { sheet = "Expenses", cell = "B4", formula = "SUM(B2:B3)" } })));
    var e = M3b4Operation("inspect-corrected", "productivity.inspect", ProductivityDocuments.Inspect(output));
    var f = M3b4Operation("read-corrected", "productivity.read", ProductivityDocuments.Read(output, "Expenses", 1, 1, 5, 3, 1, 5));
    var state = M3b4Checkpoint("Report the corrected workbook path and verification evidence");
    state.Operations = [a,b,c,d,e,f];
    state.Plan = [M3b4Completed("Inspect source", "observation", a), M3b4Completed("Read source", "observation", b),
        M3b4Completed("Hash original", "observation", c), M3b4Completed("Save corrected version", "action", d),
        M3b4Completed("Inspect corrected version", "observation", e), M3b4Completed("Read corrected formula", "observation", f), state.Plan[^1]];
    string outputHash = M3bHash(output);
    await setup.Store.SaveAsync(state, default);
    var model = new FakeModel(_ => throw new InvalidOperationException("No inference needed to deliver saved receipts"));
    var outcome = await setup.Engine(model).RunAsync(state.Id, null, new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(outcome.Status == ConversationStatus.Completed && saved.Plan.Count == 7 &&
        saved.Plan.All(step => step.Status == "completed") && saved.Operations.Count == 6 &&
        saved.RetryBlockCode is null && saved.LastAnswer == outcome.Message &&
        outcome.Message.Contains(output, StringComparison.Ordinal) && outcome.Message.Contains(outputHash, StringComparison.Ordinal) &&
        outcome.Message.Contains("SUM(B2:B3)", StringComparison.Ordinal) &&
        outcome.Message.Contains("\"formula\":true", StringComparison.Ordinal) &&
        M3bHash(source) == originalHash && M3bHash(output) == outputHash,
        "A paused 6/7 checkpoint must report the real saved formula and hash without any model call or repeated file effect.");
}

static async Task M3b4LiveReport()
{
    using var setup = new TestContext();
    Guid id = Guid.NewGuid();
    int calls = 0;
    var model = new PlanningTestModel(AutoPlanChoice("plan",
        ("Save note", "Saved receipt", "action"), ("Read note", "Read-back evidence", "observation"),
        ("Report saved path and verification evidence", "Answer includes path and receipt", "analysis")),
        new FakeModel(messages => {
            calls++;
            return calls switch {
                1 => new ModelReply(null, [Call("write", "filesystem.write_text", new { path = "report.txt", content = "REPORT_READY" })]),
                2 => new ModelReply(null, [Call("link-write", "nira_plan_update", new { index = 0, status = "completed", evidenceId = M1cLastProof(messages) })]),
                3 => new ModelReply(null, [Call("read", "filesystem.read_text", new { path = "report.txt" })]),
                4 => new ModelReply(null, [Call("link-read", "nira_plan_update", new { index = 1, status = "completed", evidenceId = M1cLastProof(messages) })]),
                _ => throw new InvalidOperationException("Receipt delivery must not require a fifth inference")
            };
        }));
    var result = await setup.Engine(model).RunAsync(id, "Save a note, read it back, and report its path.", new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && calls == 4 && saved.Plan.All(step => step.Status == "completed") &&
        saved.Operations.Count == 2 && result.Message.Contains("REPORT_READY", StringComparison.Ordinal),
        "Host owns report delivery as soon as action and readback have distinct verified receipts.");
}

static async Task M3b4FocusedLink()
{
    using var setup = new TestContext();
    var state = M3b4Checkpoint("Inspect the saved file again", "observation", "filesystem.read_text");
    // The outstanding observation is deliberately not the last plan index.
    state.Plan.Insert(1, state.Plan[^1]); state.Plan.RemoveAt(state.Plan.Count - 1);
    state.Operations.Add(WorkflowProof("unused-read", changed: false));
    await setup.Store.SaveAsync(state, default);
    int calls = 0;
    var model = new FakeModel(messages => {
        Assert(messages.Count == 3 && messages[0].Content?.Contains("checkpoint review", StringComparison.Ordinal) == true &&
            messages[^1].Content?.Contains("unused-read", StringComparison.Ordinal) == true,
            "Review must carry the actual unused ID in a fresh small checkpoint request.");
        return ++calls == 1
            ? new ModelReply(null, [Call("link", "nira_plan_update", new { index = 1, status = "completed", evidenceId = "unused-read" })])
            : new ModelReply("The saved file has been verified.", []);
    });
    var result = await setup.Engine(model).RunAsync(state.Id, null, new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Completed && calls == 2 && saved.Operations.Count == 3 &&
        saved.Plan[1].EvidenceId == "unused-read" && saved.RetryBlockCode is null,
        "A compact review must link an existing unused observation without repeating work.");
}

static async Task M3b4FocusedNoMutation()
{
    using var setup = new TestContext();
    var state = M3b4Checkpoint("Perform another independent observation", "observation", "filesystem.read_text");
    await setup.Store.SaveAsync(state, default);
    int calls = 0;
    var model = new FakeModel(_ => ++calls == 1
        ? new ModelReply(null, [Call("forbidden-write", "filesystem.write_text", new { path = "duplicate.txt", content = "DO_NOT_WRITE" })])
        : new ModelReply(null, [Call("finish-" + calls, "nira_finish", new { outcome = "action", answer = "Done", evidence_ids = new [] { "saved-write" } })]));
    var engine = setup.Engine(model);
    var first = await engine.RunAsync(state.Id, null, new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    int priorCalls = calls;
    var second = await engine.RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(first.Status == ConversationStatus.Paused && second.Status == ConversationStatus.Paused && calls == priorCalls &&
        saved.RetryBlockCode == "NO_PROGRESS_FOCUSED" && saved.Plan[^1].Status == "pending" && saved.Operations.Count == 2 &&
        !File.Exists(Path.Combine(setup.Workspace, "duplicate.txt")) &&
        saved.Messages.Any(entry => entry.Content?.Contains("FOCUSED_REVIEW_TOOL_UNAVAILABLE", StringComparison.Ordinal) == true) &&
        first.Message.Contains("Perform another independent observation", StringComparison.Ordinal),
        "Recovery cannot dispatch an omitted mutation, bypass a missing read, or loop on unchanged retries.");
}

static async Task M3b4BadProof()
{
    using var setup = new TestContext();
    var state = M3b4Checkpoint("Report saved path and verification evidence");
    state.Plan[1].EvidenceId = "missing";
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel(_ => throw new InvalidOperationException("Invalid proof blocks review")))
        .RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Paused && (await setup.Store.LoadAsync(state.Id, default))!.Plan[^1].Status == "pending",
        "A report cannot complete if prior proof is invalid.");
}

static async Task M3b4Uncertain()
{
    using var setup = new TestContext();
    var state = M3b4Checkpoint("Report saved path and verification evidence");
    state.Operations.Add(WorkflowProof("unknown", status: OperationStatus.Dispatched));
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel(_ => throw new InvalidOperationException("Uncertain action blocks inference")))
        .RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.NeedsReconciliation && (await setup.Store.LoadAsync(state.Id, default))!.Plan[^1].Status == "pending",
        "Never turn uncertain external state into successful final reporting.");
}

static async Task M3b4TerminalAnalysis()
{
    using var setup = new TestContext();
    var state = M3b4Checkpoint("Explain the significance of the observed evidence");
    await setup.Store.SaveAsync(state, default);
    var model = new FakeModel([new ModelReply(null, [Call("finish-analysis", "nira_finish", new {
        outcome = "action", answer = "The readback supports the observed write; it is not a new file operation.",
        evidence_ids = new [] { "saved-write" } })])]);
    var result = await setup.Engine(model).RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed && (await setup.Store.LoadAsync(state.Id, default))!.Plan[^1].Status == "completed" &&
        result.Message.Contains("readback supports", StringComparison.Ordinal),
        "A focused native finish with the actual answer can complete the final analysis in the same checkpoint.");
}

static async Task M3b4ProviderFailure()
{
    using var setup = new TestContext();
    var state = M3b4Checkpoint("Explain the remaining discrepancy");
    await setup.Store.SaveAsync(state, default);
    var model = new FakeModel(_ => throw new AgentException("Synthetic provider error"));
    var result = await setup.Engine(model).RunAsync(state.Id, null, new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Paused && saved.RetryBlockCode == "NO_PROGRESS_FOCUSED" &&
        saved.Plan[^1].Status == "pending" && saved.Operations.Count == 2 &&
        result.Message.Contains("Explain the remaining discrepancy", StringComparison.Ordinal) &&
        result.Message.Contains("Host-recorded execution evidence", StringComparison.Ordinal),
        "A provider error must show the exact remaining task and saved effects, not erase evidence.");
}

static async Task M3b4MarkedReport()
{
    using var setup = new TestContext();
    var state = M3b4Checkpoint("Report saved path and verification evidence");
    state.Plan[^1].Status = "completed";
    await setup.Store.SaveAsync(state, default);
    var result = await setup.Engine(new FakeModel(_ => throw new InvalidOperationException("Report delivery is host-owned")))
        .RunAsync(state.Id, null, new FakeApprovals(false));
    Assert(result.Status == ConversationStatus.Completed &&
        result.Message.Contains("Recorded verification observations", StringComparison.Ordinal) &&
        (await setup.Store.LoadAsync(state.Id, default))!.Operations.Count == 2,
        "An already marked report must be delivered even if the provider never returned a final answer.");
}

static async Task M3b4SubstantiveReport()
{
    using var setup = new TestContext();
    var state = M3b4Checkpoint("Report saved path and calculate the outstanding total");
    await setup.Store.SaveAsync(state, default);
    int calls = 0;
    var model = new FakeModel(_ => { calls++; return new ModelReply(null, []); });
    var result = await setup.Engine(model).RunAsync(state.Id, null, new FakeApprovals(false));
    var saved = (await setup.Store.LoadAsync(state.Id, default))!;
    Assert(result.Status == ConversationStatus.Paused && calls > 0 && saved.Plan[^1].Status == "pending" && saved.Operations.Count == 2,
        "A reporting step with actual unfinished computation must not be host-completed from generic receipts.");
}

static Task M3b1ReadFormulaType()
{
    using var setup = new TestContext();
    string source = M3bOriginalWorkbook(setup), output = Path.Combine(setup.Workspace, "typed.xlsx");
    ToolReceipt revised = ProductivityRevisions.ReviseWorkbook(source, output, M3bHash(source),
        JsonSerializer.SerializeToElement(new[] {
            new { sheet = "Expenses", cell = "B4", formula = "SUM(B2:B3)" }
        }));
    Assert(revised.Success, "Typed read fixture must be created.");
    string before = M3bHash(output);
    ToolReceipt read = ProductivityDocuments.Read(output, "Expenses", 1, 1, 5, 3, 1, 5);
    string after = M3bHash(output);
    var rows = read.Data.GetProperty("rows").EnumerateArray().ToArray();
    JsonElement target = rows.Single(row => row.GetProperty("row").GetInt32() == 4)
        .GetProperty("cells").EnumerateArray().Single(c => c.GetProperty("address").GetString() == "B4");
    Assert(read.Success && target.GetProperty("formula").GetBoolean() &&
        target.GetProperty("cellType").GetString() == "formula" &&
        target.GetProperty("formulaA1").GetString() == "SUM(B2:B3)" && before == after,
        "A read must reveal actual Excel formula type, not merely a string of matching characters.");
    return Task.CompletedTask;
}

static Task M3bRevisionToolsRegistered()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    ToolRegistry registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http,
        desktopBackend: new FakeDesktopBackend());
    foreach (string name in new[] { "productivity.revise_workbook", "productivity.revise_document" })
    {
        Assert(registry.TryGet(name, out IAgentTool? tool) && tool is not null &&
            tool.Descriptor.HasSideEffects && tool.Descriptor.Risk == ToolRisk.WriteWorkspace,
            "Revision tools must be registered as auditable workspace mutations.");
    }
    return Task.CompletedTask;
}

static Task M3bRevisionToolSchemasStrict()
{
    var resolver = new WorkspacePathResolver(Path.GetTempPath());
    var writer = new ProductivityReviseWorkbookTool(resolver);
    Assert(!JsonToolSchema.Validate(writer.Descriptor.Parameters, JsonSerializer.SerializeToElement(new {
        sourcePath="a.xlsx", outputPath="b.xlsx", expectedSha256=new string('a',64),
        edits=new object[0], overwrite=true }), out _),
        "Revision tools must reject a model's overwrite flag.");
    var doc = new ProductivityReviseDocumentTool(resolver);
    Assert(!JsonToolSchema.Validate(doc.Descriptor.Parameters, JsonSerializer.SerializeToElement(new {
        sourcePath="a.docx", outputPath="b.docx", expectedSha256=new string('a',64),
        operations=new [] { new { kind="deleteEverything" } } }), out _),
        "Document tool must reject unregistered operation kinds.");
    return Task.CompletedTask;
}

static string M3bOriginalWorkbook(TestContext setup)
{
    string source=Path.Combine(setup.Workspace,"expenses-original.xlsx");
    Assert(ProductivityDocuments.CreateWorkbook(source, JsonSerializer.SerializeToElement(new [] {
        new { name="Expenses", rows=new object[][] {
            new object[]{"Category","Amount"},new object[]{"Rent",1200},
            new object[]{"Utilities",150},new object[]{"Total",1350}
        } }
    })).Success, "Test fixture should be a real XLSX.");
    return source;
}

static string M3bHash(string path) => Convert.ToHexString(
    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

static Task M3bWorkbookRevisionPreservesSource()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup), sha=M3bHash(src);
    string output=Path.Combine(setup.Workspace,"expenses-v2.xlsx");
    var revisions=JsonSerializer.SerializeToElement(new object[] {
        new {sheet="Expenses",cell="B4", formula="SUM(B2:B3)",bold=true,numberFormat="$#,##0.00"},
        new {sheet="Expenses",cell="A5",value="Review",bold=true}
    });
    ToolReceipt receipt=ProductivityRevisions.ReviseWorkbook(src,output,sha,revisions);
    Assert(receipt.Success && receipt.ChangedState && File.Exists(output) &&
        M3bHash(src)==sha && receipt.Data.GetProperty("sourceUnchanged").GetBoolean() &&
        receipt.Data.GetProperty("contentVerified").GetBoolean() &&
        receipt.Data.GetProperty("originalRetainedAsBackup").GetBoolean(),
        "A new Excel version must have independent evidence and retain the exact original bytes.");
    using var workbook=new ClosedXML.Excel.XLWorkbook(output);
    Assert(workbook.Worksheet("Expenses").Cell("B4").HasFormula &&
        workbook.Worksheet("Expenses").Cell("B4").FormulaA1=="SUM(B2:B3)" &&
        workbook.Worksheet("Expenses").Cell("A5").GetString()=="Review",
        "The independently opened revision must contain formula and text edits.");
    return Task.CompletedTask;
}

static Task M3bWorkbookLiteralIdPreserved()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup), output=Path.Combine(setup.Workspace,"identifiers-v2.xlsx");
    ToolReceipt result=ProductivityRevisions.ReviseWorkbook(src,output,M3bHash(src),
        JsonSerializer.SerializeToElement(new[]{new{sheet="Expenses",cell="C2",value="0012345678"}}));
    using var book=new ClosedXML.Excel.XLWorkbook(output);
    Assert(result.Success && !book.Worksheet("Expenses").Cell("C2").HasFormula &&
        book.Worksheet("Expenses").Cell("C2").GetString()=="0012345678",
        "Zero-prefixed identifiers must remain literal text.");
    return Task.CompletedTask;
}

static Task M3bWorkbookStaleHashDenied()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup), output=Path.Combine(setup.Workspace,"denied.xlsx");
    ToolReceipt result=ProductivityRevisions.ReviseWorkbook(src,output,new string('0',64),
        JsonSerializer.SerializeToElement(new[]{new{sheet="Expenses",cell="B2",value=99}}));
    Assert(!result.Success && result.ErrorCode=="STALE_SOURCE" && !File.Exists(output),
        "An outdated source hash must abort without creating a revision.");
    return Task.CompletedTask;
}

static Task M3bWorkbookDuplicateEditDenied()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup), output=Path.Combine(setup.Workspace,"duplicate.xlsx");
    ToolReceipt result=ProductivityRevisions.ReviseWorkbook(src,output,M3bHash(src),
        JsonSerializer.SerializeToElement(new[]{
            new{sheet="Expenses",cell="B2",value=100},
            new{sheet="Expenses",cell="b2",value=200}
        }));
    Assert(!result.Success && result.ErrorCode=="CELL_REVISION" && !File.Exists(output),
        "The same cell cannot be silently modified twice in one revision.");
    return Task.CompletedTask;
}

static Task M3bWorkbookDestinationNeverOverwritten()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup), output=Path.Combine(setup.Workspace,"existing.xlsx");
    File.WriteAllText(output,"KEEP");
    ToolReceipt result=ProductivityRevisions.ReviseWorkbook(src,output,M3bHash(src),
        JsonSerializer.SerializeToElement(new[]{new{sheet="Expenses",cell="B2",value=1}}));
    Assert(!result.Success && result.ErrorCode=="ALREADY_EXISTS" && File.ReadAllText(output)=="KEEP",
        "The revision must refuse an already used version name without overwriting it.");
    return Task.CompletedTask;
}

static Task M3bWorkbookUnknownSheetDenied()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup),output=Path.Combine(setup.Workspace,"unknown.xlsx");
    ToolReceipt result=ProductivityRevisions.ReviseWorkbook(src,output,M3bHash(src),
        JsonSerializer.SerializeToElement(new[]{new{sheet="Missing",cell="B2",value=1}}));
    Assert(!result.Success && result.ErrorCode=="SHEET_NOT_FOUND" && !File.Exists(output),
        "Unknown worksheets must be rejected without partial outputs.");
    return Task.CompletedTask;
}

static Task M3bWorkbookUnsafeFormulaDenied()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup),output=Path.Combine(setup.Workspace,"external.xlsx");
    ToolReceipt result=ProductivityRevisions.ReviseWorkbook(src,output,M3bHash(src),
        JsonSerializer.SerializeToElement(new[]{new{sheet="Expenses",cell="B4",formula="WEBSERVICE(\"https://example.org\")"}}));
    Assert(!result.Success && result.ErrorCode=="CELL_FORMULA" && !File.Exists(output),
        "Formulas that try to access external resources must be refused.");
    return Task.CompletedTask;
}

static string M3bOriginalDocument(TestContext setup)
{
    string src=Path.Combine(setup.Workspace,"summary-original.docx");
    Assert(ProductivityDocuments.CreateDocument(src,"Monthly Report",JsonSerializer.SerializeToElement(new[]{
        new{kind="paragraph",text="Category\tAmount\nRent\t1200\nFood\t300"},
        new{kind="paragraph",text="Draft totals require verification."}
    })).Success,"Test requires an actual Word document.");
    return src;
}

static Task M3bWordReplaceParagraphPreservesSource()
{
    using var setup=new TestContext();
    string src=M3bOriginalDocument(setup),output=Path.Combine(setup.Workspace,"paragraph-v2.docx"),sha=M3bHash(src);
    ToolReceipt receipt=ProductivityRevisions.ReviseDocument(src,output,sha,
        JsonSerializer.SerializeToElement(new[]{new{
            kind="replaceParagraph",paragraphIndex=3,
            expectedText="Draft totals require verification.",text="Totals were reviewed."
        }}));
    ToolReceipt read=ProductivityDocuments.Read(output,null,1,1,1,1,1,10);
    Assert(receipt.Success && M3bHash(src)==sha && read.Success &&
        JsonSerializer.Serialize(read.Data).Contains("Totals were reviewed.",StringComparison.Ordinal),
        "Word paragraph revisions must be saved to a new file with the original unchanged.");
    return Task.CompletedTask;
}

static Task M3bWordConvertsParagraphToRealTable()
{
    using var setup=new TestContext();
    string src=M3bOriginalDocument(setup),output=Path.Combine(setup.Workspace,"table-v2.docx"),sha=M3bHash(src);
    ToolReceipt result=ProductivityRevisions.ReviseDocument(src,output,sha,
        JsonSerializer.SerializeToElement(new[]{new {
            kind="replaceParagraphWithTable", paragraphIndex=2,
            expectedText="Category\tAmount\nRent\t1200\nFood\t300",
            rows=new[]{new[]{"Category","Amount"},new[]{"Rent","1200"},new[]{"Food","300"}}
        }}));
    ToolReceipt inspection=ProductivityDocuments.Inspect(output);
    Assert(result.Success && inspection.Success && inspection.Data.GetProperty("tables").GetInt32()==1 &&
        M3bHash(src)==sha,
        "Text-based rows must become a real Word table while preserving the original document.");
    return Task.CompletedTask;
}

static Task M3bWordAppendHeadingAndTable()
{
    using var setup=new TestContext();
    string src=M3bOriginalDocument(setup),output=Path.Combine(setup.Workspace,"append-v2.docx");
    var operations=JsonSerializer.SerializeToElement(new object[]{
        new{kind="appendHeading",text="Analysis"},
        new{kind="appendParagraph",text="Food spending was reviewed."},
        new{kind="appendTable",rows=new[]{new[]{"Item","Amount"},new[]{"Food","300"}}}
    });
    ToolReceipt result=ProductivityRevisions.ReviseDocument(src,output,M3bHash(src),operations);
    ToolReceipt inspect=ProductivityDocuments.Inspect(output);
    Assert(result.Success && inspect.Success && inspect.Data.GetProperty("tables").GetInt32()==1 &&
        inspect.Data.GetProperty("paragraphs").GetInt32()>=5,
        "Appended headings, paragraphs and a structured table must be independently readable.");
    return Task.CompletedTask;
}

static Task M3bWordStaleParagraphDenied()
{
    using var setup=new TestContext();
    string src=M3bOriginalDocument(setup),output=Path.Combine(setup.Workspace,"stale.docx");
    ToolReceipt result=ProductivityRevisions.ReviseDocument(src,output,M3bHash(src),
        JsonSerializer.SerializeToElement(new[]{new {
            kind="replaceParagraph",paragraphIndex=2,
            expectedText="I am inventing source content",text="bad"
        }}));
    Assert(!result.Success && result.ErrorCode=="STALE_PARAGRAPH" && !File.Exists(output),
        "Word revision must verify the exact source paragraph before changing it.");
    return Task.CompletedTask;
}

static Task M3bWordNoOverwrite()
{
    using var setup=new TestContext();
    string src=M3bOriginalDocument(setup),output=Path.Combine(setup.Workspace,"exists.docx");
    File.WriteAllText(output,"PRESERVE");
    ToolReceipt result=ProductivityRevisions.ReviseDocument(src,output,M3bHash(src),
        JsonSerializer.SerializeToElement(new[]{new{kind="appendParagraph",text="Hi"}}));
    Assert(!result.Success && result.ErrorCode=="ALREADY_EXISTS" && File.ReadAllText(output)=="PRESERVE",
        "Existing Word documents must never be overwritten by a revision.");
    return Task.CompletedTask;
}

static async Task M3bRevisionOutsideWorkspaceDenied()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup);
    var tool=new ProductivityReviseWorkbookTool(new WorkspacePathResolver(setup.Workspace));
    string output=Path.Combine(setup.Root,"outside.xlsx");
    ToolReceipt result=await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new{
        sourcePath=src,outputPath=output,expectedSha256=M3bHash(src),
        edits=new[]{new{sheet="Expenses",cell="B2",value=1}}
    }),default);
    Assert(!result.Success && result.ErrorCode=="WORKSPACE_SCOPE" && !File.Exists(output),
        "Revision operation must not send contents outside the authorized workspace.");
}

static async Task M3bRevisionReceiptsReachAgent()
{
    using var setup=new TestContext();
    string src=M3bOriginalWorkbook(setup),sha=M3bHash(src);
    var writer=new ProductivityReviseWorkbookTool(new WorkspacePathResolver(setup.Workspace));
    var create=await writer.ExecuteAsync(JsonSerializer.SerializeToElement(new{
        sourcePath=src,outputPath="revision-v2.xlsx",expectedSha256=sha,
        edits=new[]{new{sheet="Expenses",cell="B4",formula="SUM(B2:B3)"}}
    }),default);
    Assert(create.Success && create.ChangedState && create.Data.GetProperty("sourceUnchanged").GetBoolean() &&
        File.Exists(Path.Combine(setup.Workspace,"revision-v2.xlsx")),
        "AI tool must provide an audited changed-state receipt with a safe new revision.");
}


static string M3cFixture(TestContext setup)
{
    string path = Path.Combine(setup.Workspace, "pdf-fixture.pdf");
    File.WriteAllBytes(path, Convert.FromBase64String("JVBERi0xLjMKJZOMi54gUmVwb3J0TGFiIEdlbmVyYXRlZCBQREYgZG9jdW1lbnQgKG9wZW5zb3VyY2UpCjEgMCBvYmoKPDwKL0YxIDIgMCBSCj4+CmVuZG9iagoyIDAgb2JqCjw8Ci9CYXNlRm9udCAvSGVsdmV0aWNhIC9FbmNvZGluZyAvV2luQW5zaUVuY29kaW5nIC9OYW1lIC9GMSAvU3VidHlwZSAvVHlwZTEgL1R5cGUgL0ZvbnQKPj4KZW5kb2JqCjMgMCBvYmoKPDwKL0NvbnRlbnRzIDkgMCBSIC9NZWRpYUJveCBbIDAgMCA2MTIgNzkyIF0gL1BhcmVudCA4IDAgUiAvUmVzb3VyY2VzIDw8Ci9Gb250IDEgMCBSIC9Qcm9jU2V0IFsgL1BERiAvVGV4dCAvSW1hZ2VCIC9JbWFnZUMgL0ltYWdlSSBdCj4+IC9Sb3RhdGUgMCAvVHJhbnMgPDwKCj4+IAogIC9UeXBlIC9QYWdlCj4+CmVuZG9iago0IDAgb2JqCjw8Ci9Db250ZW50cyAxMCAwIFIgL01lZGlhQm94IFsgMCAwIDYxMiA3OTIgXSAvUGFyZW50IDggMCBSIC9SZXNvdXJjZXMgPDwKL0ZvbnQgMSAwIFIgL1Byb2NTZXQgWyAvUERGIC9UZXh0IC9JbWFnZUIgL0ltYWdlQyAvSW1hZ2VJIF0KPj4gL1JvdGF0ZSAwIC9UcmFucyA8PAoKPj4gCiAgL1R5cGUgL1BhZ2UKPj4KZW5kb2JqCjUgMCBvYmoKPDwKL0NvbnRlbnRzIDExIDAgUiAvTWVkaWFCb3ggWyAwIDAgNjEyIDc5MiBdIC9QYXJlbnQgOCAwIFIgL1Jlc291cmNlcyA8PAovRm9udCAxIDAgUiAvUHJvY1NldCBbIC9QREYgL1RleHQgL0ltYWdlQiAvSW1hZ2VDIC9JbWFnZUkgXQo+PiAvUm90YXRlIDAgL1RyYW5zIDw8Cgo+PiAKICAvVHlwZSAvUGFnZQo+PgplbmRvYmoKNiAwIG9iago8PAovUGFnZU1vZGUgL1VzZU5vbmUgL1BhZ2VzIDggMCBSIC9UeXBlIC9DYXRhbG9nCj4+CmVuZG9iago3IDAgb2JqCjw8Ci9BdXRob3IgKGFub255bW91cykgL0NyZWF0aW9uRGF0ZSAoRDoyMDAwMDEwMTAwMDAwMCswMCcwMCcpIC9DcmVhdG9yIChhbm9ueW1vdXMpIC9LZXl3b3JkcyAoKSAvTW9kRGF0ZSAoRDoyMDAwMDEwMTAwMDAwMCswMCcwMCcpIC9Qcm9kdWNlciAoUmVwb3J0TGFiIFBERiBMaWJyYXJ5IC0gXChvcGVuc291cmNlXCkpIAogIC9TdWJqZWN0ICh1bnNwZWNpZmllZCkgL1RpdGxlICh1bnRpdGxlZCkgL1RyYXBwZWQgL0ZhbHNlCj4+CmVuZG9iago4IDAgb2JqCjw8Ci9Db3VudCAzIC9LaWRzIFsgMyAwIFIgNCAwIFIgNSAwIFIgXSAvVHlwZSAvUGFnZXMKPj4KZW5kb2JqCjkgMCBvYmoKPDwKL0ZpbHRlciBbIC9BU0NJSTg1RGVjb2RlIC9GbGF0ZURlY29kZSBdIC9MZW5ndGggMTY3Cj4+CnN0cmVhbQpHYXJXMDVta0lfJ0xfW1lgQFMtTFJORiMyNCUwTkRPV3NmLFUmSTtNO0ZANS1pdTsvPC9uQl0+JG88cztQJWAubCIlVUEuLCozIjU6ZyclQClKQD5uOlooRDMtUylGQWw/XXVdZCR1UlJqK0VPWSpuR006K01MMF1TT0FrKE5vdCo0aWVlYDJLQGJUXEM0WW9mSzFuUmNRcnAvbjU5RkRYa0U3PyN+PmVuZHN0cmVhbQplbmRvYmoKMTAgMCBvYmoKPDwKL0ZpbHRlciBbIC9BU0NJSTg1RGVjb2RlIC9GbGF0ZURlY29kZSBdIC9MZW5ndGggMTQwCj4+CnN0cmVhbQpHYXBRaDBFPUYsMFVcSDNUXHBOWVReUUtrP3RjPklQLDtXI1UxXjIzaWhQRU1fP0NXNEtJU2k8IVs3YCNPQl9zS09KOVwhZS9YMWxNTHM/amlgJUUzLD8iXD1Lb2RqPiYxb0daJG05a0o7VGdnb2I4JDMzRXNtImQ5MlxeXE9FO21pITw9b2ksKT9+PmVuZHN0cmVhbQplbmRvYmoKMTEgMCBvYmoKPDwKL0ZpbHRlciBbIC9BU0NJSTg1RGVjb2RlIC9GbGF0ZURlY29kZSBdIC9MZW5ndGggNTkKPj4Kc3RyZWFtCkdhcFFoMEU9RiwwVVxIM1RccE5ZVF5RS2s/dGM+SVAsO1cjVTFeMjNpaFBFTV9QUCRPITNeLEM1UX4+ZW5kc3RyZWFtCmVuZG9iagp4cmVmCjAgMTIKMDAwMDAwMDAwMCA2NTUzNSBmIAowMDAwMDAwMDYxIDAwMDAwIG4gCjAwMDAwMDAwOTIgMDAwMDAgbiAKMDAwMDAwMDE5OSAwMDAwMCBuIAowMDAwMDAwMzkyIDAwMDAwIG4gCjAwMDAwMDA1ODYgMDAwMDAgbiAKMDAwMDAwMDc4MCAwMDAwMCBuIAowMDAwMDAwODQ4IDAwMDAwIG4gCjAwMDAwMDExMDkgMDAwMDAgbiAKMDAwMDAwMTE4MCAwMDAwMCBuIAowMDAwMDAxNDM3IDAwMDAwIG4gCjAwMDAwMDE2NjggMDAwMDAgbiAKdHJhaWxlcgo8PAovSUQgCls8MWMxNzgxOThmYmRmYTUxYjI1OTk1ZDg5ZDQxMDIwNDM+PDFjMTc4MTk4ZmJkZmE1MWIyNTk5NWQ4OWQ0MTAyMDQzPl0KJSBSZXBvcnRMYWIgZ2VuZXJhdGVkIFBERiBkb2N1bWVudCAtLSBkaWdlc3QgKG9wZW5zb3VyY2UpCgovSW5mbyA3IDAgUgovUm9vdCA2IDAgUgovU2l6ZSAxMgo+PgpzdGFydHhyZWYKMTgxNwolJUVPRgo="));
    return path;
}

static Task M3cExport()
{
    using var setup = new TestContext();
    string source = M3cFixture(setup), hash = M3bHash(source), target = Path.Combine(setup.Workspace, "all.txt");
    var receipt = PdfProcessing.ExportText(source, target);
    Assert(receipt.Success && receipt.ChangedState, "Real PDF export must succeed.");
    string text = File.ReadAllText(target);
    Assert(text.Contains("Travel expense: 125", StringComparison.Ordinal) && text.Contains("Travel expense: 250", StringComparison.Ordinal) &&
        text.Contains("PDF page 3", StringComparison.Ordinal) && text.Contains("No extractable text", StringComparison.Ordinal) &&
        receipt.Data.GetProperty("noTextPages")[0].GetInt32() == 3 && receipt.Data.GetProperty("noTextPages").GetArrayLength() == 1 &&
        receipt.Data.GetProperty("allPagesCovered").GetBoolean() && !receipt.Data.GetProperty("ocrPerformed").GetBoolean() &&
        receipt.Data.GetProperty("contentVerified").GetBoolean() && receipt.Data.GetProperty("sourceSha256").GetString() == hash &&
        receipt.Data.GetProperty("outputSha256").GetString() == M3bHash(target) && M3bHash(source) == hash,
        "PDF export must retain source and verify actual saved text, page boundaries and missing text.");
    return Task.CompletedTask;
}

static Task M3cRange()
{
    using var setup = new TestContext();
    string source = M3cFixture(setup), target = Path.Combine(setup.Workspace, "range.txt");
    var r = PdfProcessing.ExportText(source, target, 2, 2);
    Assert(r.Success && r.Data.GetProperty("exportedPages").GetInt32() == 1 && !r.Data.GetProperty("allPagesCovered").GetBoolean() &&
        File.ReadAllText(target).Contains("Travel expense: 250", StringComparison.Ordinal) &&
        !File.ReadAllText(target).Contains("Travel expense: 125", StringComparison.Ordinal), "Inclusive page range must be exact.");
    return Task.CompletedTask;
}

static Task M3cNoOverwrite()
{
    using var setup = new TestContext();
    string source = M3cFixture(setup), target = Path.Combine(setup.Workspace, "existing.txt");
    File.WriteAllText(target, "keep");
    var r = PdfProcessing.ExportText(source, target);
    Assert(!r.Success && r.ErrorCode == "ALREADY_EXISTS" && File.ReadAllText(target) == "keep", "No overwrite.");
    return Task.CompletedTask;
}

static Task M3cInvalidRange()
{
    using var setup = new TestContext();
    string source = M3cFixture(setup), target = Path.Combine(setup.Workspace, "bad.txt");
    var r = PdfProcessing.ExportText(source, target, 1, 4);
    Assert(!r.Success && r.ErrorCode == "PDF_PAGE_RANGE" && !File.Exists(target), "Invalid range must not publish.");
    return Task.CompletedTask;
}

static Task M3cSearch()
{
    using var setup = new TestContext();
    string source = M3cFixture(setup);
    var a = PdfProcessing.Search(source, "TRAVEL EXPENSE", 1, 1);
    var b = PdfProcessing.Search(source, "TRAVEL EXPENSE", 2, 2);
    Assert(a.Success && b.Success && !a.ChangedState && a.Data.GetProperty("hits")[0].GetProperty("page").GetInt32() == 1 &&
        a.Data.GetProperty("nextPage").GetInt32() == 2 && b.Data.GetProperty("hits")[0].GetProperty("page").GetInt32() == 2 &&
        b.Data.GetProperty("noTextPages")[0].GetInt32() == 3 && !b.Data.GetProperty("hasMore").GetBoolean(), "Search continuation and no-text evidence must be accurate.");
    return Task.CompletedTask;
}

static async Task M3cScope()
{
    using var setup = new TestContext();
    string source = M3cFixture(setup), outside = Path.Combine(setup.Root, "outside.pdf");
    File.Copy(source, outside);
    var tool = new PdfExportTextTool(new WorkspacePathResolver(setup.Workspace));
    var r = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {sourcePath = outside, outputPath = "denied.txt"}), default);
    Assert(!r.Success && r.ErrorCode == "WORKSPACE_SCOPE" && !File.Exists(Path.Combine(setup.Workspace,"denied.txt")), "In-scope output cannot authorize out-of-scope input.");
}

static Task M3cRegistered()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    Assert(registry.TryGet("productivity.pdf_export_text", out var export) && export is not null && export.Descriptor.HasSideEffects &&
        registry.TryGet("productivity.pdf_search", out var search) && search is not null && !search.Descriptor.HasSideEffects &&
        !registry.Validate("productivity.pdf_export_text", "{\"sourcePath\":\"x.pdf\",\"outputPath\":\"x.txt\",\"extra\":true}", out _, out _), "PDF registry and schema must be correct.");
    return Task.CompletedTask;
}

static async Task M3cAgent()
{
    using var setup = new TestContext();
    _ = M3cFixture(setup);
    Guid id = Guid.NewGuid();
    var model = new FakeModel(new ModelReply[] {
        new(null, [Call("pdf-export", "productivity.pdf_export_text", new { sourcePath = "pdf-fixture.pdf", outputPath = "extracted.txt" })]),
        new("Here is the extracted text file.", [])
    });
    var result = await setup.Engine(model).RunAsync(id, "Extract this PDF to a text file.", new FakeApprovals(false));
    var state = (await setup.Store.LoadAsync(id, default))!;
    Assert(result.Status == ConversationStatus.Completed && state.Operations.Count == 1 &&
        state.Operations[0].Status == OperationStatus.Succeeded && result.Message.Contains("extracted.txt", StringComparison.Ordinal) &&
        File.Exists(Path.Combine(setup.Workspace,"extracted.txt")), "Real registered PDF export must reach the agent receipt and final report.");
}

// These tests inject observed OCR responses to test the adapter contract. The actual
// WinRT recognizer is exercised separately with the shipped image-only PDF in the CLI.
static OcrPageResult M3dResult(string text = "Sample text", int total = 1) =>
    new(true, "", text, "en-US", total, 1200, 800);

static Task M3dRegistered()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var registry = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    Assert(registry.TryGet("productivity.ocr_read", out var tool) && tool is not null &&
        tool.Descriptor.Risk == ToolRisk.ReadLocal && !tool.Descriptor.HasSideEffects &&
        !registry.Validate("productivity.ocr_read", "{\"path\":\"x.png\",\"command\":\"bad\"}", out _, out _) &&
        !registry.Validate("productivity.ocr_read", "{\"path\":\"x.png\",\"page\":\"1\"}", out _, out _),
        "OCR must be registered as a strict read-only tool, not a command runner.");
    return Task.CompletedTask;
}

static async Task M3dPage()
{
    using var setup = new TestContext();
    string file = M3cFixture(setup);
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace), (path, page, lang, ct) => {
        Assert(path == file && page == 2 && lang == "en-US", "Only selected source/page/language passed to recognizer.");
        return Task.FromResult(M3dResult("Page 2 text", 3));
    });
    var r = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path = "pdf-fixture.pdf", page = 2, languageTag = "en-US"}), default);
    Assert(r.Success && !r.ChangedState && r.Data.GetProperty("text").GetString() == "Page 2 text" &&
        r.Data.GetProperty("page").GetInt32() == 2 && r.Data.GetProperty("nextPage").GetInt32() == 3 &&
        r.Data.GetProperty("language").GetString() == "en-US", "Page OCR observation must be paginated and explicit.");
}

static async Task M3dUnchanged()
{
    using var setup = new TestContext();
    string path = M3cFixture(setup), hash = M3bHash(path);
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace), (_, _, _, _) => Task.FromResult(M3dResult()));
    var r = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path}), default);
    Assert(r.Success && !r.ChangedState && r.Data.GetProperty("ocrPerformed").GetBoolean() &&
        r.Data.GetProperty("sourceSha256").GetString() == hash && M3bHash(path) == hash &&
        r.Data.GetProperty("sourceUnchanged").GetBoolean(), "OCR source hash must remain unchanged.");
}

static async Task M3dBlank()
{
    using var setup = new TestContext();
    string path = M3cFixture(setup);
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace), (_, _, _, _) => Task.FromResult(M3dResult("")));
    var r = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path}), default);
    Assert(r.Success && r.Data.GetProperty("noRecognizedText").GetBoolean() &&
        r.Data.GetProperty("note").GetString()!.Contains("does not prove a blank page", StringComparison.Ordinal), "No text is not proof of a blank page.");
}

static async Task M3dInvalid()
{
    using var setup = new TestContext();
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace), (_, _, _, _) => throw new InvalidOperationException("Unexpected worker dispatch"));
    var a = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path = "x.exe"}), default);
    var b = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path = "x.png", page = 2}), default);
    var c = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path = "x.pdf", languageTag = "en;bad"}), default);
    Assert(a.ErrorCode == "OCR_FORMAT" && b.ErrorCode == "OCR_PAGE_RANGE" && c.ErrorCode == "OCR_LANGUAGE", "Reject bad inputs before worker launch.");
}

static async Task M3dUnavailable()
{
    using var setup = new TestContext();
    string path = M3cFixture(setup);
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace), (_, _, _, _) =>
        Task.FromResult(new OcrPageResult(false, "OCR_LANGUAGE_UNAVAILABLE", "", "", 0, 0, 0)));
    var r = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path}), default);
    Assert(!r.Success && !r.ChangedState && r.ErrorCode == "OCR_LANGUAGE_UNAVAILABLE", "Missing language cannot fabricate OCR.");
}

static async Task M3dDenied()
{
    using var setup = new TestContext();
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace), (_, _, _, _) => throw new InvalidOperationException("Unexpected worker dispatch"));
    string path = Path.Combine(setup.Root, "outside.pdf");
    var model = new FakeModel(new ModelReply[] { new(null, [Call("ocr-denied", "productivity.ocr_read", new {path})]) });
    var engine = new NiraEngine(model, new ToolRegistry([tool]), setup.Store, new WorkspaceAuthority(setup.Workspace));
    var approvals = new FakeApprovals(false);
    var r = await engine.RunAsync(Guid.NewGuid(), "Read this PDF with OCR.", approvals);
    Assert(r.Status == ConversationStatus.Paused && approvals.Calls == 1, "External OCR must require approval.");
}

static async Task M3dBounded()
{
    using var setup = new TestContext();
    string path = M3cFixture(setup);
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace), (_, _, _, _) => Task.FromResult(M3dResult(new string('x', 25000))));
    var r = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path}), default);
    Assert(r.Success && r.Data.GetProperty("truncated").GetBoolean() && r.Data.GetProperty("textCharacters").GetInt32() == 25000 &&
        r.Data.GetProperty("text").GetString()!.Length == 24000, "OCR observations must be bounded and disclose truncation.");
}

// Real backend acceptance: no mocked OCR text. Both fixtures contain the same raster record.
static Task M3d1RealPng() => M3d1RealRecognition("png", 
        "iVBORw0KGgoAAAANSUhEUgAABLAAAAMgCAIAAAC8ggxVAAB9WklEQVR4nO3daWAN5////5PIJiEROxVrxRaidqWWxtpQKvagEWtFq0VbS2srpaUo2qol9rWW" +
        "KEXRpnYh9p1IxL4myL7/b+T3nf98jmTO5OxxPR+3zpxcc837zJnDeZ2ZuS6brKwsDQAAAABAPLaWLgAAAAAAYBkEQgAAAAAQFIEQAAAAAARFIAQAAAAAQREI" +
        "AQAAAEBQBEIAAAAAEBSBEAAAAAAERSAEAAAAAEERCAEAAABAUARCAAAAABAUgRAAAAAABEUgBAAAAABBEQgBAAAAQFAEQgAAAAAQFIEQAAAAAARFIAQAAAAA" +
        "QREIAQAAAEBQBEIAAAAAEBSBEAAAAAAERSAEAAAAAEERCAEAAABAUARCAAAAABAUgRAAAAAABEUgBAAAAABBEQgBAAAAQFAEQgAAAAAQFIEQAAAAAARFIAQA" +
        "AAAAQREIAQAAAEBQBEIAAAAAEBSBEAAAAAAERSAEAAAAAEERCAEAAABAUARCAAAAABAUgRAAAAAABEUgBAAAAABBEQgBAAAAQFAEQgAAAAAQFIEQAAAAAARF" +
        "IAQAAAAAQREIAQAAAEBQBEIAAAAAEBSBEAAAAAAERSAEAAAAAEERCAEAAABAUARCAAAAABAUgRAAAAAABEUgBAAAAABBEQgBAAAAQFAEQgAAAAAQFIEQAAAA" +
        "AARFIAQAAAAAQREIAQAAAEBQBEIAAAAAEBSBEAAAAAAERSAEAAAAAEERCAEAAABAUARCAAAAABAUgRAAAAAABEUgBAAAAABBEQgBAAAAQFAEQgAAAAAQFIEQ" +
        "AAAAAARFIAQAAAAAQREIAQAAAEBQBEIAAAAAEBSBEAAAAAAERSAEAAAAAEERCAEAAABAUARCAAAAABAUgRAAAAAABEUgBAAAAABBEQgBAAAAQFAEQgAAAAAQ" +
        "FIEQAAAAAARFIAQAAAAAQREIAQAAAEBQBEIAAAAAEBSBEAAAAAAERSAEAAAAAEERCAEAAABAUARCAAAAABAUgRAAAAAABEUgBAAAAABBEQgBAAAAQFAEQgAA" +
        "AAAQFIEQAAAAAARFIAQAAAAAQREIAQAAAEBQBEIAAAAAEBSBEAAAAAAERSAEAAAAAEERCAEAAABAUARCAAAAABAUgRAAAAAABGVn6QIA5CdZWVnnz58PCws7" +
        "ffp0VFRUdHR0TExMYmJiSkqKk5NTwYIFixUrVr58+cqVK9evX79x48Z16tSxsbGxdNVAHmRmZp47d+7UqVNnz56Nioq6c+fOs2fPkpKSkpOTnZyc3NzcXF1d" +
        "S5Uq5eXlVadOnTp16jRo0MDOzpj/mfIpAwCYVRZy99133ynvvcaNGxvYQ7ZVq1blqbACBQoo9Obr6/v6KhMmTFCuoV69enmqIUerVq1S3krRokVTUlLkq6jc" +
        "RXpwcXFRrtZK3h1fX18jveK8GTRoUJ5e18mTJ4cPH16mTJk8baVs2bKffPJJeHh4nralRb+DxM7OztnZuXTp0jVq1Gjbtu2wYcN++eWXU6dOZWRkKG9u3759" +
        "Ojt3dXW9d++efi+nT58+OvvP8VOsNzPvQFMUo4bOj7yyzMzMvXv3DhgwoHjx4nnabpEiRfr06bNu3bq4uDhDCsgS6VNmeCW2trb29vYuLi5Fixb18PCoWbNm" +
        "s2bNunbtOnz48BkzZmzatOn8+fNpaWmG7BMAEASBUInZAmHFihW1YpIyPQLhtWvXdJZx9erVvO2g17Rr1055E5988onWKtYfCE397lh/IDxw4EDTpk0N3Fyz" +
        "Zs1CQ0PV70Y54x4kxYoVGzp06JkzZxS2GBgYqLOfrl276vFa9uzZo7NnNzc3vdNmjsy/A81WjJzegTAtLW3x4sVvv/22gQW4ubmNHj06MjJSjxoE/JSZoRIn" +
        "J6eWLVv+8MMP0dHR+u0WABAB9xBahdu3by9ZssSkm6hWrVqjRo2U26xdu9aQTTx+/Piff/5RbjNgwABDNmERZnh3rNajR4+6du3apk2b48ePG9jV0aNHW7du" +
        "7efn9/TpU6PUprfnz58vWbKkXr16fn5+9+7dy7HN3Llzy5Ytq9xPSEjItm3b8rTphISETz75RGezOXPmvPXWW3nq2ZzU7MB85MCBA15eXsOHD4+IiDCwq5cv" +
        "X86dO/ftt9++dOmS+rWE/ZSZQXJy8sGDB7/++uuKFSu+//77+/fvt1QlAGDNCITWYvr06QkJCSbdxMcff6zcYP369Yb0v3HjxoyMDIUGnp6eTZo0MWQTlmKG" +
        "d8cK7d+/39vbe8eOHUbsc9u2bXXq1AkNDTVin3rbtm1bo0aNcvzu7ubm9ttvv+nsYeTIkS9fvlS/xW+//fb27dvKbXx8fAYPHqy+TwtS2IH5Qmpq6qhRo9q1" +
        "a3f9+nUjdpuZmZmcnKyyscifMnPKysoKDQ1t165ds2bNLl68aNliAMDaEAitxePHj3/++WeTbqJ3794ODg4KDaKioo4ePap3/zpPMObH04PZzPDuWJs1a9Z8" +
        "8MEHT548MXrPjx496tChw6ZNm4zesx4ePnzYokWLK1euvP6nDz/8UOfNfg8fPvz6669Vbis8PHzBggXKbVxcXJYuXaqyQ2ugsAOtXGxsbLt27RYsWJCVlWWp" +
        "GviUmd+xY8caNGgwY8YMC77vAGBtCIRW5Mcff4yNjTVd/0WLFtV5u9q6dev06/zGjRvh4eEKDWxsbPr166df59bA1O+OVVmzZs3HH3+cnp5uov5TU1P79u37" +
        "xx9/mKj/PImNjR06dGiO3w4XLFhQokQJ5dWXLFly5MgRnVtJT08fMmSI8il0jUbz/fffV6pUSWdvVkVhB1qtmJiYFi1aHDx40II18CmzlNTU1G+++aZnz57q" +
        "T+QCwJuNQGhFXr58+cMPP5h0EzrP0W3evDktLU2PnnUmyZYtW1aoUEGPnq2EGd4dKxEaGpo93oxJt5KZmTlgwIBjx46ZdCsqHT16NMfrpYsXL75w4ULldbOy" +
        "soYMGZKamqrc7Keffjp37pxym2bNmo0cOVK5jXXKbQdap4SEhA8++MCyFzHyKbO4LVu2tGvXLjEx0dKFAIDlEQity4IFCx4+fGi6/n19fZVHVH/+/PnevXv1" +
        "6Fnn//Q672C0fqZ+d6zB8+fP/f39Vf4o0KhRo7lz54aHh9+/fz8lJeX+/fvh4eFz587VOXxRtuTk5D59+uTpHjzTye0XjV69enXt2lV53WvXrn3//fcKDSIj" +
        "I6dOnarciZOT0/Lly21t8+u/yXpfXGB+I0aMCAsLU9m4VKlSAQEBmzdvPnv27IMHD1JTU2NiYm7evHnixIlly5YNHDiwatWqeS2AT5mVOHz4cLdu3fT7DRQA" +
        "3igWG980PzDbtBNyr0/M8Do9pp2Q6DwF0bNnz7ztpqwsnYPjOTs7v3r1Sr9dpHMn680K3x1lasZCuHv3rt79Z6mbIk+j0VSoUGH//v0K/fzzzz8VK1ZU01Vg" +
        "YKDOqvJ6kGRmZsbFxUVERGzdurVPnz5qUpajo2Nuk8g9ePDA3d1deXUHB4fLly/nVn+bNm10FjBr1iyd+0Fvlt2BBhZjXOozSYkSJX777bf09HSdfd64cePb" +
        "b78tWbKktO6pU6cU2vMpM24lcXFxd+7cOXfu3LZt27744otGjRrl6YeV0aNH6ywJAN5sBEIlFgmE9vb2ERERyt0aEjlOnjypXEDBggVzC2+50Rky/f399d5F" +
        "VhUITf3uKDN1IFR52qRZs2ZqjpC4uLj33ntPZ2+2trbnz59X7srAg+TgwYPOzs46Kzl27FhuPaxYsULNbsnMzHx93ZUrV+pct379+mqCh94svgONWIwhXr16" +
        "Vbp0aZ0vRKPRdO7c+eXLl3nqPDk5eeHChdmxUCEQ8inT+aIMP0KuXbs2ZMgQR0dHnfVk27Nnj86qAOANll8vT3qDpaWlTZ482XT9N2zYsEaNGgoNkpKStm7d" +
        "qr7D9PT0zZs3K7d5A64XzWbqd8eyvvnmG51tatWqtXPnzsKFC+tsWahQoT///LN27drKzTIzMydNmqS2RL20aNFiypQpOpspDPYYEBDQvn175dWPHj26ePFi" +
        "rSefPn06ZswY5RXt7e2Dg4OVf0ewLMN3oJWYNWvWo0ePdDbr37//tm3bXF1d89S5o6PjyJEjIyIiPvvsMzs7u9ya8SkzaRnZqlWrtmTJkgsXLnh5ealp/+mn" +
        "n+q8DRgA3mAEQmu0YcMGk06UpHNomTzd6bF//37l/+PfeustHx8f9R1aOVO/O5Zy48aNAwcOKLextbVdt26dzusnJUWKFFm3bp3OqLNz587o6GiVfeqnf//+" +
        "Ots8fvxY4a9LlizR+QV93LhxDx48kD/z+eefP3/+XHmtCRMm1KlTR2d5lmX4DrS4hIQENXNLtmjRYuXKlQqJTlnhwoV//vnnunXr5vhXPmXmPEg8PT3DwsI+" +
        "+ugjnS0jIiJe/zUHAMRBILRGmZmZEydONF3//fr1U77F4t9//9X6XqtA5/SD/v7++XeojNeZ+t2xlGXLlmXpGvNw0KBB3t7eeeq2du3aOqdZz8zMDA4OzlO3" +
        "eVW6dGmdE0goK1++/KxZs5TbvHr1KigoSFr8+++/dQ625OXlNWHCBEMKMw/Dd6DFrV27VufMMa6urqtWrTLdv1d8yszM2dl53bp177zzjs6W8+bN0zkrDAC8" +
        "qd6cr+n5lI2NTY7P79y5U+dILXorV65c69atFRpkZmZu3LhRTVcJCQk7duxQbpN/56O3yLtjKTrfRxsbG/2S8MSJE3Pbk+q3bjidVwDqPCfzySeftGzZUrlN" +
        "SEjI9u3bNRpNYmLi8OHDlRsXKFBgxYoVDg4Oys2shOE70LLUzNI+fvx4lcO06IdPmfkPkoIFC27fvt3FxUW52e3bt/ft22eekgDA2hAILUxhuLnx48ebbrs6" +
        "b+rTed4vW0hISEJCgkKD+vXr16pVKw+VWRNLvTvmFxERcePGDeU2TZs21W8mSQ8Pj2bNmim3OX/+/P379/XoXD2dI++//fbbyg1sbGyWLVtWsGBB5WYjR458" +
        "+fLlt99+e/v2beWWY8aMadCggXIb62H4DrSgmJiYQ4cOKbdxcnIaMmSI6WrgU6ax0EFSoUKFYcOG6Wy2ZcsWMxQDAFaIQGhhI0aM8PDwyPFPBw8e/Pvvv020" +
        "3W7duhUqVEihwdmzZ69evaqzH513G+bf04May7075nfixAmdbXr27Kl3/7169dLZRv3UcHq4f//+s2fPFBo4OzvrHJlDo9G8/fbbOodAfPDgQc+ePX/++Wfl" +
        "Zp6enjonJ7QextqBlnLs2DGdFwT26tWrWLFipquBT5kFD5IxY8boHHR09+7d5ikGAKwNgdDCHB0dFUatnDBhgs4bTvTj4uLSrVs35TY6TxI+efJk//79Cg3s" +
        "7e1VzrhlnSz17pjf6dOndbZp0qSJ3v03btxYZ5vw8HC9+9dp1apVyg0++ugjleOIfPHFFzpfzr59+5Tjh42NzfLly52cnNRs0RoYcQdahJog1K5dO5PWwKfM" +
        "ggdJ2bJldc7P8ejRI1OPuwMA1olAaHkBAQHVqlXL8U9nzpwx3UUsOs/drV+/XjnwbNq0KT09XaFBhw4dDB9jICwszCbvchvlL68s9e6Y2fXr15Ub2NjYqBzA" +
        "PUdeXl46B+rQeTWd3kJDQ6dPn67cRj4YjDJbW9vg4GADb/wLCgpq3ry5IT2Yk3F3oE6m+MhfunRJ53abNm1qrJeQIz5lRjxI9NCiRQudbc6cOWOGSgDA2hAI" +
        "La9AgQLTpk3L7a/ffvutiYY+a926dW7XQ2a7ffv20aNHFRrovF70DZh+0FLvjpndvXtXuUGlSpV0jsqgoGDBgpUrVzawBvWysrISEhIiIyO3bdvWp0+fNm3a" +
        "JCUlKbQPCAjIUxioWbPmt99+q3d5FStWnDlzpt6rm4Gpd6D56TzzU7x48UqVKpm0Bj5llj1IdJ4h1Gg0UVFRZqgEAKwNgdAq9OjRo169ejn+6fr16ytXrjTF" +
        "Rm1tbfv166fcRiHy3bp1S/kqLHd3906dOulZnDWxyLtjZg8fPlRuULx4cQM3obMH9TOdvE7rnJKtrW2hQoWqVKni5+e3cePGzMxMhXUbNmy4cOHCvG5x3Lhx" +
        "ep+FXrJkifIdvOZn/h1oZjoHUylbtqypa+BTpvemjULNaD1GDMwAkI8QCK2CjY3NjBkzcvvr1KlTU1JSTLFdnVeNbt68OS0tLcc/6bzDsFevXjpv4s8XLPXu" +
        "mJPyULEaFaPJ66Szh8TERAM3oQcfH589e/boEc/s7OyCg4P1uCFq0KBBbdu2zetaVkvvHWhmcXFxyg3MMB0CnzLzb1quaNGiOtu8evXKDJUAgLUhEFqLDh06" +
        "5HaHw927d3/99VdTbLR69eoNGzZUaBATE7Nnz54c/6Rzxu034HpRiUXeHXPSmWkLFy5s4CZ0flVVvt7M6Nzd3efMmbNv3z69B5Z85513vvrqqzytUrZs2Z9+" +
        "+km/zVkbw3egOSUnJys3KFKkiKlr4FNmWW5ubjp/wbFIYAYAiyMQWhGF24q+//57nb9w60e/CQlPnTqlPDhB1apVDRkuzwpZ5N15k9jomjXbbEO2enl5zZ49" +
        "OzIycsyYMToH4VA2adKkGjVqqG+/ePFiNzc3Q7ZoDYy4A62HzuMzX3gjP2XGkpWVpfPlvxmHAQDklVX8M41s7777bm433T179mzu3Lmm2Gjv3r2Vx0vcuXPn" +
        "61fRvNnTD+bIIu+O2ei8uNfwxKtzxmpnZ2cDN6FSampqRkaGvb294V05OjoGBwer/L7bt2/fzp07G75RizPiDjQbnTN8xMbGmroGPmWW9eLFC51jgJlt/wCA" +
        "VSEQWpcZM2bk9uXyp59+Up7zVz/FihX74IMPFBokJydv3bpV/kxGRsbGjRsVVrGxsenfv79x6rMm5n93zEbn1yDDv6rq7MFsX8Vu3Lgxbty42rVrG2VOtiZN" +
        "mnz22Wc6m5UoUULnVPX5hXF3oHnovIHtxYsXpq6BT5llxcTE6Gxj+G2cAJAfEQitS506dXr37p3jn+Li4kw0VL3Oq0a1zgceOHDg8ePHCu1btmypZjw3lRo3" +
        "bpyVd+fOnTNWARKLvDvmUaZMGeUGhsddnT3orMG4oqKiWrdurTyxikozZszQOVvAN998Y/gYklbFiDtQiyk+8m+99ZbyRg0ZflMlPmWWdfv2bZ1typcvb/pC" +
        "AMDqEAitzrRp03K7zObXX3+9d++e0bfo6+urfMd/aGio/NuSgNeLSsz/7piH8oyUGo0mKipK5xiJCpKSkiIjIw2sweji4+O7du16584dA/txdnbWeUWimhEO" +
        "8x1j7UAzqFixonKDp0+fqgkMhuBTZlmHDh3S2cbUc1ECgHUiEFqdKlWqDBo0KMc/JScnK0ySrjd7e/vcTnxly8zM3LBhQ/bjxMTE7du3KzR2dnbu3r27Meuz" +
        "JuZ/d8yjWrVqyg0yMzMvXbqkd/+XL1/WefeOzhoUyM8pJSUlRUVFbdy4Uc3sDs+ePRswYIDZRtqwWm/8DqxVq5bONsePHzdpDXzK9N60UagJhLlNOQsAbzYC" +
        "oTX69ttvCxYsmOOfVqxYoTy8p350ntOTxhrdsWNHfHy8QsuuXbsaPni6NTP/u2MG9evX19nmxIkTevcfFhZmlBrUcHJyqlixYq9evfbt27d27VrlMZM0Gs3B" +
        "gweDg4ONsuk3wxu5Axs3bqyzzb59+0xaA58yo2xaP7dv3z5y5Ihym7Jly5r/DCoAWAMCoTUqW7bsp59+muOf0tPTJ02aZPQtNmrUSHn0/HPnzl25ckWj4nrR" +
        "N2n6wRyZ/90xAzVzhGzevFnv/jdt2qSzjZqv7Hnl7++/ZMkSnc3Gjx/PxCE5emN24LvvvlugQAHlNps2bVIz7oje+JRZ8CCZPXt2enq6chvl8dUA4A1GILRS" +
        "X3/9dW7zlW3evFnndUF60Dku6Nq1a589e6b8I3rZsmXbtGlj1LqskfnfHVOrWrVq1apVldscP348Ojpaj87v3bun87f5OnXqlCtXTo/Odfr444+Vr4jWaDRP" +
        "nz6dP3++Kbb+BngzdmCxYsXee+895TZJSUnLli0zXQ18yix1kNy4cUPN+Uk/Pz8zFAMAVohAaKWKFi365Zdf5vgnE92J0b9/f+Xp1NavX79p06a0tDSFNv7+" +
        "/lYyB7FJmf/dMYMuXbooN8jKyvr+++/16Pn777/XuVt0bt0Q8+bN0zkK6Ny5c1+fbxPZ3owd2KtXL51tvv/+e5MOf8KnzPwHSXx8/EcffZScnKzcrFKlSu3a" +
        "tTNPSQBgbd787+751+eff16qVCmzba5cuXKtWrVSaBAdHT116lTlTt7g8UW1mPndMYPcBsuRW7Zs2fnz5/PU7aVLl3ReTmZraxsYGJinbvOkdOnSOqcKfPHi" +
        "xS+//GK6GvK1N2MH9u/fv0iRIsptXr58GRAQYLqfdfiUmfkgiYuL69GjR/b9Dsq++OILEX7NBIAc8c+f9XJxcZk4caI5t6jz9r+nT58q/LVevXpeXl5Grch6" +
        "mf/dMbXq1avrvNw3MzPT398/NjZWZZ8vXrzo27evzmtofX19dc4KYKAxY8boPH0xb968xMREk5aRf70BO9DFxeWTTz7R2Sw0NHTQoEF6X/gdFxf3xRdf5DYp" +
        "Ip8ycx4kly9fbtiw4d69e3W2fPvtt4cNG2aGkgDAOhEIrdqwYcNM/V+4nJ+fn87/zhWIc3owm5nfHTP47rvvdLa5fPly586d1QwOER8f/+GHH168eFG5mY2N" +
        "jRmm6yhWrNjQoUOV2zx9+nTp0qWmriSfejN24Lhx49Sc2F+xYkX37t3zOgJKSkrKL7/8UrVq1fnz5yuMX8KnzAwHyZUrVwYOHFivXr3r16+rab9w4UKdA6UC" +
        "wBuMQGjVHBwcpkyZYrbNubi4dOvWTb917ezs+vTpY9x6rJyZ3x0zaNKkiZr7rI4ePVq7du1//vlHoc2///5bu3btw4cP6+zt448/rlu3rvoi9TZ27Fid3/nm" +
        "zJmTmppqhmLyozdgB7q6us6ZM0dNy5CQkKpVqy5dulTNqcKoqKipU6dWrFhx5MiRjx8/Vm7Mp8zoB0lCQsLdu3cvXLiwbdu20aNHN2zY0MvLa+XKlSq3Mnr0" +
        "6A4dOhixHgDIdwiE1q5///41a9Y02+b0njSiY8eOJUuWNG4x2cLCwmz0NXz4cFOUJDHzu2MGixYtUnMKJTo6uk2bNk2aNJk/f/7p06cfPnyYmpr68OHD06dP" +
        "z58/v0mTJj4+Prdv39bZj4eHx7x584xQtwply5YNCAhQbnPv3r1Vq1aZpZz8x2w70KQf+X79+vXr109NGY8fPx46dGi5cuUGDRq0devWCxcuPHr0KC0t7cWL" +
        "F5GRkadOnVq5cuXQoUNr1apVuXLlKVOmPHr0SOUL5FNm4EGidYQUKlSofPny3t7efn5+8+bNCw8PV38XaPv27WfNmmVIMQDwBrCzdAHQwdbWdvr06XqfuMur" +
        "1q1be3h43L17N68rina9aDYzvztmULx48XXr1nXo0EHnnF0ajSYsLEzNXNi5cXR0XL9+vc5xPozo66+/Xr58ufI5nx9++CEwMFDnnHViejN24G+//Xb9+vVT" +
        "p06pafzo0aPg4GDjTqrOp8xKDpLmzZtv27bN3t7esmUAgMVxhjAf+Oijjxo1amSebdna2vr7++d1LXd3986dO5uiHutnznfHPHx8fMxwk4+Njc3KlSubN29u" +
        "6g3JVa5cWefVerdu3dq4caN56sl33owdWKhQoT179lj23D6fMosfJD169Ni/f7+zs7NlywAAa0AgzB/0m5lKP3pcNdqzZ09HR0dTFJMvmPPdMY+AgIDg4GDT" +
        "/X5vb2+/du1anTNZm8L48eNtbGyU28ycOTP/zidpam/GDixWrNjhw4fNnJS08Cmz1EHi4ODw/fffb9q0ycnJySIFAIC1IRDmDz4+Pj4+PubZVvXq1Rs2bJin" +
        "VfS+8/DNYM53x2wGDhy4a9eu4sWLG73nkiVL7t69u2/fvkbvWQ0vL68PP/xQuc3ly5dDQkLMUk7+88bswKJFix44cGDEiBEWrIFPmVnK+R/vvffemTNn1ERW" +
        "ABAHgTDfMOdpqDzdEFi1atWmTZuarph84c07SajRaDp06HD+/HlfX18j9tmlS5cLFy7onIrNpCZMmKCzzRv5hhrLG7MDHR0df/nll71791atWtWI3dra2qo/" +
        "9cSnzDxsbGzef//9AwcOHDp0qFatWmbbLgDkCwTCfKNRo0YfffSRebbVp08f9ffZ9+/f36TF5AvmfHfMqWzZsrt27dq7d6/h90k2adLkwIEDISEhasZXNKlG" +
        "jRrpPKMbHh7+999/m6eefOcN24Ht27e/fPnyL7/8UrlyZQO7cnV1/fzzzyMiIry8vNSvxafMdJycnFq0aPHjjz9GR0f/888/b96lHABgFATC/GT69Om2tuZ4" +
        "y4oVK6byF2sbGxsCYTazvTvm1759+7CwsOPHjw8dOjSvXzRLly49bNiwkydPHj9+3Hq+jU2cOFFnmxkzZpihknzqDduB9vb2I0aMuHnz5u7du/v161e0aNE8" +
        "re7q6tqjR481a9bcv39/3rx5lSpV0qMGPmV5ZWNjY2dnV7BgQXd393LlytWoUaNZs2ZdunQZPnz4jBkzNm7ceP78+bi4uIMHD3755ZceHh56bwgA3ng2Vn7r" +
        "PwCrkpmZefbs2ZMnT545cyYyMvLOnTvPnz9PSkpKSUlxdHR0dnYuVqyYh4dH5cqV69ev37hx47p163KvDvKXjIyMs2fPnjp16uzZs1FRUdJBnpyc7OTk5Orq" +
        "6ubmVrJkyVq1atWuXbtOnTqNGzc27tQFfMoAAOZEIAQAAAAAQb2ZV7gBAAAAAHQiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAA" +
        "AAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAI" +
        "hAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAA" +
        "gKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIys7SBVgXGxsbS5cAAAAAwLSysrIsXYK14AwhAAAAAAiKQAgAAAAAgrLh" +
        "bCkAAAAAiIkzhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAA" +
        "AACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpA" +
        "CAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAA" +
        "CIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEA" +
        "AAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAo" +
        "AiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikCIN9bFixdnz5790UcfeXl5ubu7" +
        "Ozg4ODo6Fi9evF69en369Pn111+jo6MN3ER6evoff/zx8ccfe3l5FS9e3N7e3ub/1K1b9/X2mZmZu3btGjRokLe3t7u7u7y9nZ2d1GzRokXS86VLlzawSJH1" +
        "69dP2pOdOnWydDmqzJo1S6q5evXqli4HAAC84ex0NxHV3r17O3bsKC3Onj177NixFqwHKmVmZm7atOnHH388d+7c6399/vz58+fPz549u3HjxpEjR7Zq1Wri" +
        "xIk+Pj56bOjEiRP+/v6RkZEq29+8ebN3795nzpzRY1sAAACAKXCGEG+US5cuNWnSpG/fvjmmQS1ZWVmhoaFt2rTp1q3bo0eP8rShs2fPvv/+++rT4OPHj1u3" +
        "bk0aBAAAgFUhEBrB2LFjpUu8GjRoYOlyxLVly5bGjRufOnUqrytu3769fv36eVpx8ODBSUlJ6tt/+eWX9+/fz2thAAAAgEkRCPGGWLNmTa9evRITE+VPlilT" +
        "ZuTIkfv27btx40Z8fPyLFy+uXLmyY8eOAQMGuLm5yVs+ePDAx8fnxIkTarZ17Ngx+bm+d955Z+fOnU+ePElPT8/6P/JTlM+ePdu4caO0WLZs2VWrVt29ezc1" +
        "NVVqn56ert8LBwAAAPTGPYR4E/z7778DBw7MzMyUnnFycvrmm2+++uore3t7eUs3N7caNWp8+OGHr169+uqrr5YsWZKVlZX9p7i4OF9f39OnT1esWFF5c6Gh" +
        "odLjQoUK7d+/v1ixYgrtjx49mpaWJi1u3769UaNGCu1Hjhw5cuRI5Rrwpho3bty4ceMsXQUAABAFZwiR7z158qRXr14ZGRnSMyVKlDh58uTEiRO10qCcq6vr" +
        "4sWLQ0JC5G1iYmJ69Ogh7ypHly5dkh6///77ymlQq32lSpWU0yAAAABgNgRC5HujR49+9uyZtOju7n7gwIHatWurWffDDz9cv359gQIFpGfCw8MXLFigvFZM" +
        "TIz0uFy5cjq3Im/v4eGhpjAAAADADAiEyN/Onz+/fv16+TO//vprnTp11PfQvXv3zz77TP7M9OnT4+LiFFaR36mocBJS7/YAAACAeXAPoXW5e/fu7t279+7d" +
        "GxER8eTJk9jY2CJFipQoUaJ27dodOnTo3LmzzqsTlaWlpW3evHn79u1nzpx59OhRZmZmyZIlGzZs6O/v37VrV1vbHH4gSE1N3bJli7RKVlZW6dKl69Wr16NH" +
        "jx49euS4ijn9+OOP0k2AGo2mffv2vXv3zmsn06ZN27Jly927d7MXY2Jili5dOnr06Nzay7eoRl7b6+3hw4f79u3bv3//5cuXnz179vTp0wIFCri7u1erVq1+" +
        "/fqdOnVq3ry5+rfM1EejSsZ9UdmysrJ27dq1adOmkydP3r9/PyMjo0SJElWqVGnfvr2/v3/58uXz1FtaWtqxY8f+/fffCxcuXL169dmzZ69evXJycipatGjZ" +
        "smWbNm3aqlWrjh072tmZ6d/b9PT0HTt27Nix48yZM3fv3o2Pj8/MzPT19d21a5dWy+zhjo4ePXr+/PmnT5++evXK1tbW1dW1dOnStWrVql+/fufOnT09Pc1T" +
        "NgAAsIws5GLPnj3yHTV79mytBr6+vnna1X5+fgqbu337dr9+/WxsbBR6KFSo0JQpU+Lj43UW7+/vL63l6+ub/eSBAwcqVKiQW+dNmza9ffu2Vj+7d+9WGGGl" +
        "YcOG169fz8tONbLY2FgnJyd5SWFhYfp1pXWZaK1atbQavPXWWzrfYlMoVaqUmvpv374dGBgov/Y1R+XKlZs7d25CQoLO3ox4NOrN8BeV4wfh7NmzdevWza03" +
        "JyenKVOmZGZm6iwvMzPz33//HTBgQOHChZUr1Gg0lSpVWrp0qZpXPXPmTGmtatWqKbTM8dXt27evUqVKrxfQsWNH+bovX74cMWKEmvPV3t7ea9euTUtLU1M8" +
        "AADIdwiEuTJnIFy9erWjo6PKfry9ve/du6dc/OvfFIODg3V+sS5btmx0dLTUyZw5c5QjQfYqt27dUi5mxYoV8lWCgoJ07HrV1q5dK+/59RSn3vPnz7XeAq2s" +
        "a82BMDg42MHBQX2fCxcuVOjN6Eejfozyol7/IOzYsUPrR4QcffzxxxkZGcoVnj17Vn152Xr37p2YmKjcrSGBcNGiRbl9Ztu3by+tGB0dXbly5TxVvn//fuWy" +
        "AQBAPsU9hJY3a9asAQMGpKSkqGx//vz5pk2bPnr0SP0m/v777yFDhugcPPPBgwc9e/bMysrSaDTLly8fO3Zslq5rHR88eNC3b1+dzUxk//798sX+/fvr3VXR" +
        "okU7duwof2bfvn1692ZOEydODAwMTE1NNUpvZjga1TDui5IcOXKkR48eycnJOluuWrXqt99+M+7WNRrNxo0bBw0aZPRus+3YsePTTz/N7cMoTcqSkpLSvn37" +
        "yMhIE5UBAADyFwKhhW3evHn8+PHyZ7y9vefPnx8eHv7s2bPU1NRnz54dPXp0/Pjx8onU7969279/f/m0ewpiY2MDAgKy02D9+vWXLl0aERGRmJj48uXLkydP" +
        "jhw5Un7mMCwsbN26dZcvX5bmwWvYsOGyZcsiIiISEhJevXp18uTJESNGaK2yZs0aQ3aC3k6ePClfbNq0qSG9aa0eHh5uSG/msXTp0u+//17+jKOj48CBA7ds" +
        "2RIZGRkXF5eUlPTgwYP//vtv9uzZLVq0UL7XzgxHo/lflOTFixd9+vTJDplVqlSZO3fuhQsXYmNjk5OTIyMjlyxZonXSbPz48fLhYRWULFkyICDg999/P3r0" +
        "6P379+Pi4tLS0mJiYs6cObN48eL33ntP3njDhg2rV69W022exMTEDBkyJDsNVqhQ4Ycffjh79uzz58+lH/+kHzgWLFhw7do1aUVnZ+egoKDdu3dHR0cnJCRk" +
        "V37jxo2tW7eOHz++Zs2aRi8VAABYF8ucmMwPdF4yKhkzZozUrH79+uo3ERUVJf9i7erqunr16twaP3nypHXr1vKSli9fnltj+bVk2WxsbH788ccc74z6888/" +
        "5QGvUaNGLVq00Gg0tra28+bNy3GVLVu2yL+FN2vWTOFlmuiS0ZSUFHnZtra2cXFxhnSodb6xXr16ubVs1qyZ1GzUqFE6ex42bJjU3sfHR2f7hQsXSu0VLhm9" +
        "cOGC1tWPbdu2ff1GULk7d+6MGjUqODj49T+Z7mjME+O+qNc/CBqN5quvvkpNTX29cUJCQqtWreQtla+tPX/+vK+v7/79+3VeXLpz5075vvX09FS4R1G/S0Yl" +
        "Q4cOVb4q1cvLS2pcuXLlyMhI5eLPnz/v7+//33//KTcDAAD5FIEwV2YIhPLvcy4uLsePH1dun5SU1KBBA2mVmjVr5va18vVvirNmzVLoWZ5YJPPnz1dYJSAg" +
        "QN747t27ubU0USDUuuatYsWKBnb48OFDeYfFixfPraWVBMLOnTvLC+7Zs2eOOUcl0x2NeWK6F5VtwoQJCu0fPnwoT27Kv3TkyeHDh+W/ofz999+5tTQkEH78" +
        "8cfKZcTGxsrb7969W7+XAwAA3hhcMmox9+/f37x5s7Q4c+bMJk2aKK/i5OS0bNkyadCIK1euHD58WM22GjZs+OWXXyo0+OSTT7SeadasmdbsfFqCgoLki+a/" +
        "wFLrvrWiRYsa2KG7u7t88dmzZ2lpaQb2aTpXr16VzyLw9ttvBwcH6z3JoTmPRgXGfVGv8/b2njZtmkKD0qVLy+9EPXv2rLEuhW3evHn37t2lxQMHDhilW7ky" +
        "ZcpoDZb7OvmnxsbGxsfHx+hlAACA/IVAaDHBwcFS3ihVqtTrkSxH3t7eLVu2lBYPHjyoZq3Ro0cr32fl7e1dokQJrVWUhxht0KCBPEGdP38+t5YBAQHyHyEW" +
        "LVqkpmadEhIS5IvyEzv6cXR01BpdMykpycA+TWfFihVZsuFDfvjhBxcXF717M+fRqMC4L+p1Y8eO1TnWbqdOnaTHiYmJN2/eNNbW5aMWhYWFGatbyZAhQ1xd" +
        "XZXbaI0sZcQ7PwEAQD5FILSY0NBQ6bGfn5/6Savl924dPXpUZ3tbW1s1M2RUr15demxnZ9ehQ4c8rfL48WOd7Y1LawhKoySHQoUKyRfVD7ZpfvL0Va5cuS5d" +
        "uhjSm9mORmXGfVFabG1tta5HzVHt2rXli0+ePDFWAfKZS+TDuhhLr169dLYpU6aM9DgrK+uPP/4wehkAACB/Ufu1D8aVlpYmP0VQv3599euWL19eehwdHa2z" +
        "vaenp5qJs+WXXNaoUcPZ2TlPq7x69Upne+PSmqRO64ShfrQ6UT8dn5klJCScOXNGWmzTpo3OE18KzHk0KjDui3qdp6enmtPIWqfKX758qabzly9fhoSEnDx5" +
        "8vz583fv3n316lVcXJzCRC8vXrxQ0616hQsXlv9Ak5uiRYvWqFHj6tWr2YuffPJJenr6gAEDjLurAQBAPkIgtIzIyMjExERpcciQIUOHDpUWpavm5JfPZeU0" +
        "vZiaYfFLliyppiT5GTY9VjFKHssTrciq8ou7gtTUVK0Z6goWLGhgnyZy+/bt9PR0abFx48aG9GbOo1GBcV/U61Qe1fb29g4ODtL5Z50H9qNHj8aNG7dp0yY1" +
        "0xtKUlNTk5KSjHiAvf322yqn3xg2bNjnn3+e/TghISEwMHDSpEm9evX64IMPmjZtarXHPAAAMBEuGbWM58+fyxczMzMzZDL/j/zWuxz7UXOeQc3pQY1GI79j" +
        "UOvKSTWr5Fah6ZQuXVq+qDV8oh608kyxYsWMOJyJcWmVWq5cOUN6M+fRqMC4L+p1Kj8Imrwc2Lt27apevfqqVavylAazGfeCZPX30I4YMeLdd9+VP3Pv3r2f" +
        "fvrJx8enSJEiTZs2HTt27O7du+Pj441YHgAAsFoEQsswPL1kU7gm7Y331ltvyU+JREdHG/gV9vLly/JF+cWQ1kYrOxUpUsSQ3qzkaDTuizKD/fv3+/n5GX5q" +
        "2ijU30Nrb2+/e/fuHO8rTk1NPXHixE8//eTr61uiRImePXv+999/xqwSAABYHwKhZVjzaCX5haOjY9WqVaXFzMzMs2fPGtLh6dOn5Ytag4tYM+XxYHWyzqPR" +
        "wBdlaklJSYMHD5aPbOTo6NizZ89FixYdPHgwIiLixYsXSUlJ8tOq8pF7LMvNzW3Xrl07duxo3rx5bm2Sk5P/+OOP1q1bt2vX7v79++YsDwAAmBOB0DK0Js3T" +
        "OQl4buT3XAmoUaNG8sXjx48b0tuJEyfki/JZ162N1vFj4Ck+KzkajfuiTG3ZsmV37tyRFtu1axcdHb1p06agoKAWLVpUqVLFzc3NyclJvor577NV9uGHHx4+" +
        "fDgiIuKXX37x8/PL7R7L/fv3169fPyoqyszlAQAA8yAQWkbx4sXli3zZ0k/btm3li2vXrtW7q9jY2N27d8ufadeund69mZpWdrp3754hvVnJ0WjcF2VqO3bs" +
        "kB5XrVo1JCSkVKlSyqtYZ8StUqXKiBEjtmzZ8vjx4+vXr69YsaJv377yKUY1Gs3jx4/9/f0tVSEAADApAqFllC9fXn7/26lTpyxYTP7l6+srnxni4sWL4eHh" +
        "+nW1fv16+ZWTNWvWrFatmqH1mUylSpXkA94YOMu5lRyNxn1RpiY/nzx8+HA1g3Nq3aRqhTw9PQMCAtatW/fgwYPff/9dPlDN8ePHjxw5YsHaAACAiRAIjUA+" +
        "i3dmZqaaVVxdXd955x1pcdeuXcYvSwBFixbt2rWr/JlJkybp0U9CQsKPP/4ofyYwMNCQwkzN2dm5Xr160uKBAwcMGdDFSo5G474ok0pMTJRf/+nt7a1mrYMH" +
        "D5qsIiNzcnIaOnTo9u3b5U/u37/fUvUAAADTIRAagXySBvW3Cfn4+EiPb968uWfPHiOXJYavvvpKvrhnz57NmzfntZPJkyfL7wcrWrSofCI+69SqVSvp8b17" +
        "9+RXMOrBSo5G474o09Eaz1Z+mjo3ly5dMvAeV/Nr3bp1lSpVpMW7d+9asBgAAGAiBEIjkI+Pf+fOHZWnNQYOHCi/Tm/cuHFJSUlGr+2NV69evV69esmfGTFi" +
        "xKVLl9T3EBISMn/+fPkzEydOVD9nnaUEBgbKj5+vv/7akDFLrORoNO6LMh2tW+yuX7+uc5UJEyaYrBwTcnV1lR5b7QlbAABgCAKhEdSsWVN6nJycvG/fPjVr" +
        "Va9e3c/PT1q8cOFCQEDAGzlq6MqVK21kRo4cadz+58+fLx+P5Pnz523atLly5Yqadf/6669evXrJv+k2aNBg1KhRxq3QFDw9PTt37iwtRkREBAYGpqWl6deb" +
        "lRyNxn1RpmNvb1+hQgVpcenSpcrz18+fP3/nzp2mr8vInj17dvXqVWmxXLlyFiwGAACYCIHQCOrVqycfDCMwMHD58uVRUVE6p3ebPn26/EzU5s2bW7duHRER" +
        "oXOLjx8/nj9/vtakC8IqXbr0xo0bCxQoID3z+PHjBg0azJw5UyHSvHr1KigoqHPnzvKp5IoWLbp582Z5V9bs+++/l49lsnnzZl9f3+joaIVV7t69+8UXX6xY" +
        "seL1P1nJ0WjcF2U68otsw8LCxowZk+P9w2lpaVOnTv3iiy/MWJqStWvX9ujRY+fOnfLDPkevXr3y9/dPTk6WnmnTpo2JqwMAABZgp7sJdClSpEjnzp23bduW" +
        "vfjo0aPBgwe/3szPz2/Lli3yZzw9PVeuXCk/M3PkyJFq1ap17dq1U6dOTZo0KVWqlJubW1JSUlxc3P37969evXr+/Pl//vnn/PnzWVlZau5cEkTbtm2XLl06" +
        "aNAg6URNUlLShAkTFi1a1L179w8//LBixYplypRJT09/+PDhjRs3tm3bFhIS8uLFC3knhQsX/uuvvypVqmSBF6CXmjVr/vzzz/LbHffv31+tWjV/f39fX996" +
        "9eqVKFHCzs4uNjb2xo0bp06d2rlz5+HDhzMzMxcuXPh6b1ZyNBr3RZnO8OHDg4ODpcV58+YdPXp01KhR7733XqlSpZKTk+/evfv3338vWbJEuqDUz89v69at" +
        "5izydcnJyVu2bNmyZYuzs3OLFi1atGhRq1at6tWrFytWLPvq0NjY2OvXrx84cGDJkiWPHj2SVqxZs2bLli0tVzgAADAVAqFxzJgxY+/evYmJiXldsVu3bj//" +
        "/PPo0aOlqxYzMzO3bdsmxUuoNHDgwIIFCwYGBspvfnvw4MGCBQsWLFigc/UyZcrs2LGjYcOGpqzR+IYMGRIZGTlr1izpmZSUlODgYHlWUc9KjkbjvigTadiw" +
        "ob+//7p166RnTp48qTBZX6dOnYKCgiweCCWJiYl79+7du3evmsZOTk6rVq2S394JAADeGPwHbxzVq1ffs2ePh4eHHut+9tlnu3fv1pqVG3ro3bv3iRMn6tev" +
        "n9cVu3Tpcvr06XyXBrPNnDlz8eLF8ouWDWElR6NxX5SJLFmypEmTJmpa+vj4bNiwwcbGxtQlmUKRIkX27t3boEEDSxcCAABMgkBoNC1atIiIiNi8efPAgQPr" +
        "1atXsmRJNXNVZ2vXrl1ERMQ333wjH9NPwVtvvTV27Fi9J2F/g9WpUycsLGz16tV16tTR2djGxqZ169b79+8PCQkpU6aMGcozkWHDhl29erV37946I4eHh8fc" +
        "uXOVZ1m0kqPRuC/KFJydnUNDQ4cNG6Zw02nBggUnTZq0Z88e+eQ0FtS/f/9t27b5+fk5OzvrbGxvbx8QEHD16lUuFgUA4A1mozw4HswsPj4+NDT0n3/+CQsL" +
        "e/LkybNnzxISEgoVKuTq6lqhQoVq1arVrVv3/fffl49ritycP39+z549J06cuHHjxoMHDxISEmxsbAoXLuzh4VG9evXmzZt37txZPlbkGyA6Onr37t379u27" +
        "efPm06dPY2JiChYs6O7uXq1atfr16/v6+jZr1kz9eSorORqN+6JM4ebNmytWrAgNDY2MjIyJibG3ty9ZsmStWrXatm3r7+9fokQJC9aWm5SUlGPHjv33338X" +
        "L168evXq06dPX716VaBAAVdX1zJlynh7e7/77rvdu3cvVqyYpSsFAACmRSAEAAAAAEFxySgAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiK" +
        "QAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAA" +
        "AAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIh" +
        "AAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAg" +
        "KAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAA" +
        "AAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAI" +
        "hAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoO0sXkC9dvHhx7969x44du3nz5v37" +
        "9xMSEmxsbAoXLly+fPlq1aq99957vr6+FSpUsHSZgpo1a9b48eOzH1erVu3atWuWrcfiHj58eOjQoVOnTl25cuXGjRsxMTFxcXEuLi7u7u4eHh5NmjRp2bJl" +
        "hw4dChQoYOlKAQAAYG42WVlZlq4h38jMzNy0adOPP/547tw55ZY2NjatWrWaOHGij4+PWUrD/49AqNFoMjMzjx8/HhIS8ueff964cUNnew8Pj1GjRn3++edG" +
        "jIWLFi369NNP5c9UqVIlIiLCWP0DAADAcFwyqtalS5eaNGnSt29fnWlQo9FkZWWFhoa2adOmW7dujx49Mn11wP84dOhQ8+bN58yZoyYNajSau3fvjh079t13" +
        "371165ZRCrhz544UywEAAGC1CISqbNmypXHjxqdOncrritu3b69fv74eK2Ls2LE2/6dBgwb5rv/86OTJky1btoyKijK8q2HDhsXHxxveDwAAAEyKQKjbmjVr" +
        "evXqlZiYKH+yTJkyI0eO3Ldv340bN+Lj41+8eHHlypUdO3YMGDDAzc1N3vLBgwc+Pj4nTpwwb9XA/6948eL+/v6//vpreHj4/fv3U1JSXrx4ce7cuZ9++qly" +
        "5crylvfv3//ggw9SU1MN2dyqVav27t1rWMkAAAAwBwKhDv/+++/AgQMzMzOlZ5ycnKZPnx4dHb1w4cK2bdtWrVrVxcXFzc2tRo0aH3744apVq+7cuTNs2DAb" +
        "Gxtplbi4OF9f39u3b1vgBYhn3LhxWf9HzBsIJba2tl27dg0JCXnw4MHatWs/+eST+vXrly1b1sHBwc3Nzdvbe/To0deuXRs7dqx8rWvXrs2bN0/vjT5+/Hj0" +
        "6NHZjytUqFC2bFmDXgMAAABMiUCo5MmTJ7169crIyJCeKVGixMmTJydOnGhvb5/bWq6urosXLw4JCZG3iYmJ6dGjh7wrwHTs7Oz8/f0vXbq0ffv2Ll26KByu" +
        "9vb2s2fP/uqrr+RP/vzzz3oPNxUUFBQTE5P9+LfffitYsKB+/QAAAMAMCIRKRo8e/ezZM2nR3d39wIEDtWvXVrPuhx9+uH79evmYjeHh4T///LPxqwRe07x5" +
        "87Vr19aoUUNl+++++65KlSrS4sOHD/W78XXbtm1bt27Nfty7d++OHTvq0QkAAADMhkCYq/Pnz69fv17+zK+//lqnTh31PXTv3v2zzz6TPzNjxoy4uDjj1AcY" +
        "j4ODw4ABA+TPnD9/Pq+dxMbGBgUFZT8uWrQoP38AAABYPwJhrn788Uf5VXPt27fv3bt3XjuZNm2ah4eHtBgTE7NkyRLj1KeXtLS0gwcPTp48+aOPPqpevXrx" +
        "4sUdHBxcXV0rVqz47rvvjhkzZufOnenp6YZvKCsra+fOnf369fP09HRxcXFycvLw8GjVqtXMmTPv3LljeP8wuoYNG8oX9ZguZfTo0dJas2fPLlmypHEqAwAA" +
        "gOlkISexsbFOTk7yHRUWFqZfVwsWLJD3U7NmzdxaFitWTGq2dOlSNZ2HhobKO4+Njc2xWWZm5r///jtgwIDChQvrPCQqVaqkcutZWVn+/v7Sir6+vtlPnj17" +
        "tm7durn17+TkNGXKlMzMzBw79PX11VmhnJ+fn1YPM2fOlP5arVo1Y/W/du1a+fPh4eEqd1G2+vXrS+s2adIkT+uax+HDh+UvcNKkSXla/e+//5bWbdWqlfS8" +
        "/ErUKlWqGLtqAAAAGIQzhDn766+/kpOTpcVatWo1atRIv678/f0dHR2lxStXrly/ft3Q+vLo/Pnz77///urVq9VcsBoVFTVkyJA+ffokJSXpsa0///yzadOm" +
        "586dy61BcnLylClTtMZutX49evQoUaKEtPj777+rX/f06dOnT5+WFj/55BPp8cqVK21kRo4caZRq9XD37l35YunSpdWvGx8fP3To0OzHTk5Oedo5AAAAsCAC" +
        "Yc72798vX+zfv7/eXRUtWlRraI19+/bp3ZvZbNy4cdCgQXld68iRIz169JBn6dysWrXqt99+06s0y3BwcBg8eLC0uGHDBvW3g8oDUtGiRXv27Gnk4ozh2LFj" +
        "8kX1A9JoNJrx48dHR0dnP/7mm288PT2NWRkAAABMhkCYs5MnT8oXmzZtakhvWquHh4cb0pshSpYsGRAQ8Pvvvx89evT+/ftxcXFpaWkxMTFnzpxZvHjxe++9" +
        "J2+8YcOG1atXq+/8xYsXffr0yZ7TvEqVKnPnzr1w4UJsbGxycnJkZOSSJUu05kAfP368ND9BvjB8+HBb2//3kYmPj9e6iDQ3cXFxGzZskBYHDhyodTWyNUhJ" +
        "SZEPoVSkSJHmzZurXPfo0aO//vpr9mMvLy+tGSwAAABgzQiEOUhNTb1x44a0aGtrW69ePUM61Fr94sWLhvSmB1tbW19f3/379z98+HDFihVDhw599913y5Yt" +
        "W6hQITs7O3d393feeWfYsGGHDh3auXOnm5ubtOKMGTOyVM9Hd/To0Xv37mk0mq+++urq1atffPFF7dq1ixQp4ujoWKlSpSFDhly8eLFVq1ZS+7i4OK1xXDUa" +
        "za5du7KvZh4zZoz0ZP369XO76HnLli152hWG9F++fPlOnTpJiyovjFy7dm18fHz2Yxsbm2HDhuWpYPP45Zdf5OG8R48ednZ2alZMSUkZNGhQ9tW/tra2S5cu" +
        "VZjzEAAAANaGQJiD+/fvy2eQL1++fKFChQzp0MvLS75o/mE269Sps2vXrjZt2kgnuHLTqVOnXbt2Sc1u3LihdfWsThMmTPjhhx9yTAXOzs4bNmyQB86NGzfm" +
        "qXOLGzFihPT4/PnzYWFhOleR58Y2bdpUrVrVJJUZIDIyctKkSdKio6PjuHHjVK47depU6Z7YTz75pEmTJsavDwAAACZDIMyB1oD7RYsWNbBDd3d3+eLz58/T" +
        "0tIM7NN0mjdv3r17d2nxwIED6tf19vaeNm2aQoPSpUvLb8g8e/Zs/hpapl27dvJEt3jxYuX2J06ckE/oJx9Oxkqkpqb27t07ISFBembcuHFaF/fm5uzZs7Nn" +
        "z85+XK5cOfn4rgAAAMgXCIQ5kH851mg08jNa+nF0dJQPNKrRaPQbwNNs5KPgqDkJJhk7dmyBAgWU28ivukxMTLx582Zey7MgGxub4cOHS4ubNm168eKFQnv5" +
        "6cG33nqrc+fOWg0CAgLkV6guWrTIqPXqNnz48FOnTkmLDRo0mDhxopoV09PTBw0aJM1auWjRIjWTmgAAAMCqEAhzkD0sisTFxcXwPrUuOk1JSTG8T9N56623" +
        "pMfXrl1TuZatre3rged1tWvXli8+efIkT7VZ3MCBAwsWLJj9OCkpac2aNbm1fPHixaZNm6TFIUOGqLwxz2xmzJixYsUKadHd3f2PP/5QeRPgjz/+ePbs2ezH" +
        "fn5+Xbp0MUmJAAAAMCUCYQ4cHBzki1onDPWj1YlRQqZ+Xr58uWrVqqCgoObNm1eoUMHd3d3Ozs7mf7Vr105qr3wGTM7T01PN2VT5bH7Z9eSlfMtzd3fv06eP" +
        "tKgwtMzq1aulU8F2dnbyWSuswe+///7NN99IiwULFtyxY0fFihXVrHvt2jXp2mA3N7eFCxeaokIAAACYmnWdr7ASzs7O8kXDE0tqaqp8ar4CBQpobcI8Hj16" +
        "NG7cuE2bNqmZJ1CSmpqalJQknRNTULJkSTUd2tvbOzg4SKdhjZK3zSwoKCg4ODj78eXLl48cOZLjJA1LliyRHnfu3Fl+3tXi1q1bJx8gx97efvPmzVrzjuQm" +
        "Kytr8ODB0lnuWbNmlSlTxiRVAgAAwMQ4Q5iD0qVLyxdjY2MN7FBrtj2tMWbMY9euXdWrV1+1alWe0mA2lRe4qr+FzMbGRnqsfloL61GvXr3GjRtLizmeJDxy" +
        "5Mjly5elRasaTmbbtm0BAQHScD62trZr1qyR39upbNGiRUePHs1+3KxZM+ucSAMAAABqEAhz8NZbb8lHRomOjpbmkdOPPBhoNBpPT09DetPD/v37/fz88t3F" +
        "mdZMfnpty5YtWplf878p8e23327Tpo2ZKtPlr7/+6tOnjzQYjI2NzbJly3r16qVy9ejo6AkTJmQ/dnBwWLp0qTzeAwAAIH8hEObA0dFRPrVAZmamNHiGfk6f" +
        "Pi1frFWrliG95VVSUtLgwYPlI+U4Ojr27Nlz0aJFBw8ejIiIePHiRVJSknysy9DQUHNWmB/16tWrePHi2Y+Tk5NXrlwp/2tMTIx8Rvvhw4dbSWrat2+fn5+f" +
        "/GBYuHDhwIED1fcwffp06feRcePG1ahRw8glAgAAwIwIhDlr1KiRfPH48eOG9HbixAn5YoMGDQzpLa+WLVt2584dabFdu3bR0dGbNm0KCgpq0aJFlSpV3Nzc" +
        "nJyc5Kvkx/v6zMzR0TEwMFBalN8uqNFoVq5cKV2a6+TkFBAQYM7achMaGtq1a1f5BcCzZ88OCgrKUydxcXHS42nTptnocuvWLan9rVu35H+qXr264S8KAAAA" +
        "hiAQ5kw+zKZGo1m7dq3eXcXGxu7evVv+jK+vb44t9biz7tWrVzrb7NixQ3pctWrVkJCQUqVKKa9i+G2TIvjkk09sbf/fJ+j69ev//fef9Cd5PuzZs2exYsXM" +
        "XNvrjhw50rlzZ/kEmFOnTh07dqwFSwIAAIDFEQhz9sEHH8inkr948WJ4eLh+Xa1fv15+TqZ+/fq5jTYpH8kzMTFRTedqJvGTn58cPny4mvFCtW56RI4qVqzY" +
        "sWNHaXHx4sXZD0JDQ69fvy49bw3DyYSFhX3wwQfyE79ff/31pEmTLFgSAAAArAGBMGfu7u7dunWTP6Pft+eEhIQff/xR/oxCPJBP4vf06VM1/YeFhSk3SExM" +
        "lMcAb29vNd0ePHhQTTOTks/hLo2HaW39y6+33L59e/a7Jh9Opm7duk2aNNG3RuM4ffp0+/bt5Zd6jho1atasWRYsCQAAAFaCQJirr776Sn4N5549ezZv3pzX" +
        "TiZPniy/f69MmTL9+/fPrbH8Ss4LFy7o7DwtLW3Pnj3KbbTGR5Wf9szNpUuXDLxn0igKFSokPTbFPY1G6b9Dhw6VK1fOfpyamrpixYqnT59u375damDx04MX" +
        "Llxo166dfIDZoUOHzp8/X+8ON27cmJUXVapUkdatUqWK/E/Xrl0z5KUBAADAcATCXNWtW7dPnz7yZ0aMGHHp0iX1PYSEhGh98542bZqDg0Nu7eWn70JDQ3Ve" +
        "Nbp69er79+8rt9Ga81B+KWNupEkFLKtIkSLS4zt37mRkZFhh/zY2NsOHD5cWlyxZsnz5cmkMT1dX1759+xpWpkGuXr3apk0b+ZQYH3/8sXRpKwAAAEAgVDJv" +
        "3jxpagGNRvP8+fM2bdpcuXJFzbp//fVXr1695DGjbdu2gwcPVlhFPrRpfHz8r7/+qtA4IiJi9OjROsuwt7evUKGCtLh06VLl4Wrmz5+/c+dOnd2aQc2aNaXH" +
        "ycnJ+/bts87+AwMDpTFab9269d1330l/6tevn/w8ZI5WrlwpH3hz5MiR+pXxuoiICB8fH/m1x7169Vq+fLmVTIABAAAAa0AgVFKyZMmNGzfKJ6l//PhxgwYN" +
        "Zs6cKc3r/bpXr14FBQV17txZPttb+fLlV6xYoby5Tp06yfPD5MmTc7uXLywsrEWLFmqGGNVoND4+PvIVx4wZk+Mtc2lpaVOnTv3iiy/U9GkG9erVs7e3lxYD" +
        "AwOXL18eFRUlH6HHGvovVqyYfFZ3+XldC14vGh0d7ePj8/DhQ+mZrl27rl27Vn4wAwAAAARCHXx8fIKDg6XZBTQaTVJS0oQJEypUqDBq1Kh//vnn1q1biYmJ" +
        "r169un79+s6dOwcOHFihQoVff/1VfiKuRIkS+/bty21wUYmLi4u/v7+0mJiY2LZt288++ywsLOzVq1epqan379/fvn177969mzZtmv1dv3fv3jpfgvyaRo1G" +
        "M2/evKZNm65fv/7u3bupqamvXr26fPny3Llza9euPWXKlOw2fn5+uneNiRUpUqRz587S4qNHjwYPHly5cmUnJyf5KbXu3btbvP8cp/Jr3ry5l5eXfrUZbs2a" +
        "NfKbVzUaTUhIiL29vc5pAyVt2rSxVPEAAAAwGzvdTYQ3YMCAggULBgQEyE/+PHjwYMGCBQsWLNC5ure399atW+VDayiYMWPGtm3bpMv80tLSFi5cuHDhwhwb" +
        "9+rVa9iwYRs3blTus2HDhv7+/uvWrZOeOXnypDx5aunUqVNQUNDWrVvVFGxSM2bM2Lt3r8oZOCzYf8OGDRs0aKA1MYnFh5MBAAAAdOIMoSo9evQ4ceJEgwYN" +
        "8rSWvb39Z599dvz4cZVpUKPRFCtW7I8//tB545lGo+nUqdPKlStVdrtkyRKVkx/4+Phs2LDBSm4zq169+p49ezw8PKy/f62ThCVKlND71CUAAABgNgRCtWrX" +
        "rh0WFrZ27VqVU/lVq1btypUrP//8s5qJ4OVatmz577//1qlTJ7cGTk5OU6dODQkJkcYy0cnZ2Tk0NHTYsGEKt5AVLFhw0qRJe/bsURNHzaZFixYRERGbN28e" +
        "OHBgvXr1SpYsmdf9aZ7+P/roI3mKDgwMVBhOFgAAALASNspjTiJHFy5c2LNnz/Hjx2/cuPHgwYOEhARbW9tixYrJx/CwsbFZsmSJ8rCiCjIzM7dv375jx46w" +
        "sLAnT57Ex8e7ubnVrFmzXbt2gYGBZcuW1a/bmzdvrlixIjQ0NDIyMiYmxt7evmTJkrVq1Wrbtq2/v3+JEiX06xYbN26UJimxtbWNiIioVKmSZUsCAAAAdCIQ" +
        "GtMPP/wwbtw4adHGxiY4ODggIMByFcFM3n///dDQ0OzHHTt23L17t2XrAQAAANTgklFj+vrrr3/44QdpMSsra9CgQfLRXPBGunDhgpQGNa8N6woAAABYLc4Q" +
        "Gt+cOXO+/PJLabFAgQLr1q2TT1WHN4yfn9+2bduyH3t4eERFRTHdHwAAAPIFzhAa39ixY+fMmSMtZmRk9OvXzxpmcYAp7Ny5U0qDGo1mxIgRpEEAAADkF5wh" +
        "NJV58+aNHj1aWrS3t//jjz+6dOliwZJgRCkpKXfu3NmwYcMPP/wgzWRYtGjR27dvFy5c2LK1AQAAACoRCIE8CA8Pb9iwYW5/nT9//qhRo8xZDwAAAGAILhkF" +
        "jOO999779NNPLV0FAAAAkAcEQsAI3n333ZCQEFtbPlAAAADIT+wsXQCQX9nZ2RUrVqxevXp9+/bt06cPY8kAAAAg3+EeQgAAAAAQFFe4AQAAAICgCIQAAAAA" +
        "ICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQA" +
        "AAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICg" +
        "CIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAA" +
        "AICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQ" +
        "AgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAA" +
        "giIQAgAAAICgCIQAAAAAICgCIQAAAAAIys7SBcCEMjIyQkJCdu7cGR4e/ujRo5cvX6anp2f/ydvb+9y5cxatDm+45OTkXbt2HTt27OTJk/fv33/x4kVcXJyz" +
        "s3PhwoXfeustT0/PmjVrNmvWrHHjxk5OToZs6OXLl3///ffRo0cvXrwYFRX1/PnzxMREJycnNzc3d3d3T0/PevXqNWjQoHXr1o6OjsZ6ddBbSkrKn3/+eejQ" +
        "oVOnTmUfGCkpKa6uru7u7l5eXg0bNuzatWvNmjXz1OfDhw+zO7xy5cqNGzdiYmLi4uJcXFzc3d09PDyaNGnSsmXLDh06FChQwEQvCgCAfCwLudizZ4/yrrO1" +
        "tXVzc/Pw8GjUqNGwYcOWLl367NkzS1f9/wsPD69atWpuxXt7e1u6QLyxXrx48eWXXxYtWlTNP0GOjo7t27fX7+MTFhbWo0cPBwcHNRtyc3MLCAg4ePCgHq/o" +
        "xx9/1Opt586devST478qa9euVd/D0qVL5eueOnVKjzL0s3DhQq3Kq1SpkqceUlJSpk6dWqxYMZ1vVvPmzU+ePKncW0ZGxpEjR8aOHevp6anmAPDw8JgzZ056" +
        "erqaUmNjY9X0qWzevHl52j8AAFgEgTBXOgPh6xwcHHr27Hn79m1L15516dIlFxcXhVIJhDCR0NBQDw8PPb49d+nSRf1WHj582Lt3bz22otFofHx8jh8/nqcX" +
        "VaNGDa1OunXrlrf9kpWVlcu/KpUrV05NTVXZg6UCYXR0dKFChbQqz1MgjIyMzNN5vwIFCkydOlWhw9DQUPW9SRo1ahQREaGzWgIhAEAc3ENoTKmpqZs3b/by" +
        "8tqwYYNlKxk8eHBCQoJla4CA9u/f36FDh7t375p0K4cOHapbt+7GjRv1W/2ff/7p1KmT+vbHjx+/evWq1pM7d+58+vSpfgVoiYyM1Ip5VmjYsGHx8fF6r37n" +
        "zp1WrVpduXJF/SoZGRmTJ0+eNGmS3hvN0cmTJ1u2bBkVFWXcbnP0zjvvmGErAAAYiEBofPHx8f3799+xY4elCggPDz9x4oS0WKdOnR07djx58kR+rRQ3EMLo" +
        "bt261bVr15SUFPmTTZo0mT9//rFjxx4/fpycnBwfHx8VFRUaGvrdd9/5+PjocU/XX3/91a5du8ePH8ufdHBwaNu27U8//XT8+PHIyMi4uLjExMTbt2+fPHky" +
        "ODi4T58+JUuW1Pt1rVix4vUn09LS1q1bp3efWr777rvExERj9WZ0q1at2rt3ryE9DB8+/M6dO/Jn6tWr9/vvv1+5cuXVq1epqamPHz8+cOBAUFCQs7OzvNn0" +
        "6dOPHTumZhPFixf39/f/9ddfw8PD79+/n5KS8uLFi3Pnzv3000+VK1eWt7x///4HH3yQmppqyCvSqUqVKi1atDDpJgAAMA4Lnp20cloXd82ePVurQUZGRkxM" +
        "zOnTp+fPn1+rVi2tHVusWLEXL15YpPIffvhBKsPFxeXJkycWKQOi+fDDD+UfgZIlS/7111/Kqzx48GDWrFkVK1bUqLtk9MSJE1oj0Nja2g4YMODOnTvKK2Zk" +
        "ZPz111+tW7eWPp4qX1RCQoKrq6u0Ofllk7Vr11bZiUThQvQZM2ao6cH8l4w+evRIuh20QoUKZcuWlbau8pLRQ4cOab3YadOmZWRk5Ng4IiKievXq8sYtWrTI" +
        "sWX2JaO2trZdu3YNCQlRuOw2NTV17NixWjXMmjVLTfHq/fTTT3q8oQAAWByBMFc6A6Fcenr6N998o/WFY/LkyeYq9n/07dtXqqFz584WqQGiuXHjhvzgL1y4" +
        "8LVr11Sum5qaunjx4s8++0y5WUxMjDyNaDSaQoUK7d+/P0917t69u0KFCuoD4apVq6TNFShQQOtsYXh4eJ62rvWvir29vfS4SJEiMTExOnswfyD08/OTNrd7" +
        "9+4qVapIiyoD4ciRI+U1Dxw4ULl9VFRUwYIFpfa2traPHj16vdnhw4f9/f2vXLmi8oV89dVX8jLKlCmTmZmpcl015D8LFihQ4N69e0bsHAAA0+GSUeMoUKDA" +
        "d999FxAQIH9y27ZtFikmJiZGeqzf8B5AXmldIz1hwoRq1aqpXNfe3n7YsGE///yzcrMxY8Y8ePBAWixSpMi///7bpk2bPNXZsWPHixcvdu/eXWX74OBg6XHb" +
        "tm0HDBjw1ltv5fhXPQwePFh6/OLFi1mzZhnSmyls27Zt69at2Y979+7dsWNHPTo5deqUfPH13860VKxYccCAAdJiZmZmeHj4682aN2++du3a18f7yc13330n" +
        "T7MPHz7UKswQYWFhly9flhbbt28vP04AALBmBEJjmjZtmnzx4sWLxhp2Ik/kYz/IT0EApqP13Vp+mtooLl26JD9Zp9FoFi1a1LBhQz26Kly48OLFi9W0jIyM" +
        "lF/u+PHHH9va2vbr1096ZsOGDVr3TOZJu3btpKtYNRrNwoUL5YnX4mJjY4OCgrIfFy1aVGdiz82jR4+kx+XKldO6oy9HLVu2zK0HvTk4OMhzpkajOX/+vOHd" +
        "Zlu+fLl8cdCgQcbqGQAAUyMQGpOHh4fWz9X37t1Tue7du3d///33jz76qHbt2qVKlXJwcChZsmStWrV69+69cuXK58+fqy8jKysrD0WbuJjXpaenb926dcCA" +
        "AV5eXm5ubgUKFLCxsVEe9dGk9WRlZe3cubNfv36enp4uLi5OTk4eHh6tWrWaOXOm1jAYefLw4cNVq1b169fvnXfe8fDwcHJycnFxKVeunI+Pz1dffXXo0KHM" +
        "zEz1vZl0DxiF/Cu7o6Nj+fLljdv/Dz/8IN9jXbp08ff3N+4mXrdixQrp0+Tq6tqlSxeNRvPxxx9LDWJjY7dv327IJmbOnCk9TkpK0vpRybJGjx4tva2zZ8/W" +
        "e2Ae+RuncnZKrbkK8/RhUaD1C4JRcqZGo0lMTNy0aZO0WKJEic6dOxulZwAAzMGyV6xaszzdQyhp166dfK2///5b5yq3b9/u16+fjY2NwttUqFChKVOmxMfH" +
        "59ZJni5P8vPzM2kx2eTf1319fbOf3LdvX6VKlV7vs2PHjhap5+zZs3Xr1s2tZycnpylTpuT1RqPbt28HBgbqHD+zXLlyc+fOTUhI0NmbsfaASclH2HdxcTFu" +
        "57GxsVpjyeT15j09ZGRkyK+4Hjx4sPQnea5o166d+j61/lXZvn17VlZW165dpWfs7Oxu3ryp0IPZ7iH8+++/pa20atVKel6PewgbN24sP+zVrLJ+/Xr5y9y1" +
        "a5eeL+N/HT58WN7tpEmTjNLtypUr5d2OHj3aKN0CAGAenCE0Mq1fsrN0naxbs2ZNtWrV1q5dq9wyPj5+ypQpzZo1u3//vhGqtFAxv/zyS/v27XOcASzHMwCm" +
        "rufPP/9s2rSpwgwcycnJU6ZMGThwoPoTFCtWrPD09AwODs7IyFBuee/evdGjRyvfhGaKPbBy5UobGa0BP/RWpEgR6XFCQoKxzr1k2717d3JysrRYv379+vXr" +
        "G7H/HB04cEA+oaL8xKD8bmGtZnqYMWOGre3/+6c4x+GpzC8+Pn7o0KHZj52cnH7//XdDemvevLn0+N69e5GRkTpXOXjwoPS4QIECTZs2NaQAidY7Vbp0aaN0" +
        "q3W9aGBgoFG6BQDAPAiERqb1haNEiRIKjWfNmjVgwAD19yCdP3++adOmxv2qbbZiduzY8emnn+YWbF5PXKau58iRIz169JDHjNysWrXqt99+U9PnxIkTAwMD" +
        "jTW/mVUdHjppXSO6YcMGI3a+b98++aLRb1DMkTyrV65cWZ5qevfu7eDgkP04MzNT6+bGvKpZs2b//v2lxc2bN1t8mtDx48dHR0dnP/7mm288PT0N6S0wMFB+" +
        "invGjBnK7W/fvr169WppsWvXriovNNVJa0pD9QPSKLh586b8xGPjxo1fn4UIAABrRiA0ptu3b1+/fl1atLOzq1mzZm6NN2/ePH78ePkz3t7e8+fPDw8Pf/bs" +
        "WWpq6rNnz44ePTp+/Hg3Nzepzd27d/v372+sO2rMVkxMTMyQIUOy02CFChV++OGHs2fPPn/+XDpVrfWN39T1vHjxok+fPtnJrUqVKnPnzr1w4UJsbGxycnJk" +
        "ZOSSJUu0xr0YP368fOzWHC1duvT777+XP+Po6Dhw4MAtW7ZkT5WelJT04MGD//77b/bs2S1atJBOCuXIqg4PNbRGAZk8efLJkyeN1bnWiDXySxBNJDY2NiQk" +
        "RFrUGoykaNGi8pvEVq5cqfNaAGVTp06VEmZWVpbWW29mR48e/fXXX7Mfe3l5ac3WoIeaNWsOHz5cWgwODv7uu+9y22O3bt3q2LFjUlJS9qKLi4uxBl9NSUmR" +
        "X4lapEgRecjXm9ZJfk4PAgDyH7NfpJpv6HEPoXz4QY1G06FDh9xaRkVFyb/Hu7q6rl69OrfGT548kQ9FqNFoli9frlBGs2bNpJajRo3SWbaJislxzI+hQ4cm" +
        "JiZaTz1fffVVjvNZJyQktGrVSt5y4cKFCjVfuHBB6ya3tm3b3r59W2GVO3fujBo1Kjg42Gx7IJvWTHpBQUEKjdWLiYkpXLiwvGd7e/vPP/88IiLCwJ5TUlLk" +
        "N2QWKFBA542Xhlu4cKG0RRsbm1u3bmk1+PPPP+Uv9r///lPTbY73EGb77LPP1HRo6nsIk5OTpflCbG1tjx8/rtVAj3sIs7KykpKStO6vrl+//pIlS65evRoX" +
        "F5eWlvbkyZN//vln5MiRzs7OUhsHB4edO3ca66VpTRyf/ROVgdLT08uUKSP16ezs/PLlS8O7BQDAnAiEucrrxPTjxo3T/K+9e/fm1l4eTlxcXF7/1qUlKSmp" +
        "QYMG0io1a9ZUGOkkr4HQRMW8HsA+/vhjncWYs54JEyYodPvw4UN5KmvWrJlCY61BBXv27JljzlTJpIeHiQJhVlbW5MmTNTl55513xowZs3Xr1rt37+rRrdYt" +
        "Z5UqVTJWwQrq1asnbbFFixavN0hLS5OPujlgwAA13SoEwidPnhQqVEj6U9OmTXPswdSBUH5yMsdjQ79AmJWVlZKS8vXXX6ufCKdatWonTpww1uu6deuWi4uL" +
        "1Lmjo+PrIV8PWr8LqPwnDgAAq0IgzJXOQJiRkfHixYszZ87Mnz//9UtD/f39c+v53r178m9FCxYsUFPPuXPn5PfhHDx4MLeWeQqEpitGK4CVKVNGzW/nZqvH" +
        "29s7PT1duVv5gCvOzs4ZGRk5Nrty5Yp862+//bYhA36a+vAwXSBMT09v27atRlGZMmW6dOkyZ86cU6dOqRy+Veu+r3feecdYBedGa3q6ZcuW5djs888/lx8e" +
        "r1690tmzQiDMysqaNGmS/K87dux4vQeTBsIzZ87Y2dll91yuXLkcX5HegTBbdHT0xIkTlWewaNOmzYYNG3R+PNVLSUnRmnBi8uTJRulZPkKsRvFzBwCA1SIQ" +
        "5krrq1uedOnSJTk5Obee5VONlSpVKi0tTWVJ8osYp02blluzPAVC0xWjFcBUjvButnrWrFmjs8+9e/fKV7l27VqOzb788kt5s61bt6qsOUemPjxMKjk5Wf09" +
        "VOXLl584ceK9e/eU+zxw4IB8LfkUCCYiv3qzYMGCuf2QoTX0y9KlS3X2rBwIX758Wbx4cemvXl5er/8GYbpAmJaWJp87JCQkJMdmhgTCpKSkn3/+2cvLS/nA" +
        "cHZ27t69+8mTJ43xsrKysrIGDhwo779BgwaGnMCXPHr0SMrPGo2matWqhvcJAID5MaiMkZUtW3bx4sXbt293dHTMrU1oaKj02M/PT/6VQpn8VrGjR4/qXaRF" +
        "iunVq5f11GNra6tm5ujatWvLF588eZJjM/kQ+eXKlcuevlxvVnV45JWjo+Py5cv//fffJk2a6Gx8586dGTNmeHp6Tp48OS0tLbdmWuOsyq+rNIXU1NR169ZJ" +
        "ix999JGrq2uOLb29vb29vaVFrVOvenB1dZVfsXnp0iV5Jab2448/nj17Nvuxn5+fgYfx606cOFGjRo1Ro0ZdunRJuWViYuKWLVsaNWo0bNgwaXQZvc2YMUP+" +
        "1ri7u//xxx/qr1xVsHr16vT0dGlRK3YCAJBfEAiNqW7dumFhYcOGDVOYRjwtLS0sLExazNN0avKR/aVB4Q1htmIKFy5cvXp166nH09NTfn9gbrRmDXn58uXr" +
        "bRISEs6cOSMttmnTRueU9Aqs6vDQW+vWrY8fP37u3LnRo0frfN8TExOnTZvm4+Pz7NmzHBtIw29mS0hIMFqhOfnzzz+fP38uLWqNL6pFPjnhsWPH5IMM6yco" +
        "KMjDw0NanDx5srFmMVF27do16dS0m5ubfEwdo9i3b1+rVq1u374tPVO7du1ffvnl0qVLL1++TE1NffTo0d9//601qMySJUt8fX3VzA2Tm99//10+r2PBggV3" +
        "7NhRsWJFvTuUk+fMAgUKyA8GAADyE0uforRe+l0y6u7u/u+//yp0e+3aNXl7W1vbAjK2/0c+dXiOGypZsmRum1B/yahJi5Ffoqnyvi+z1ZPjMCE5kqeRjRs3" +
        "vt5A63THb7/9prJn8+8BS3n06NHWrVsnTJjQvn37YsWK5ViwRqNp1qxZjhfymfkewo4dO0rbKlu2rPKdbI8fP5afwv3666+VO1e+ZDTbsmXL5G20hrc1xSWj" +
        "mZmZ8n80lI9hPS4ZjYqKkp/XLVCggMIYXXfu3NGaVmTw4MF5fklZWVlZWWvXrpVP7mJvb2/EMUu1TsJ36tTJWD0DAGBmnCFUS+sbTGZmZlxc3Pnz5xcsWCA/" +
        "BxIbG9upU6cTJ07k1o/85INGo8nMzMyQyfw/8m3l2M+LFy8Mf1FmK0bN6Thz1qM1QYICeeLKcXNa8xOWK1dOZc85sqrDw1hKlSrVrVu3GTNm7N279+nTpxcu" +
        "XJg6derrZ2mOHj2a45TlpUqVki+a9KU9ePBAPiVmv379lM/3lixZskOHDtLi6tWrMzIyDKwhICBA/k/K9OnTTX1SdNGiRVK8adas2bBhw4zb/6hRo+Lj46XF" +
        "n376aezYsbk19vDw2Ldvn3yYrmXLlh0/fjyvG922bVtAQIA0J6etre2aNWs6deqU135ys3z5cvki0w8CAPIvAqGebGxsChUqVKdOnU8//fTixYtBQUHSnxIT" +
        "E7t3757bPOaxsbFGKcDw750aMxYjH/BdgVXtHJW03ugiRYoY0lt+3AN5YmNjU7t27UmTJt28eXPevHlat9rOnz//9etyy5UrJ09ld+7cMfy+stysWrVKvuuU" +
        "rxfNJr9Q8OHDh1oDEemhQIEC3333nbT4+PHjefPmGdingujo6AkTJmQ/dnBwWLp0qcIV73qIjIzcuXOntNigQYNRo0Ypr+Lq6vrLL7/In/n555/ztNG//vqr" +
        "T58+0g1+NjY2y5YtU3kbsxrx8fGbN2+WFkuWLGnEqAkAgJkRCI3Azs5u0aJFffr0kZ65f/++fEh6Oa0RMizLqorRWF89ejDwy/QbsAdUsrOz+/zzz7du3Srf" +
        "Yy9fvnw9UDk4OHh6ekqLGRkZ0tgnRrdy5Ur5opeXl40uPXr0kK8SHBxseBndu3eXTyw5Z86c3H5gMtz06dOl03fjxo2rUaOGcfvfvXu3/Dz28OHD1azVqlWr" +
        "atWqSYt79+6VzvXptG/fPj8/P/m9lwsXLjTuiC+bN2+Wn/Ps37+/UUapAQDAIgiERvP777/LLxdcs2aN1r1P2YoWLSpf1DnneG7ko9vpzaqKscJ69KjZwFN8" +
        "+XEPGMLX11frvI18yFaJ1iRy8nF3jOjw4cM3btwwsJOdO3fmNjpOnsycOVN6/PLlS/miccXFxUmPp02bpjMA37p1S2p/69Yt+Z9yHEBIPuSSRqN57733VBYm" +
        "b/ny5Uv5dhWEhoZ27dpV/sPK7Nmz5VdwGAXXiwIA3iQEQqMpXLjw3Llz5c+MGzfu9WbyecY0Gk1UVJRpy1JkVcVorK8eNbQi3L179wzpLT/uAQP17NlTvnjn" +
        "zp3X27Rr106+uH79elNUYvi8ERqNJi0tbe3atYb306ZNGx8fH2lx0aJF9+/fN7xb83v69Kl8sXTp0ipXLFOmjEI/OTpy5Ejnzp3lVxRPnTpV4X5F/Vy/fl3+" +
        "Y1/Tpk3ldzwCAJDvEAiNqXv37vJ5Ag4fPrx//36tNuXLl5cPfHfq1CkzFZcTqypGY331qFGpUiX51WIGnrzKj3vAQPJRKzWv3ZOZzdfX18nJSVoMDw83+lWj" +
        "8fHxf/zxh1G6Mkqw1PzvScLk5OSpU6capVsz07qIWv011Vm5DJiUm7CwsA8++EA+AM/XX389adKkPHWiBqcHAQBvGAKhMdnY2EyZMkX+jDS1l8TV1fWdd96R" +
        "Fnft2mWGwnJjVcVorK8eNZydnevVqyctHjhwwJABXfLjHjCQ1pT0Oc4CX6RIET8/P/kz8mFXjELrrrDly5fn6Rpd+Qf/woULp0+fNrykhg0bduvWTVpcsWKF" +
        "4Ve0mp/WRCMPHz5UuaJWS62T51pOnz7dvn17+eWvo0aNmjVrluoy1UpPT1+9erW06OLiYsSxagAAsAgCoZF16tRJHg+OHDny77//arWRXwl28+ZN/SY8NBar" +
        "KkZjffWo0apVK+nxvXv3duzYYUhv+XEPGEJrMnetSSYk48aNk5873b59+4YNG4xYhnwwGEdHR638qZN8lkuN8U4STp8+XRphNT09XT7HurHkOLumAoV5CLVm" +
        "0cymdeXn4cOHVRYmb2ljY6NwremFCxfatWsnH5926NCh8+fPV7mhPNm1a9fjx4+lxR49eqifwwYAAOtEIDS+b7/9Vr74+oVeAwcOlH+1HTdunOmG0dfJqoqx" +
        "wnrUCAwMlNf89ddfGzJxXH7cA4ZYs2aNfLFu3bo5NvPy8pJP8KDRaIKCgrQGLFEpLi7uk08+kT9z8+ZN+TzjnTp1UjlzpuTtt9+Wj3yzfv16owwYW6NGDfnU" +
        "F1u2bNHvJVtQixYt5Iu///67mrX++ecf+elQb2/vHE8dazSaq1evtmnTRn6l8ccff7x48WK9itVN63rRQYMGmWhDAACYDYHQ+Lp06eLt7S0tHjp0SGvgxOrV" +
        "q8vPP1y4cCEgIMBSw0JaVTFWWI8anp6enTt3lhYjIiICAwO1roRUz9R7YOXKlfKRIUeOHGmUbj/99NM5c+a8evUqT2utWLFCa56JDz/8MLfGP/30U9myZaXF" +
        "2NjY1q1bh4aG5mmLe/furVOnjtbtglpzRfTt2zdPfWaTnySMjY0NCQnRo5PXTZ06VZqtMSsra9myZUbp1mxatGhRsGBBafHUqVOLFi1SXuXly5dah2X79u1z" +
        "bBkREeHj4yMfb6ZXr17Lly837lSKEq15Jj09PZs3b26KDQEAYE4EQuOzsbHRurLr9ZOE06dPl19otHnz5tatW0dEROjs/PHjx/Pnz2/UqJFRSrXCYqywHjW+" +
        "//57+bfezZs3+/r6RkdHK6xy9+7dL774IsdrC/PjHrh79+6XX37p4eERFBR06NAhnbPGJScnT5s2bfDgwfIn27dvrzXGjJy7u/u2bdvkc9m/evWqTZs2AwcO" +
        "1Dm4a1ZW1v79+9u2bduxY8fbt2/L/5SRkSG/K8zNzc3X11e5txz17t1burxTY6QJCTUajYeHh/x8pt4/NFiKs7Oz1tyDX3zxhcJE83fu3Gnbtq386lNHR8cc" +
        "f7aIjo728fGR32rYtWvXtWvXyt8F41q1apX8pxmGkwEAvCHydPeIULTu3Zo9e7b6dTMzM728vOSrHz58WKvN1q1btd4LW1vbbt26BQcHX7ly5fnz5+np6XFx" +
        "cQ8ePDh16tTq1avHjBlTt27d7F++HR0dFbberFkzqc9Ro0apKdgUxchPmPj6+qrbc1ZXjzx+bNiwQaHlkiVLtGp2dHQMDAzcunVrVFRUfHx8cnLyw4cPDx48" +
        "OGfOnJYtW2ZfF7pw4UKz7YFsWhE0KChI5a5Q1qVLF3m3ZcqU6dev3/z58w8fPhwZGRkbG5uenv7q1atbt279+eefn3/+udatZRqNxsHB4fr16zo3tHPnTvmb" +
        "Iu3q9u3bz5s3LywsLHtvJyUlRUdHh4eHr1y5sn///vJTixqNplixYlKHf/31l/xPgwYN0nsntG3bVv5+3blzR6uB1r8q27dvV9Pt06dPc7tR7dSpU3pXqx+F" +
        "ewhz8+TJE62hZTQaTd26dX/77bcrV668evUqNTX10aNHf//9d1BQkPyHlWxfffVVjt0aPrCQj49Pnl571apVpXXt7OwePHiQp9UBALBOBMJcGRIIs7KytEa8" +
        "aNOmzettfv75Z/1+zDZ6IDRFMYYEQuupR30gzMrKynHmSWW5BUJT7IFs5gmEeeXg4LBjxw6V2/rvv/9yG3tGJXkg1Bo/5p9//tF7J6xcuVLe1XfffafVQL9A" +
        "mPW/o5jK5YtAmJWVdfjw4ddjvBodOnRIS0vLsU8zB8JDhw7J1+3cubP6dQEAsGZcMmoqPXv2rF69urR44MCB48ePa7X57LPPdu/erTWzuaVYVTEa66tHjZkz" +
        "Zy5evFg+LaEh8uMe0E/FihV3796tcPeglpYtW547d05rRnv12rRpI50VfPbs2c6dO6U/lS1bVj5mbF5169ZNfoJr5cqVWXmcTC83o0ePLlGihFG6sojmzZv/" +
        "9ddfr58WVubv7//HH3/Y2dmZqKo8YTgZAMCbikBoKra2tjrvJNRoNO3atYuIiPjmm29yG0NPy1tvvTV27Njw8HDjVGnFxVhhPWoMGzbs6tWrvXv31jmshYeH" +
        "x9y5c5VvQ8pHe2DFihXr1q3r27dvnnJLxYoVv/vuuytXrsgn21CjdOnSmzZtOnHiRPfu3R0cHNSs4u7uHhgYeOTIkf379zdu3Dj7yXXr1qWmpkptevfuLR/i" +
        "Na8KFy4sH17o1q1bWqeVDOl5woQJRunKUnx8fC5cuDBixAgXFxedjb29vTdt2rR27dpChQqZoTad4uLi5AMRlSpVSr8bTQEAsEI2xvoBGwaKj48PDQ39559/" +
        "wsLCnjx58uzZs4SEhEKFCrm6ulaoUKFatWp169Z9//33a9asKVoxVliPGtHR0bt37963b9/NmzefPn0aExNTsGBBd3f3atWq1a9f39fXt1mzZurHQsxfe+Dm" +
        "zZsnTpw4d+5cRETErVu3nj59Gh8fn5KS4uLiUrhw4WLFitWsWdPb27tFixZNmzY1fEDIly9f7t2798iRIxcvXoyKinr+/HlycrKjo2ORIkWyd3i9evUaNmzY" +
        "qlUrldERJvXy5csdO3YcO3YsPDz88ePHsbGxKSkpbm5u7u7uVatWbdq0qY+Pz7vvvmvpMgEAEAWBEAAAAAAExSWjAAAAACAoAiEAAAAACIpACAAAAACCIhAC" +
        "AAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACC" +
        "IhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAA" +
        "AACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpA" +
        "CAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAA" +
        "CIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEA" +
        "AAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAo" +
        "AiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAA" +
        "ACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiE" +
        "AAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACA" +
        "oAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIA" +
        "AACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIi" +
        "EAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAA" +
        "AIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAI" +
        "AAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAI" +
        "ikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAA" +
        "AAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAAICgC" +
        "IQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQAAAAA" +
        "ICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICgCIQA" +
        "AAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAAAICg" +
        "CIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQAgAA" +
        "AICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAAgiIQ" +
        "AgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgAAAAA" +
        "giIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiKQAgA" +
        "AAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgKAIhAAAAAAiK" +
        "QAgAAAAAgiIQAgAAAICgCIQAAAAAICgCIQAAAAAIikAIAAAAAIIiEAIAAACAoAiEAAAAACAoAiEAAAAACIpACAAAAACCIhACAAAAgKAIhAAAAAAgqP8PpEMB" +
        "ucV0AFcAAAAASUVORK5CYII=");
static Task M3d1RealPdf() => M3d1RealRecognition("pdf", 
        "JVBERi0xLjMKJZOMi54gUmVwb3J0TGFiIEdlbmVyYXRlZCBQREYgZG9jdW1lbnQgKG9wZW5zb3VyY2UpCjEgMCBvYmoKPDwKL0YxIDIgMCBSCj4+CmVuZG9i" +
        "agoyIDAgb2JqCjw8Ci9CYXNlRm9udCAvSGVsdmV0aWNhIC9FbmNvZGluZyAvV2luQW5zaUVuY29kaW5nIC9OYW1lIC9GMSAvU3VidHlwZSAvVHlwZTEgL1R5" +
        "cGUgL0ZvbnQKPj4KZW5kb2JqCjMgMCBvYmoKPDwKL0JpdHNQZXJDb21wb25lbnQgOCAvQ29sb3JTcGFjZSAvRGV2aWNlUkdCIC9GaWx0ZXIgWyAvQVNDSUk4" +
        "NURlY29kZSAvRmxhdGVEZWNvZGUgXSAvSGVpZ2h0IDgwMCAvTGVuZ3RoIDMwNzQwIC9TdWJ0eXBlIC9JbWFnZSAKICAvVHlwZSAvWE9iamVjdCAvV2lkdGgg" +
        "MTIwMAo+PgpzdHJlYW0KR2IiLVZHQzEtOnJyO3BCLFUhOlc4YSRfMzZCcm4yI3VbIWZRUVFXayRAdUpKT2ZvKiEvKm5tSCUwXkhEJ1NRXilNP2xiZyJba1Uw" +
        "P2tjXWM4L0ZUcStkQFlHIkhubDtdOVtPLFxdO2ZQMVk4JTlrKjRzInI1NDojRW04NyJcYFQ/Il1wV3FSbygpa156enp6enp6enp6enp6enp6enp6enp6enp6" +
        "enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6" +
        "enp6enp6enp6eiEuYU9bYktEdGxuREptJSRcZjA2aFAzVCVXMlFZTkxrbFppOGQjOCJLU0c1PXMsVzIjKDRTTVdgLyUyUXE7Zm5yNl9fOGxdKnBZRzJgTEtn" +
        "czhLS3BqPEkwVG0rJi48SypEZS1RUzJeT0FCQTwiTU1fQDZYXWVhJmFbNlMlb1s2QWNNK087I09Ec1tGNGtGay1Xc2V0LjBDZWJqRHU0NmBnVT01SidlR1kn" +
        "PzBZOCxgM01lK2cxLTEsPGNGN1onLjdQaFRrMkU1JWVbWGJFP050XiNuSVFtYjAhViIrdXIkcUxtPUhRRG5ZNXVMZCxuLCZOZixOSiw1P3JhMGctW2hBOUxD" +
        "VGg6UyIlZGNcOVNtUW04YFZvXS40MSRYVUEhOC9sME1qPGwucCZuL1lbUEdANGFXJ29yVlpLX3BbLV1fPHI6VV9nVV9PLkRYbC9POUU0Xz9Eb046TVI6aSw9" +
        "WGAqZVwyVSdESkJMWldGa1sqaTY3Jm50SEFdcyNKSzE1V1hCWVMtJVE1L1MpamlXaiZCa2JrNytNVD0xQV46JDxVdVMsQCtFPmlLP1JxOCRPYzo4OmxJYEBP" +
        "OU1DQl5eaiJbYXBXRzUpOU1BLSVHdVAiSydLJ2RPSz0sSUU7UGJYVSM0Z10vJy43bjZFVyNUT2BzL0ptP0cxWiFtbThSLzwxQSxeKl0zY1xHXmdkaVtWXTdH" +
        "cD9lRitTUU1uP3M4RF1fX2wyWU04SjJVSTBrLyhXIXNdPExsdUNbXkBDZ1UiYjEiLzJDdTJnLVtyUmxsQUlPPmdTKHA1WURYMzdFaUIvTWRfaCYlImxJRHJ1" +
        "QS1eJjApYHI0WjtHclsvNHF0aTZiUkhdQSNta25AVG5mTD9TVTE7NUgiNF9uISU/al5RM1csU0Y2Q1xpYldtM2BnOWJSaGgxcmNFb3JlamE6JyRcNz9lNFk0" +
        "T0g+UXNnPDA8YVRWJz49YS4pWy5FY1EybTUoLVxHZGpMYzhvJzRjK25xMVhiZE5mRXFxOU9RaUBybVxpRHIuUiFbQW4rdVs7NEFURjxLaTRsKlk5VmRPaShN" +
        "aUJnJWk4PVRKXWQvQk8qMVJcUWBxc00pbXAkOTVvKVpdcz9eXG1ZXEhqPlBxPFRfU3VxInNyNGxkZnE9ZXVhckIkTlFpVUYoWlBxPDJsJF8ublkhLz08PHRz" +
        "SiVnKEpDPWZXVWZEdG0sbStKX1FsVU9IOChZbS5cbnQ7c3BhKHQ6Pj0/Qk5hPU9INy1YXiZGLU5aQzIsI0xoJ1ZqZXNZL1duOk9lYTJRJD5DYkNtR3IuMGVF" +
        "MUllaWNwWzZpWClCOVY5KSptcT9gbUhvOVEnTSU1YjplZExpOjxLK2dVQnRhJW42Q0dGKXQtMSVOUlNNNSgpIlZPVD5GckFhW2NwX1JvOU4vKGxYZUdxXyo1" +
        "MiltUXRdKztNdG1yaU4rJ08lblpWLVgiaUdNWz1bTGBHRWs0UXBHP2clIT9NXDg5aihecWA0IUZ0TWs4V2A/KUZhNCZFbGpmITRdYXJOV2tlRTIza14/QExy" +
        "akBJQ2YmL2RlLiMuZzIvYF1lNzxidTRgLmswPnJOI0hxTExybTQiQ1JGRG5sNE5MT2JyZ3JvQCp1WFU4JkoqaCQhNkIsIypcOjZlTiopZ3FGTmRpLidgLk9q" +
        "WDUvUF9oc1piTm8mTkMwYjVWRD5rS11XPkVla15wZyxzJypnJHVwQS4kZ2lFZ2JfazgjUkZQR0hGOnRBM2RSWTNraCovOGMsSllMQ1koJThXMV5pKyhvWzYp" +
        "R2hrYHNHT0psMGswOCRxbyY1P0c4cmVgcTQ+aFksbVlsTjc5TUo1dWRcTV5raD0tV09LM1Z1WmxBPEpZOkhganBtVG1yTiE8PGc6RFUqbU8/aUtVNFI+cjAx" +
        "SzNzLSxbPDtkIStzTz4hWlExIV5xXiwlK1VnOk5IZjwtX0QsJ2c/OUs3OVxnKkgwLFI0KktyciQ0OmZAIz06YURwP15IVzFTZW5qKzBKX05fV0oiJlhdaFI1" +
        "RDFgJCVGSXRQYnJxbDBNZWUmOiUtUWgvWU5zPnA6U1hrbmVLQl9YYmlgRVRcSUJfKUNLM1Z1Wm0+NDlwY0NIYUgxMVtzISElPi8uVCpKI1ItdW5NIi0vRlMh" +
        "Y0BbPixrcnFeZl9oSlUxTyZVU0JtXEJURC82VXRxK2QyJFo0RDwwcm5aIl49UCouJGhYU1hubWc8PydJRjh1NzwoR0IpL0pPb10/cjRpIj9oJ1giQWVZbD40" +
        "azUwQD89PEk4KWg6cGZDQHEmbmgydWdvX3JoN2E+MU0+IWhhZU4tLVhQNk9AMzImREpgY3VZLGg6PEljND8uVFRALzY3Vi5UTUBjTkVzNURvdGM3Y2BUOGIl" +
        "S2pNTkNyUllEK3JUVTVQKCtpXUhGUWgpTVcyUDtQQyQuZGIxNjsxSmxCK1JYMSVuRDkhLlo5MXJIWmFjbUx1KEVvOlJkYG9QIVYlb15DXHBaIWtmJDpJbiUn" +
        "WHIvLHJITWREUUBtZCVSYUcsISFnIVROOjA+SGBFZChvNW9RIjdUYC1lLDUjbz1PSiplIzlVJHFmXEE0QmFWYjQlRmFeMFVRWnJWMC8ibi9tSV1ZbENZLF4o" +
        "N2hjTEdUXEdJY2BKYEp0SCg3RCtqaVdrO2hFM2JXPVFBXkU0YVFiSW1EL1ZmXlRUXi9MM1txQlNhOztOWF4mRDdwSy9zcjwyZjxWYHVqTCpqUVAvV0ZtQEs2" +
        "czhJNihAcHViOStCT3RISFFtMzJcOF5FUlRnUy5zWFcnJy5OKEQpSFNacyhwRy0jU0ohISNIUjgrTCVhOmx0R2s8a0dGaE0uPyMyXiJPSlQ7NS9gV2x0Mjty" +
        "YGRJTDVCKzVCNj02MmBTY10wb2lvQUlxWnMiI3IzKkJTRy9SMz8lJWYiI2o3aC5nRVxRQysmPD8tOjhcWj44J3BkKlBWRlQpQUJETzdrJVM9Z0NHLT1EV0ZM" +
        "bEVlZiNlKHNgNiM5VDJQKkpTaWUhKyI0cEI4Q2g7XFQyOTshdGgsTGllXm1LREUxWlkxR2ErP2dwcl9NVzAlPXFiJmUwOnEmKzU0YC8zSGpMUHRyLGdFMj91" +
        "UmxCWywkMzNtWkdwRXBzQnIzNmMtUEsiZkwoMU9QQC4xRm0yVmt1aDpcVC8hKUsxJnBYWllmW3JuNVwsRnJwIydaX3FePmBmMXAxNSQoN2FFVD4lQCVtNkxo" +
        "WlZSMldnMyZwWCxoJlZiZSpZME9wJDRJYjRTJFE8XGVSRiprVGVWWT0wR3FpTGtzRjcuT1pdOUdITDlZRFx0X1VHL2diQycuPCZkTCUsJDNjXGwuR2BjYExs" +
        "Z3JoQGNJLjYmW0hPW0wtRkg9TEMxWltXPVBFSl1OV2BRQnFtVDRvPiEoRSpMbm1GXkpKRydBNlM0anBrXiVcK0EjQz8lKytbOFNnMkpwbUY8aW9PX05NZz1e" +
        "PlRQYSFsdEEoMSV1UmNZbWFEWmJZb2MjS2FKI25LWEU3Z1QrTmlEY3A4alBOUDlgLks9YTAnbnE7YDNLJEtvYS0lZ3Jjb1kjKV1PbCYtPzIuXnA8UUJUVlky" +
        "W0QuKFAnJkc8bTAnbk5PbDA3SERUREtBN0ZRbThfO2duNUliMmFuKyRZTiIiN1lePjBbNFAndVpuREVwZjZKaFZxVkQxWVAiJlMlJzhNQVBnMF8jcDU/MDpO" +
        "MjxfMmZhLF8+MmJSZiYoKnFWOGFsdEEoMSV1cVNmOjpMI2ZpUFErKCpdXDg6RFdPSFItPj9GdWFYIzheRitpc1JGb009N1xkJmldJzRZbW9BXWtrRnAuXnU0" +
        "RVBtYkIwLFsia0cvZiU5JFBxcnQlWyN1JFNpcGwvaENaN1tnbWVWJzQqVSwiQltLLmpFLCpxJ0FhM3A6ITw+PSslPShlMWhoajsqQkFuNnRraGEiYkcvbjQ9" +
        "UW0iN1JsMDdGWTk6IidbRmxeVVVCODRLW0gtZksmYHVcMU5AISRMdG1WJDprP2EvXzFLYVlLZDclLHVLVkdZL1VQR0UqUF0lVFNwaHBkc1lRUkRBL282J0JN" +
        "amttZy1VJjFvJTBpXUsrPmpgamopKmYhXWBkaHNfXG5VVjdHamk/P2JoMDw8PS4uOFhFVlUiVDZgVSVYUGQibE4tQVRfRiEvXmg6aj9cY3BDTFpDPllINz5Q" +
        "QShlLyFnKVh0VlU0cGYmWF1WaHBdP1okPjUzViY9R3FdYlRsS20vNT82OSU7ZjNgWU5HTmREaUU2Z1VnbEQuWWFcKTJZQFwlLC1ZTT0tajYlTklJJD49OG1h" +
        "bVtHSCQ8aXBdPmswOi9AYTZGX09wOHJCUG50I2woPEcqOzpSW1U4Uk05OnBBT0hidERRZ2BYY1gzPUBXUCpkc2tBP0pGalZtJDQmTUFlRzdfOzJfSTduLHUl" +
        "cXJzQ3U+QVRVSjUmbUJyVmpBbDU5IlQjXk5aSFROZSU5Z0JtMzksS2I0K09Va2MobXVPKlEiaXE9PFhZUSJTRjdEN0piTWtLX1ZbXk5UIUpIXmQpXThdM0Fk" +
        "XTBeMUVXOG8wa1ZHRWhlSFJ1alZwIVFQVmRGJD9jbihyMicqMCdOaCFWRkdaXk4mOidtQD1TT0woJih0SVsuRmMsVVxFZDFaW1E3TW40cmdYXiZFQlVMYUgr" +
        "S3E5N1NpNGFYOigraV4jbiMlYUFbXj5pNUhePnVmaDNUbG1TMmFCWF9OY1dRIVdyTl4uS2ZuWT8ra1I3YVdeNF8qcVxDRFosc1hAcSErb2BsO21vS1ZrNENs" +
        "PmtdZnRfTHI0L2x0dTFnLF5LLj9HSThzYjR1VXRBMmBGaCk6QThfN3E+OicwYTJeN3FLJE5uZCFWRkdaczBvY0E6JXRIYj5lLztFcWMyVypQRk5tRmJuLTtw" +
        "Si9cPGgvY19PVUYmKmskYWlVTWheVjdBYzJTWCtmUC9PVkJnR1xINWVaKVdtQiY5ZV0xTTtfayJtdEtVOC5MYjpnPkEtV2dwSUM1Tl9TWnQ2L0RGTiwvOXFP" +
        "Vl01KCJTTkQlO2hbblZJP1Umbiw0KmAuP2ZXZGc8Nl85SjBjRWZLVm9PcjJfWV1gPExVSVVBZ0AvLThTPDxNSEk5ME9bTDdea11eO05fLGxsOFlxJWNiPytE" +
        "WGdkMDgwJ2k4SDBFMihSQC0qdEIuJTVTW2pkNVpaTGJNYHJUaz51TCo0YFZqKUJYXkBkKFk0LUheQTlmUlBoXDRuRkFdL24iSSd0UEJoRk1wRlRabmxJN1FE" +
        "RDJXYDVsPW9eXTlxKyc3W1dWNDdiZmUwQ2xmcDk9Y0UoPGBHMlUvZVxyNVklZk46V1NhLWgzV1gmY0AwWm5zIltCVHVSLVRMVVZbaCdZbV4wNDNTb2koRSNa" +
        "UDw5MjlvJjA5IkokMy8vOlxtQGwhLl5mXUQwXHRVYFMuVi85NyhMay9SInIibGAubFhARFcwN21fLywsJGhoImxJSlU+RVtjUFlzYktTMlttPVkvcjV1NCZC" +
        "SyQ7RGpdJ2k+RT1AUG50ZjwxXDwvdWkhVkRKcmFDLUFiMVdldV1DV1pbO0g3Mi9oQCxmSC5NPmg8TmN0M209Vy9jbSttKWBKUEQ8MmZJRVdXUkpiZEs8JV06" +
        "LXFxZDk4UClMYnJJIjpnPHQvKzUhIy9tW2JDZFtvYygiKy5NMltCbSJZM1kjTm9TMEBDTmhFbzQ/UFxlZ1pHci4tVnI9TTxbMTZ1MWRvP24wRyE7bWtqMCFv" +
        "PzYwTF0kLjFCW1VJUGo0aDZ1T3RYLWBONGArNjNgWGEpR0ltYlBKb0lcYyVVIXNjM0o1QVNgP3JxJVM1NllNdWslQTYzREgvNE46Qy00Nic8T2cwc1I/UnFw" +
        "aFJvaUI8PzxrJSEpUlpKKlljMi1sIWdqTGJhQzhQQVIxWl8+LjhDT2g4P2I0MzFDbFBeLjlhdCtbTXQtNSo2KkE0YGtqSGJmYlhNRF9DQTFcJCtiLj48TVQu" +
        "O3FlaHAucz85b1JQQENWUHErZUFxWGlSdT5cPU9daWwkWmBtRjpcViQ+OSdHTilwSV03a1QpQyk7XzhlKyIlN3AkPE5XQG86QHE7cVtZWGcjN2BSaykkIUs4" +
        "XE4oMmdmc0FfKlpoITUkaG5GTm5nPHRcM0EpPWtEZy0naVtsKStvLFY+RENJVzJNLGQlZXJYO29aNV1fbmBgLmEtQmUnVDY6KyQqRDxvbklkXEtIdSVtS2RI" +
        "YGYxcDQqZylQUFJHImZRNmh0IytmV2AzZlNKcEVkIjZjOyQwSUEtZGZcc3AzLlsrQ0AqbURnTmRzVlZKMVhBMG5USj9NPyY5RG42J0hWYyVFSTA4YjpTKUI+" +
        "ay8sJyFQKjJSPFVkVko6XDk3LCdDOShFQkd0JVY/YC46UileZlUpN14qaDNkJEQvLFdNPE8wZXBZVUpYUjBQYCUscFhjIjUiV1kkZCVFWldmcVNSUzIxVSdc" +
        "ZS9rYkphZHM/bUtAZDs2ZkJwIjo1MGFUO243TSs3SzhHR0FwPT5PPEA6QGNqPypjQS9EO3JvaicpVVwtTWo8W05FY05YMFdoZSYhaHFxZj8vUFxLbyM8ZCpO" +
        "UGclPSdAWzNsdGYoaUNhWztYci9IUF5SS2RSRVFXWzhsREEwVV9bWDxOJmExM15fXUtXczZvcVFiNC9iQi9Ycj9YdGlRcWpbJV44VEYjLEpOJW4tSCEpU0te" +
        "NTdcKlIicUBsaUdqTyhmUThZbT8qKyszb1s8PHEwR09NXE5eTGJTKG1Jamh0RypVc3NRYlNTZls+RmUsTmZuWzVZXTheQ2cxQlUkWF9ZSCQoL1txK2c6TSVT" +
        "XSFdNGc5aEd0bjhzUyxESGFmM1otIkxPc1BVb3M4SFhAa0RwbkA6QDE8Y2QnY20rbWltX2BGJEAhKTM+PE9nMHNSNUA5UT9iJV0yUlBpOFJBSDR0PWspaGNO" +
        "cVhqJEZUSEtfYDZVRipqWCpyU0hOcFErS20wQjxpIjlfdUJHZ1k3RXE8K0MlNHQrdFs2bW1jMlg3RHFcY100MDZIMTkzTys0VnRvaygmWTZiMT9PUmNCRCRm" +
        "QmxBKGFTcFhZU0kvM0JzY1BvJiMvKT04NS83Ni1IcTdSWC08PE1ISTk0JGIvb09sTEFGbnBLT1xCNihiMWRgVGFSXFxdZ3AkOjNLVyFpWVNgJnMxZktibic/" +
        "Xy1bI3RfaFtFRU8+cFFta3A+X0c6N2pgK2RQPCRsVUsiM2opRCs8RDZeODBgZEgrU1gwXSpPVmEzKSVXVztEOUZpTVc7czE+SHEqUC5MPDY5VUs0K0YsKSph" +
        "aDdJbU1fTEMlODxCWV4mZUQ3K11yQGEybWpsSUBEMV5kWzpLQzBeRU8nTTcqclRkZzUmKjM/LjVCbHBQUSs0c14mJmI3XWRePGpsaDByXD9FNT1lTiEnaF5p" +
        "V0I+RnFpXkoyaTU2cVorNEYtTG5iN2hxQjVRQGhAY2ZXS0E+P1puMVBCS1NoKy5ELzBILWxNJmEzKSVXXDg5aFJoOSonOmNTalIiaDhlLlRDWSNTbFwlMUho" +
        "Wj06Y1ttRGdNVS82XUYlbj1gdSFWRUNlZmBXMT5HUDJFS1BLQzJVVGBsPlQ7XkloYUJPcD5JOSRrX2AocExQIjxYJmFBTS8tKT9pbzJIYkZgZkQzW0RqKDIo" +
        "IysxTjRGPEAhJW9dVDAiOUdIKXJwP2Y/SDNqYDdkYkNmVl4zJy5vPGpoWGxuOj9iZ0lkJVEqTlNwMFZYZnBTRW4xSidlLWxuZEknPS9cczNUcHNhPigiUEw3" +
        "RG8pTCNqSDw7MTheW0pmQVZoJEc7SCRNIl4yIzclMTxFKnNgWEVfQkM2V1RIczg7JUU0J0VDIjshPDBZJXVPcGlSNS8+aURQSSJALnE9RUdiKjRAL0FMQzA9" +
        "RTopQDpIZihGJFcqNlUvcFVraClxLUE/bmZzKjRhJio/TUlNOEd0LGBbWmZ0a21nQThWRy9vdDMmc1A3JU5NRS9ESyE1XVFAVkUwLmA/VGRocGhobVdDcFJb" +
        "ais6NC06LFFQNTxhOTJrYTVaWS1FcTg5X2gzV05xUmw+OC9XJTdxQS4uQ1VjJ10rLHUqU25LRkNiaDs5YCxcM0EyLzUhYWYkc1hcZF5YO1cqOFJlT2sudUdT" +
        "VCxlOzxvQUpyPF5UTGRnYC8jITAuX2tjPE1uNTJScnFGMEM8LGNsQCxVWHUqNTYlcypmczFlPVpSTihzazxbXTBUVmpGPE8kRVZHSGJWbjgxMWFfXUhtKmpI" +
        "YiUpaVVDTS0xQT4kQFtqTkg+RSdHNCIiRHJhUjhUVElCO0cqX11AKmY3KHU6NGJhV2pfW2V1NT8hNm5CPzZaVTVcbDBdcUtDMXJjcSdQOkBqMls1SCgoVVpW" +
        "My09QUdVT0NfbFM9Vi4wckZlXmEtY3EjSic2SzpRYio0PWIqTF1tYmNAYnBGKkMqRCtvJj8pUUdzcUJUXG9tPyRyOj5YL2lAOiVaLFZHVDFbcjowVElCOUtn" +
        "UzJrWDlDQj1LbidbUG8tNjJWMDxvLCFnUGlQYCFDa3UlVnFaWUoxIVYxRzM2QkhHQkFxcTkoTTM9Y0hbSE0jUmRobWRbZGxkcj01KDsrWUlgV0hgNlROaCNF" +
        "LV9BXGZqODovbzhQLj1DR3U6OCVLb0NzIWFbOHVVb1FlZjsiNmRRbz83KWxeNlxjL3JhaVE5RyRQPlgxSUopLUMjN29PKF5mQDZuOFIrLD9OWjomV15PQ1Vl" +
        "T1YtbS9YLV0qOix1WUhXOnQwX1AldVMscUM9VD0xb0BLQ25pUF5rTVQhJXQiXEhaUD5gcS8iS2tpVDQ4RGdpYSQ4NFxoJktUNyc3YXVnSWEvTTBdWiE+a2I3" +
        "cllnXS0kXlhQTTBrODVlRy9qJSMqPUk5OmJdaGtDSExSKjRGbUdAU0Q+ZmloKGU1WiZQYVJxIkRyKlRMST5xU1M+XDFMMWA7LHVFNGFKcUMySEp0PlZcWFok" +
        "Ji0tKyFhN0VdUWdpTTRMUzRPXjNuIV5eNTBKTiI9UHEqKDRrZHUqb2hzRydzbG9aWykoYmU1Oy11Lj43XFNeKFpOQjVoJ0RqTG1VMShYMilhWCJFQW5ZNUlK" +
        "WV42IS1gYmxHKEtmblMhIVZHOnIwOEw6OF1sdHVobG90Qz87amVTWThQKUw7LVZwbU1cWyQ/Xm05Xmw9QV9OP2RkQFIvM1ZkSHJRK1liXWttRm5ybE9MQW9T" +
        "OSkkXm84bjVvJWVOR01iNWB0LjtHcV8wOT02QC1hOSRTW2xaMkFRUz9XT2BbN14+Ty5MbiQyP2pARXAsbTs2TFYlPS5Ic1s7NT9qTVxQYzRldVckT3BbPms9" +
        "bCZvMCxNPnJKNShPbVkuPmQ/XCxBRTheaDJmc0UuV1dZJWNgIUcwT00kUktzZ2tuRT5iXWhHN0hMUiosR2pgLSpqOXBqMDJmLi1OMi9DZCFcPDM7T1M9UTMm" +
        "QidicEFCRDFjTF00QiRIaVBVSCpyIWY9dFgrUkhISF1qYVc2Vz4rUGo6UUIsQVdALHU6YypHYipZZDItPk5hQD9FNl1ePSZUR1xKXzRmW2ZJLmMja2BxSldG" +
        "R3FlLWhCPyhJLDBgVi1lJDBdKzVmZDJMbDAtMjJXMEBNNEtYXTM3cD9NQ11ERm1JVTluRDE+bk9wPjhKSjAqJGhCa186YD5JUU1vJXQ1LyszazM8aC1BZ0xK" +
        "QCZIYmolIkRoKiEpUSguOEtKVCJadFVjYkdGVUlHNls3WE4ycnNeRTs2RjA3YmZpYWpdQ1lcWFIuJiYkai44QF0vNlQ4SHJlUU8ncFw0XklNXz9AUWoyK1Mv" +
        "JU43L3BbcjNjdFhEaToiTUZcZjRHSnNRLjQ6Z1QzSitBR2tHUDMlJG87ZE1FYl0kP19pcCFBNV4zZlVXajpNQ21NQ2U1PEsrPDxnZSZhXiNvMGRbOGFZZkdb" +
        "S2NnbzttSGg+NyVnbjQyMEBwKGxJYCRiaEpsXSRaLkdsIVpZI05AV2xALTRARVQ+JyFeMl1oMGo/X3I2Y2w7UEtPSURXbFdEYjEuXGZwbU1LPCozdTpSaSs9" +
        "KidAZlQnX0MwMiFXWFdCXUkrM3NoN0lsdHI2KkMtND1FMmNAQihFIXI2YiNKJDosYFdHTHRNZkItJWFGRHIrYTVJIU5pb1Ajbm50YFVDUDxjQ3BpPnAnRSFt" +
        "cVhpMiZqTFRMIio5cHRbYWIwQ2EmIm85TV8+TzErIjxCQDdQU3I7PXM0K3FjQXRVTnNraE5gP09YIUA5KUFyTj87PENsKG5FNkVuUU1nWCpxKWdXXG1UZGte" +
        "I3VPUHNjS3FUTmo/YE8xKkIzL3RyOzA3dCdITkslMWA5YmZHa3BvQTglW2E2USlgJ01DPVQ+TDZUbHR1SC1qWTZvVVwwWVoiOylvbk9xKVtJMF4pYSY9OURF" +
        "I0lxbnJtPjFsQ3JyKkVtcjM1bmQ9b2VVT2FLK20pZCdtcSNSRDhOZWNcIVlxLjckNmNQKDFmbHAiIT9tbSZQKmRfMGlfXipWXF9fJGFGKV9rVz1xY1s7NEM+" +
        "bjcnVydEXl5eWFQuPnEqXjBtKUdLREVlJVRYWCNLK1poYzRycXVTN2xJOWZvbiJpW0RJZUQ1bVBFTSg7b1JYJjpeKmlKPDY6Lzgqa3FbLDBaalJbJihgTzRq" +
        "NVM8MHJIMTolRlVCKEFyXHBqZkNUKkwoO1FsKTcrRD5qRzNjXm07Y2JgMyQpKmQjPFFuUGdSOjg0WjhnMGssaDZGN0xuND0wPSsiX1ZWdFFTXzpFbz0vbj8q" +
        "bSpQLUZrS11YWyRQcC00a2ZhJFFAMzJlc2MtPihPQyIlLkw0Vm8uXG1hK110JSIucDlWbzopWzkxcShSRzcxUS9SQFtOOl5PNi5jIiYiayw7NkYwN2xJRHBv" +
        "Q3RQdCVhLFlURW9RZkRMKFonVDdoLFVMVixwanRxXTZEU2gwYF83U0o7UkdibikobltVUTdWOG86USdcbkE+ZGdYJmkoSmBeMyFZYDdkb189IkwvQlU4I1N1" +
        "TWs+SXJuXShrRSo4bU0ibjJXS2gzVyZPOyc5ZGg9bkRRTlZvU0FOZigzciwkMWMtPHQ1bD1dOllQUnFYTV1hNmJhOEdjKWo1XTAqclc1MGExJnE6U3p6enp6" +
        "enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6el50OEFfISEhIUlsb1o6TWkyJlAhMGBfN1MhMTsnXFlsZT1UZGg8QXAhISVRNjQ8" +
        "Nktgenp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6" +
        "ISEhITgzLjFNalp0W3V1QFVeNjdESHNWZGZxPiIyaThFT0xMa2xmdT1wdUIxRTtvZTJrS11XPkVZbks8aC03Uj1wIypaPWBAYVI/XnFgNE1zKFUmYz1pJ2dC" +
        "XFtgWTxEQihRTj1PWC1BN0AwOCtNXFJhU0QpUVYrPmpKImBXYDcqXjopN0VlNTVWRigsVGhDIjBpKiFgYidXVGpCQzBkPXFYbSMtKjw2KGksJjZgM2MkZyFB" +
        "SS4yXWJITlljQUYrKyZga0tUSlRwWEEwa24pKDZPW3InbS4pO0EiVDY6LjUoRm1LaHJRajZFWDNuOk5JOTctJ0pLNFo1OiFXVzU5KSJPPVFxVzQmTj4uOENP" +
        "UmZsYWZwP2U/PlNGIlVfJU5JSClVLW88SVJdOEpwcT08V15lWio+ai1QKVlzNDl0b2UhK1kqXWkrb08wQW83OCJLK0dRIlA+TSxAWDA0aVc2ZmhnTjtVcWRJ" +
        "cFs2aWZeRnRSLkkyOGhGcSMnYTBDVVlaJVJYbVIsISEhIi9hRmgzPGZzNS9pITxLM148TWA8UWRBJzNNaDdFOi9XRGknR2tyM1A7NEVnJ24+WzYoQl8xVHBx" +
        "LkFMVnQoKUUzdE88Q201NGFabE4yZi1tOWBRIWxWWT9ubzJQRVRMX3JPJEZtSnFBVFVtNyRfUlxvZDFkcnB1Y0tsdGkwWWFiLWBfKld1SENKMi0rTiswcD1s" +
        "cDB1IXUuRjInZDhCPG1dXU9OVTYkK0ZnLilFRGk2XlZATyhYNzVXSlpZLlZMbVM2YGlER21LNldLYFZdbUlva3RZPzEvUmtGWzZJb0RpPFkwQlBQIyEhJTZH" +
        "bnQ8MXJFb2ZwaG8mWixhWCRWTElMQ1lKbWZldEg4OjNsInRCW3MsSl4iW3A4IXNdPUNbZj5BMHMvPE9HQVVeXmRdPS0zZF8layVEISEhIVFMLTRaTVBcWWBB" +
        "PmUrbTNNMkNlbUorXz5uV2RCWFtCP28jYFVmWGErZiNGSVNOUkhZJWZXZXFnbUNSSyksMlk9PU0qKSVEISEkTk1LMEo4QFRnUGorSSIoVikoUUs3SzFNMlRP" +
        "Nj9tYk1tRyMqLWczO2kzYWtITlQ9J2o6VnMyIy1zPVBKJmdoVkhUSV8oYkYmISEhIlhuT09fNkU4ZVpjIlVlZEtAW10sZjtSJ11NVFYucWInZTIlVGU3Y1hK" +
        "IURBXzBkSl1aKlkzYk5BJ2U8PldsZHI9YG1GaWpZUFVLa05HW3RAR1A+PUJRRzQiIz1nZyQ+PEE3XT5TPmUwRilWazBuZVZiVW89VzdYXlkuazdXKFtFQD5R" +
        "bVBKVigrUVs8YGpIMnFAJ00xcWonQj5hZTFMbkU1TlY0QVNHJyMsQ05aOigtNGFIUEZWK1tHL3A2YzFQYydwWXE/RzNwMF5aKEpYWCZlV0hMRUBRckRWVkMj" +
        "XSxXa0NCJFdxPGM1YCheNElESiVbcGQvWW5eRkc4LXJFXmcnVTliZGtiMEcoVk9UbCNoVSpDJWJBLTRjU1diQnAmbj1xVklmNShZI1ZmQGdAOjskL2VuajZY" +
        "clJlQWJgUDFjcl00XlguKkBMUWdCJEhlLVQ3PVJwajJSb2dRYVQiaGFedU8sRzQiIz1cVDd1Yyo4JUsoX3QpcDQjNnRLaEtTNS8maDZebkZCOmFCXHFYaiMi" +
        "OlMpWjRbZkhJL11TXlNKJ15JODY3dS5TTVorRyk/Yzk6cFdGL2lHTS5dI2ImUlFQazlIQCEvP2tBO2koW3Ina0goOSRxYU1oUzRxSzwhNG85cSxuSldiJCs0" +
        "ZXVgLydHLkRrWGBYVDIjPEhjTFJVS1pnKFJBNmkuKD1LMmVnMkBLWU82I24kYE9RcEdDbWFMP1k2QmQ0MU5WXiZOL1MyJEMrYyQ5TCxqYFsyRDlgOSM3bF0q" +
        "QnMzdG07PCMpUkArPVM4Qlg7M2hOa3A+UU1zLihwNmJpTlUpZFRzbVUhLUJXNUA7cSZkO0BeTjxVP2JvI0M/LjRYa2BYJFhdcVBpTkRiMU1ldSEnKFJdVFxz" +
        "X09ZcmYhISdlUlwyK1hKPklMVlhuYCNeU2A6RTkoaGdYJkJJKTFOcmlsLWd1ZkA8bSU8YFJ1clI2SCYxRnM9LyxFb3RSYG1FUjdjWjopN0Y5LyF1UzFcQiw2" +
        "MTAlSmZAIzEjNVJbVTY7ImZlTmVjJDwtVWd1Lip1cTNsQmZjSUpsVW8lQF04OStJdVtLYiwlOlZ0cj1UUnRfPWhVM2VPbmpAXTc5PVVaOTBuQlJSZGcxP2hz" +
        "S0NlNk1aWmpZRERrMmcwXU0+IlklZV1iM24lUjk9Mj8zX0IrbGchblcqSzxuTU4hS2Y6LzlDKF1QPS5NQV1ra0YwUEprQkQtUDJTT1tZMEQ4VHIrYk0qOiJf" +
        "NipZKik8ZHQpSXBSUzkhJywuLkJWVEtdXVJnMTw5U0VPV1xOQkNtb0NjJnEtI0dvIUNpb0IoXDFUJjVvZzc6J14jREhmXkd0LEVJaUI0VyFySGFIUV8hME9Z" +
        "VmUpcFFiZmc4IylFP0dmbmZSbzQtdS5QJCNKXjMtOTMmPDFjJ2xuSzRhWzUuckAnMEVnIkcnPF5PLFxAUjRyJyRsTV1zMUhNITxzZy9OaDsqQGItbkQjKyk5" +
        "KysvJWRfTSttYFQ+I0MwY0UxPFtwP3BgdUhMdGxOPCpbLmJxMmxtbFhTNU9wW3IqZSdVIkNWOCxOSkxiZ0w/QERVSU00QD1oOF5TYF5sUGIjOSxSO1czIVhJ" +
        "Zi5MXjk5cT09Rj1cWFFEalxOUUcldD1CPlZsS0kwTVxkSHJGUWYvTkNRYWpEZGFIUzNoUyI6I2RcV0tyR0RaXl5WK1ZXSj0zOWdqNidHb1VVS2klP2FwWG5R" +
        "YSxcXU49MUEqV2g3QGBBY0NCMVRuRFA8RkRNV1pjYikyYWlET0gzNEtyKFpEQ0BvdUQjOmlpVihgT0ZrYVg8XjpfNz1nXFU/TFJMSGhYW01eT0crVkpIWkRz" +
        "RlJJcGVGWkErLE1tNUNzXjQja2MnZTM+Yl5Ma2BaQFBXbDBnJEphR0MiJjw7I243PVlRZlAjPFMnSVdqMDxUOmZjVkwpbVs8Q3FARU09QzBDMU0kSipXdUhD" +
        "Nz8mN1gtWmYiSj1sbTYrRlFxNXNRN11HcHEhLUQxaDdAYC1HM1BZIXIyNzZKV2BMa1tycjJuKypHRyU2aE0pSVlQa2hJVlJsNGA4L2NKNmtaWS5PT0FUSk5p" +
        "MTpKZ09YWGU3Sl9fYGVRQW4+TyZdLk5rKGYmPW1nJ0ohTCQ1P2tSNEJzNEFkY11cc2dgbkZgRCV1Tj9BcVdNRzhTaW1JMSN1cDArS2FYV21NXGUkIXBgb0JC" +
        "VUkvOjU6XiZLVEBKb29qLDIzbEwvQ0lBSFkkSTtFWzZJY19SJGRbJEB1bElcVVJRcnJEVlYwMWdaW2o3TkViXGZudD1OaCZMNFlJUDFsXicnNU0oaUpPTEAq" +
        "bV50TVFZcDVDRltANzBzISMuMithRmhYcz5maS1UJyt0cWJnWV1ca1tvS0owZWtJJj9DMU02Xz1gQyg1ckUyYShAJ1EuYEhhXnBAWipDUlZgckdrXEoqYVIi" +
        "LjFfO1lYS2xaRT4uJ2tPUiUwXEovNktXNkgkXy1WLFMqKi9gIlU5J3BbQCJzQiQ/QkpoSkA6aUdtQG44RVQwQkdwMG5MW0s6RFIhRj50TzA1JG4lXC4sVExO" +
        "WGhDJUdqP188am09SzYyXD1tYFJDIiJEPUxCQE1WOyE9PTwjQ2xYPDJzJXNVazAwNTxaXidmdUhoS1dIV0VMXllEVlxMbig1PTY8PiQtWklZa0lYOmhuRk5d" +
        "cDBXPUpbQ3FrUWpoVyRncikkVHRqKiUjaWJmaWMyK1A5PTJoTi51JypCVS1nJDIqVDZGNjY+bFZUcVlWWSEmYF0hWisyMEIkR0A6RWpSYkM0YU9LQ0k7ITtt" +
        "JFU0a0haRTpQKlw4Z1FxMilDYS1YJlEnPWxkZFpUNHJjZTlndEgnQ0ZdTVAvXzFNYWchJUAzWGNAXiZLb3AwWEMwRTQiVWpbXCNtJlkhVzokVXBJYlpzMTpt" +
        "QT9nPUY+MjtvcVBxLUthNUMpZzFhbzhvMGxKbkdrXG9pKS5WV3ReXHAvXWYlTzlcNElFOmVObDBRZjpyRjZ1OVQoVXQkQjA0MWxvYWQrTV9QYWUxJkVQZmdd" +
        "PT1POFlVZUAlLHA2bGxFSzlJV1ZkZD1yNyw9ZWomNzM5KmdwVEJXJyE8PCpKIW1KckJiLSwwMylEdC4kQHJLKldWS0c3UVk/dD9gJ0VcQzxEVnIxMT4/OlJM" +
        "TW5tK25oLGkpNG1HIytaRFh1ZUYwP1BBZzlEbipfcyYsR11gdWZmZHEzaG0tJl4kUERZP25XaWJUJCtcUmw1LGJENXBbQFknXG5yUyNOX2tTbmVpZF5WR1BC" +
        "b0ImZCc5ZUtiYlcxM3JTcjojPyZJQl9HOkZmVm9kYEpQRHQyKUNhLU51Z0hhZ1A9Oy48OTVHRj9QKkdBISU/aSNjJUZeJmZxRys1ZXVdcDwoNU1INTZcPlJQ" +
        "Q3BtMW4nXmA7KmwtbFBrRmQhJU9HWCxqU2pWKFw2KnAhcEstLVdrYFctWFZgZkE3VEM6SlZkcGg6YTI2MkhJO2EjLiVzOyxoRiolS24wVXROLnU/X0N0dU50" +
        "QWgwISM1UHNlJyZaKkolVyk5PFpmczUxc2hzXzZ1KixNZ0NZcmlqPSgrL2spPG9tOCo5OXEtPkdrIzgrRiNPKCE1aUgzZipeIkglS1gsaGY1WDcmWEpILC5A" +
        "WWRwZEBHRihWKzp0NThOUmFSaXBLPDFRYEE9IS9OSWlnLWUtOlZTXChZWjMuTyljWT1zOEhELDxnSUtGXiVWPlViInFzQisrTzNuREc6UlBjVUxMLzRYXls/" +
        "KFA4PC04KV5JSlgwYGZuLVJURUIxLjo3RnBGPm0uIWJzN3NbVjQmVG8mc10zU3VxOUxwJDEpSDA8c3AhW3FlM3EnRUEtKzgyMnQ0V2o3UmxwP2pdNz87ck4q" +
        "LTA2SiVxWGZwJ0FBRV1yTmMqaUNCP20jRSVSb2Q9W0BKa0lIRzQmXDciaTc+Km1dSSdyOiM/RVgtMzRdbG5OaUY4b2o7Yj1XIlhbbnBnWnM7WE5yMFdhNnQw" +
        "TztnLHJmVzhkPCVzST9vPFYjQFpaYTJgOGtbPGlmW1ZPSmlyJ0w7NF5dKj9VZ0tPWFghLjo/OkgxNztBXittJ048JTFrTFIqR0lyK1YvMHNBaCJuXG4sMyVm" +
        "W3InbHMqKlxDQS9mcj5rbGRyPUFGdTtIST9GUWQ5WlktJFlmLGcwMVdIaUlLZXVkKFZNMmxVbipnQW4jcjszKyg8SVdUVFZOP24kVms4Rzdxc1Y6ZDArLmFb" +
        "Om1kVz0jTTYrYUNOcF9yPDBbIUkrQkhcLmQ7XFsoKyZmKDQ8RTomJEdkK2xJaE5lQ2puKHQwTjtoTmMwOzNHXSpiLzFlUkdqIUFDKUR1N1g0IlJpKmhuVDMo" +
        "SDFRNEhnMyo4YUJwI3IqOltdIk45JWknUkgtZGBmMCE4LU46OmRAMkJAITBhclV1X1VgO29yPUklQDZeOnBpKC5JSjo0R01GXGU5cnFZYEJfXkdTVzFZKT9x" +
        "bUFtPWNuSUBhRGs8W15LWypuPG8uKjhxXjAuTmlTWkZATW5vdCNQSFo/QkMkSTk7UW41bW45MWtLZmRYWUAjJiUnZUIuNitYVVZxO05lPF0hISEhX2IxJlFL" +
        "YjpnVVlNZ3RhJWtIPS5HSWhlKS5cODloUmpZMDpqKD1mO0pmPDs1XTUsZSMyM0tOPmhmMDROIm0rRWYvbSx1TCMyQGtdOkJQZUN0J2RgR3NRSz87Vi5oQyEi" +
        "bnQ9RlFQOilcSj1ZYVs7K1FdWG0vX1tbUG1zS05gISEiKk1DKigkMWxeJStSRGUkZUdFRiRXYlgsLGlRZUM3R3RMdGZDV1Q8VjtjazA2aVVDIjApRDdVcU1C" +
        "XzJPPjNUVipFQyleZiI7QC5uRSRqUCM2bDBKPHMmLzZVRVRCI11jSTExZj9maXNrKWlvPjNWJ0ZuYj1bOkhuU0JvciZqc0klcT5UITVNW1drSj1xMmpZLDFz" +
        "bmtYWk1haklcIjwpZio/SE4ham0sJ2QjOCNHSEExaD9KazVWbC1UMCRUPEcwRkgsdThjXCJSPVtWYC1gUzo+W25XTyNIbkNDX29QWm9mYjJYKWJYYmdVOWhS" +
        "RCM8cW5RIThOVCpqalMpQHUlQS1QOXMiRXEoO1hJITUrVkYqZG1rUFwjbW9MVVNCLiROdXBTbT1iIm50OWVQYCtJaGVNOnEhV180Z1tGbU5yOjNLTk1eXCZg" +
        "JEs5dCZbcjowVEJeWkNVOyMuOitfVjJENUZqIk1SRlouQmBtNlRqJ1EzWFR1XFlBR2diYCtGL0xOPD9AK1YrcCZsZGlLM0BkKTE0QiddUiEhNUxcPGs8XDRw" +
        "NFg7OkBudHNGc2oyW0UsZFZJUkRXPSomOz8kLFhGSVh0XlYmSjVVYW00SCpLZjNhIzVYQjcyZyJsNTc+R3NgVzhIMUliT3JtW3VNMDBNSShcJEEvb2M6Myle" +
        "NGZFYldubUhXYVtXOTU5Pi1waTBqcUNOUWlWLltfT1UjKnRCT1RfTmBZM09zISEhP0pxKWwwOHFmWk9EcTBPVmY1US5zMF91QlczaG4rKlktKFhlMTRMZXFE" +
        "JyczUTlvWzZDPExFUyQqM0I5MmE+ZSg2KVRvTDBXLl5TTSRoZ0QyRktqOlpaWGBQNWkycm03ak40RV4mTG4oMkRnYEgnIVVJLzo1OyEqMVVZXCkuS2VpJUMi" +
        "W2EhIj1kalIxa2VoIlE7XUtacDVdNjViTyxmSTlQTSVfOEpIa3ViXyErJjteJXVQTSNuaTdQUC5wJCZhQk1oWio7SidPPHArQiszNDtlbk1WVkZoQjo3Wms0" +
        "b2lLdGA/K3RcaFg+IWtmREpjUkNPaWk9UHBZUUNeXSNSbUg3Izw/a1ZKJTZEPklTTDApRSNbcGhkPHBaYUhcTyxlYj40aC1vLnRjOVArNk5wP1sobWdjOkE6" +
        "KEc4cztwIW0rWzA0JW9ROCo4JmNMVWQmTmJmYlhWTCguKClFbTBuKlxbZjZTUnUmZ1xIMUdLMT48SiZSUVMyX0ZlNUsuW2ZXWk1MLm5kTE5cM1BCNWxEPGs6" +
        "TlpDMmxGb19SQzpjUVBaNi9mcDVUPHUvaTtzTXU8P2JtMFpeJ1p0RitAJm0+TXFfYV5OaHJWTWsyYlUhVjU3Si9GRDlYSElFUnAuSGdTNW1SUSMvJjhNT10p" +
        "QzlLaWovSXIxdGApUFxqVyo2VW9iOmdXQ0Q7Mk1KVmJecjlBO0RRIk0lXzhUQFE2OChbVlhYJldsPzRsOFxUTXM5Q2ViRmwpMS9STl5DcXMmUVFjXjNkXlI6" +
        "SD9zbUxBbkgoKG51YzkxVkdFaWBjcEo9SDUpaCNWZ1Q1IzhDaWttTCYnRE9JXDlJQ0o8QD9QLkJzVmpBOGVGZmVlLk9TKWFIN10nQiQ6ZztjSXRQXSMjTWMm" +
        "ODAyJEdFMXMrVkJQP1YpTyQ1VTliKjlFR1w8QTo5VlBMInVoZm4yKVBva0pNQEZDckNQcSxwOnA5bWhuN1EoPyUhISM4bWpcUiFcSWYtXVg/aG5FQVIwU0VW" +
        "NVEiPyNEYkNeZzpkbHFrPE9cOlQ8Ti9rYlhSSU5baj83Y0JeVkYlXkFEIyhYcTwjbWoiJ2Z0RmsyZW0vRUYiVmlyal82NVNZIilZJUxFRkoxJk5ANyEhISFA" +
        "alxSITRHNCF1dSkmXUVZVlF1Tm5LbD1hKTxxUW5dKTxBVmMmZWJxRFk+MTApM2UtREItMkc2ITw/O2hpLzZKU0NgM00iVDRJQycoQ2MnLVgjQSVXa2JmSWs9" +
        "ZjwxWjxobyYpXjkyIzBdcXNUZiJrVVVxX0N0WipVR09DXEUlS0hLKlFvTE4+S3JoQVdlWjJhXipCTFE9YkVga248YEluVWRgQCdYcCQ6NjBwOiVmJS9tVlht" +
        "S2lZPFpbRlxjOD8vbUImNzViWWAzQjkrQ1E3Wm4rWyZOSUlxWVUvNmxka0k4biVOamIoWihHX0whdXFxJWlkUjVpJFcsOitzUz1GY0M/PjhOLTcpMWM6NV5B" +
        "NTpCSiVAL2ctIVtZcD9NRUNYJSwmc3RMaVAzJWZjaE4uc3UxR2RYYj46NGs5bCRmM2kmOiRBcjJFIVFVPWYqcVMhISZPVVFCbnNmX1pnXS1nOWtddFZiUzg7" +
        "KztbXHExMWVVYSdsVSFGU2M4OFo1RGMwWXE7YjNoM2NtbkRYW1EnJ2ZXXyVkX25hJlFaK15ZWCVyXiUjUjdPVm0sZGo3QmlldF9lSUl0b1cwZWtEQilgT3Al" +
        "WihhNT1ZY3Q+TTIvO2RNZyUxMV5oTUA1UmY3Ii4qTU8yT1ZmLGcubzo3WEYnQERXLiU4c1xuJSRzOWlKYnMyLWImJG9sbVxwRiVMQkRaOyUjX2tFYWxrIT0t" +
        "RHI4ODwpRG04K0xfKldYODRcT3IiRUF1JCpeIlxgSmQhU1AuQSNyU3p6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6" +
        "enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6eiE2NidDSSFGQVBCZV89c04+amYvPklKP0U6ZikrTUUoISleTVxbbEslTklI" +
        "WHJxcGojYz9bIV43RnVuVF4kIyxTcCQ6NihGRV1rOWRcVFU7PGlwXThbRl1hIW1UbTs+aC9fVyIiVTUwcy45ISpFWF1oOFdHOS9cXGdjKyw8UEVWM2psIyRQ" +
        "JiEhJD1XMmBMTCZjVGNAKV1YYVhVSXQuSzloN0BJWFthITksL3JJYCZxPWooO21iUEteSixUJyhrXC9mOi5SWmBxOjdqYCtbQWUtNWdwc0d1TGMiRDZxPl5L" +
        "cUpEOiJtYjpmdSVcIUMwIy8rcjthMmBXbk9kc1dsUWFGaEEoaGdiK1k+U1l1IkJsai5DQnBXciJyOyNyXDhzTFMyIVdXNG4mR1lPa1k8a1REJTRuYDtXYD8r" +
        "Zmg3TTpmOClTYCNIZ05dVihkQGBRS0NUc1JrIT9vWEk9ZV5kLXJkNkovTGAtanEkb09ga2JBPD5QNlYqZyE4cXBhL1ImdSUmPjM0WzxpSnUub11YXCxbcis/" +
        "T2w7VyhVcVRZS2dOdV46dChEZiNUbkRIJzViLD81NktuVWZ1LF5CL0ZIams7SSk2LGYkXjNvYGhUNy1GLk8kOzo0SixkS0coVUxFUWAvSGoka2c1OCsxWzxK" +
        "aFVSSj1wKURpITdTRCFkUFQ3KDwtQ20jIyghIShxXVtWdCYwWEI3ND1SUGxFOmpJTE1PYWovMj9cJmthVlttbEF0bDBuKUVnYVlFS3I5VytDRFg9bVoxSFZj" +
        "VEtmZzFgJi5mS2daW1ZrIU5UaHBBb19SbVdYK0srJ24lV2RjZjlcLXQoOjJncFZlNDJCPE5CLkglZnVfMl5fXjplRWkiMFpvI2MpcUthSlApKyRNNl5ELVU9" +
        "TjpmKjZvZ0soRltuSiJLPWo/W0ZzYTJeRkFtJ0ZgbSpLdDsoSWYrSFA+Y2dvIzhtWWhxWiFJK2lmP1tXNWUjI2hgIi1ObGkhIShyOGAvN2EvODFtdUg+W2gw" +
        "OnI4cUA3b3AraCFpZiJhMmJqMCpoNlFVP0lYSzNzYUUiZF4icS5CZVBldWk6b1hZPV9QNWstNV81bF5sYnBuQy0yJV1NWWBxOEYuVzMqS2s6UW5jX2ppU2li" +
        "KURyM0lKW3RQPzxkKjpFcG8xZT1PclVlUTcqTmE7WnFYciFyLnBKcUpQcSxyLGFiJ1FuITpXXjgicT1IX051clouJkhiSk1kYUI8YSxGKkRLOlM3KEdZMCty" +
        "NWNKSCpRa0cxXDVAVWBnM2Y8NjguJko1XWsnR19IYlpZJUpVbUchW0I0KkpNciVKVDtIRistVylIJFEuNlpkMWZTJ2JxSCUpSHNkdW5gbloqNVAzO2k7bDM1" +
        "S0osWylTIm5KPVVEOyk/ZzNEbltbbFJiVVdgNV1mIzJ1aSYqcyhWOXJuZlJvNCEuPChPKWBMKU02Qm9aJ0BpcTxWazAwNmdYT0YrRixDOyIyM2pVX0tcOFwt" +
        "OEVSZVBFN0NSK2wnQnM+Uk5aRWBXJWkzUj06Sl5CM1szYlI9czgubCw1NSpXPVE5UktWbjZra01jMGEkdCIkOmYhQTolWlonMUkiaVc3OCJYNT8ubVNrREw3" +
        "XV5MRy5oamUpIyFeTCkjOy80QVIrRFZNPSI0LDo3ZU84L0twalQvLEYxcylIOV5dIWsxbGFDKjJHLiU8MEduOXJVISdXb1xwaSJydFovSlRbV04hanAoS1hY" +
        "IUVUMllyUUtJOXAmXlZMKTYlOFJDcixxMkoqU2gyPmJfJ25URU4xRihkdVlfP0VjWjowKEEtPENSJEVxOlRnQXFLVCs8Iy0+XiguZzchTVxePmU1JFZvZ19a" +
        "QC81Y0UxVjNiTU8wQylmNkosWyFhLVFkZC9PbzJZJUJrXUc/ZiFLNkFjTWtxTC1gb1RXJWZ1XzJedXBOXFUwSlghb0QhX1ctNzIyYkQ5ZVkzMk9ldGwqWWIy" +
        "a2pyKXRiVzpGPkRJIWZtUFlbbV1Yb0IkYz1rZD5cNFFuVnMjcCE2OXIuT0cqcDpfPWJOKWhzLSdobj1CZ3FCUElOSzsxcXQ8cVxwVC9TIVBMbz9LRz8rLT9k" +
        "LyEhKHF1KUUpRTE+VDwuY1tFRnRRO2w8QWo8Wj0vR1lnJGN0YUZiM1g8ZkUjdHJUJy88cD9eSVJuYCVOcFsnWTJTbVRtU1ZyUDNzZCRQaztXZ3BzYUAlNFVh" +
        "VmZxY2lFKkJTR0c/RzNwbWpaZGhLYmVvMkhNXzhSUUFnU2ZNSUpXMytXYFBnSktuWkEjVlEndUxFdW1HbnJkNnNySkZ1RXFHTC9gbyddSUhOZVNEQSVWWW1F" +
        "W2o/W2RHWD1rInNiKkZWMHMjJjZcXTZASjdWK0hqdFdjb0pIIzY9ZilpKjNMbVpCWGc3JjhQUjZTIihyJmgwN2EqZkRJNChbJzg2LV1LRFJLUW1kITprX0A/" +
        "Q0JJW2xTXD9TX3ExPWgnKWRGJFw6VGk4WGZfZGBRPW5NWGEpU3JvL19bOy9pVGJJTV1XZzhmJmJHcVtsc2QpbC02cjhTbSQ+O14zSTlQK3FNKW0sVilxdG0h" +
        "SEZcO0kuMD9KaSZCJD0ocDZpUFE9LCJSU1dbVShCaCNtbVU5aj9XYkchO01rVVVhNSdVXDVvXzg6KTZNdTowJixFXF4ra0RvQ0FQQWlvQidhR0pWI2xwMDNO" +
        "ciJuVVpqTSVfYyNNa051MGMmTmYrZzIjckRII1VybS9dcl9FbCkxLnEyXEU9Qmc6aDRHU3VTRS82UVNAMmsiTm1bLSo7U0Q/RypmQyc/UCpWVDpyW0dIMUUn" +
        "WCEhJURpTyRFV0I+YlxdRGchW3VvUkAwSkNjdCliQVI/dXUjVG4hK0Flc2JYPU5STVI/YihDbzhqUSxCOChLWig7LzJUMzVoQ1ZDaGJfKDFkR3U3RjllZFos" +
        "IUEkdCY9LFU9ZVlCQUwuQVdXak5qSzM7PltYXXI4OHA+SStucGtScT0jN19eWiRrMExaPFA8XiRScFcqPiVmdV8yXnBnNytoViMjODJzNHRWKlxpISxldWFP" +
        "QF1JKzQ+NlY8W3Q+cjVgYm8mMCojYkI4YEREUiVsQWc4XUFaXz5OMj9bJ0UpVicmJGl1SG1OLi1DPVFTW0RGYDpzM2NxPWtGTDVmNWIydG9oJ0k4Xjg3Pmhy" +
        "NCVHLmRqRG1MPSwmLSlcMV4/JmAlcU5sNVZyOm88NTQndHBTb245QUlZQTFROkNgX0JDRmZWb15gSlBCZj10OSxPNVg8LTBSbD5KSj9iQ1ZcZyNqSCtwcV5Q" +
        "dVlLX15PWT9yKSNkKDY/bVZvQGZVSXVDLy9qbEZPSWpuSWFVbiVBN1BubSVEMD03NUcuWl1hKFdxc00pSSUxPkFNYSwydGIhISEjVkBgSkA0ZUxVR04xLDkz" +
        "RkVESVZpT21UTCQ0clpAbzw2KCI+Q1hmOltbVlQrLl9mSC5sJU0lJ2dWVGFaJSlEbVE/Rio7YDhaTXFrJylmN00pZyxwYmwpYEdwT1hpQHQlNVEoIjxDVTpn" +
        "bF51J1Y8YyhWaDxEOENmLWFxR3FtWDFgPVVkLi0vOlhnYnNkUFVVXDxkTnM1bGFiJ1FuITpWUmVwP1lZWlBGX2FRP3JnPG9aQ285RzJPZywqI0V0QDhMQz4l" +
        "RW88TlRVTj5MJW9vOlllKTk5a2IxLXNYL3BRLUQ1RFMtVCltbXNjay4tVSdgTkFodCM2WF9tcS5tSGArT2JkTSEiJEtZMz1KcUFVQCJuUj86bygiaGJsdEc1" +
        "S2osWkZVQk05SE0hISVPZjNITz5BRWpdOGRHJjhUN1doKGtJXjNrM3FidFFCN1dZXDVkNyYtaktJSW86Q2NOIWpQLkRQWjskXykiRzM9Y0k2Z1U9TS1ybk1T" +
        "QCdsNSFaVTJnW09EVmFVQTAkTFlGbTk4Z3NAL1RHQmQrO1NSW3FYLVgzUyYlOTBbdE9yJCRkJ14hISdfWjFHZExmbWlVPm5ENDw6IUZRaClmQWknJkxURWxk" +
        "Yj5CNWBKJWkyTm9rdS1qMCtYJiMuV011bSUrc14+MmU+bFlINCNGTHMyckIiWFMuLERycmVoUkdRSl9WcVxUPVQ+USpQM1ZsNVZtLzlZM0g1Ji0pXDE1LFVd" +
        "ISMwSzFWczFLbk9WRzxcXlhDZ3A0aF43SSQ0KlUsQlcpNXE4OVo0NDhyVk9wMTJmSTxOPylMSmpeM1Q+NGg3SWtPL1NlM29vM2MwQkIpMiNaWFZHSGxSJWFH" +
        "V0xrai4halxERjNoOWwnR3E9YFFdPEtGXitlJWhYYUosXSYzWGtRVT4xZUcvPkduOXJVISdZJmJpNSpQblpOdFp1PDlyO0pZSGxrMFQ2dCtTUjdWWyNxPDop" +
        "Oj0wR3FzMDQzOV9xb3EoV2thJlYpWj9dN2A1Q1U4WSpAXWw7K2JGLHA4ckEycjAiNT8yJWQhOldLYiRCQl5zTz4uInE6IUImV08rISEhISJEZCMmUGdQUkgo" +
        "SipkR0o+V1UmMD5YNyInXUhkOEdYSWFiM2VJT1w4WS9oVG4lZmssM2hoUyRQLzV1XmdKYGt1NWRRN2xVSj5Mb01za0o9SztjUkwkWSwtbmNQN3EwNmE1OUUi" +
        "NFledGRkKEVYYCtaPUhQZ0Jtb1R0MlwiN1slZnVfMlRbNTIraXFEY29CPDlVcUgxUjhDMW5tS05BPkhXZG0rSl5WTUQmNUU6ZylSY0UpV1Y2ZV0vVHFIL2ps" +
        "Q0ZOW2klalpGckk2YWRhQjYtOFxRNCpLc2VOYT4lPUshRjtmb1s/VCNLblk0MEhoRWA/cjVEYmVuZlJvNCEvY0FAU3A8ZjlHNCIrdFt0LGFmbDA1NTptT19W" +
        "TGFCbW1SOXEjMVJyVW1ARFEpVXRiMTcrVzhpMEtMZlRrcHJNZ0dZSiptRyMsJS9vLCQpKllhMUVnODc7cyg2WHNTVj1VQT1xRls1OEQ2YW9gJU0kNCFva09L" +
        "cCczIk1iYyZHX0hVJkZsX1d1YGtpYXQnWTRZIXNIOCEhIzhdRTpEZE9JdCl0MG8oQzBaXk4/aT1uZzlTdFYrUiNTWD1TXlhQb29AP2llXSUpTWU/Wzg9MCNA" +
        "cj1oZT9gXTVtImpaQFRcSmpYTDAhYjpmbDxdMTtHTWdNYWlERCVkcDEvUWspQDNZKi4zWUVZQj89L1NlNkNScUcnVmkiJERJIVZeKG11IlxOQFVgZWtHZSE8" +
        "XE5ZakxmU0s2WDYxJnE6U241NGZCJThuZ1MsOWU9IjxnKUE8bGBcJyheOjFzVG1qVHUkYSxbUjkvNi9dTDdSaSxVckMpVltrbHMpLykza1hmbVM2ckIuJnBN" +
        "JmIuS2hyR09BXk9SdWlfblM9Q1NAaWZcVDZFLishY2UjIlNjTmNPVGI5OidobTlkTk9xRzNpPWIkNUpmJ0VsYT1jRExWMFxHbjlyVSEnXVxqUHExIWldRWpj" +
        "UDxCPkh1VzZEXU1cJUUjMUhoUHRHQUNbXFtiNGFROElmL2xqP1VgYjMkI1ZWOWYhST5LZ1U9NS0qMS9VIVE8c1k2YWIvP1dEY2NYQGUjI2omYD9rLEtpVkcj" +
        "US40TkRAOTgmJVM+NXMqJVcpOi1xMDNQU0tmbzs+KUhIQjhKJDUsLmo4c0xtS2lXQkFuMEg+RUssN2FoVSE1TmVxZ1U5aCFPMDpzLiZsWVokXFRkTTZdaE5E" +
        "I1xnO3JZbEVkP0ZDPUtLXj1BYy1rb1E5RGdeK0lIOWtLXVZWPlE0OUQ6VEM4KjdXOTspQ21fTlFRbmApa0YpcV5JYktlRCk0Rih0YFJlRm0tUiRTJnM6NG4u" +
        "TVhOYDRyWS5TR3FVODRUXGJmaWNKaWZcVDZFO2JCQyRsTk1xZ2dvaCJTITQwKFNORWJHaWJELlxONWhZTiVzXF1kISEjMyJsLWNFTmYqWVZaWF1rRFpRWDpN" +
        "Qm5hdShwKSZYOzkiLmhKbEBgKnAwWipDUUZiRWMtY1kjKGdEcG8yS083WkpJZmdlXC0+czgwUzInQnI8QmNjWEJDTS9xRmBGVFZnVllfTERSIXNePTFnQy50" +
        "SHBbMiZTVmJOWD0ucCZtZEU4Y0RJLjRRRSJFYioxSyk5USMrb110KylcI2I6L0d1L2JWYVhAJlpMcCFiZFAwQVE1bFBFTj5LUzwwbDNscWVhOFU0Ly5tNzpg" +
        "dUdPT3NfU3JhMFghIShzQzxqLGkzciNEQHNSUGVrLTE4RW9qO0dwQ2w/aGklbEp0XkVja2pzOnJnMm9MV3B1QUE1LT9xIU4qXj4qWUdNJkouSz8hMzJlPmNN" +
        "IWVWNThmYkE5XWoiOThFJV1caFxjTEM6OVxbMDZeO14mN0hFMUBLXW9nOjs6Jm5sKT1XTHQkVkJtVG1wc1IkIU5ZWSRGLTInVGVXSC08Vzdhaj8raldnOWpU" +
        "TD4vbC9hYWIuVXVnU0JeTCwxTEJqODYwNGZuZlJvNCE4c0RnRil0RWE9T0YzUTFSW0AzODBZaEpxUSRrQW1HNUNgQyImc1EuWWlcMV1fVV1aUTNXJmAjPCo1" +
        "OTd1ZVJncUNcUkFNJVYqXTlxJTw7U2FCNlZaaCdoNEslOS86WCtxMCpnOVA7NU1BN1khSU9iNldBMCpSKGhrbGwlRF5yTFYuZlpyJG5mUm80IThxJm8nLDA4" +
        "UXM4Pi0wcUQ8JEBZWTpIP0pVckJnXzgzLFZdU0xbJnJIOEthLCgkSXFaZl8pSFQ5IyJtL21HY2NmTDgxcjctRGpkYEIrJVlgSl1Kb1NpViIvX0xyNl9nOWhV" +
        "MTgoODcrVmU0MkJKLCRvJ0IkSGNVSCdeUk4mWk8/LWwycShoUCoxdGpoJzJuIiEhJG1fNkQ+cUIvbTVLdWUiKShQRlIuTiZISlgjQ21kOTksem46N0s6Vl5g" +
        "cCI1WDwsQidlO0UmLkA7Vj8waTw1RWo1VCZcaCcybiIhISZWXF8kOyYxZG06NkpgRScqRWpsb09WNyo+Nyk2cTBZV3MuL1RZISEhIzNhN1NXRlEnIWFTMFE+" +
        "OioyNGo0TTlYLyREIzNFQ0VuJUpIWGgnMm4iISEiJnVGNjYvLTlGKHQ0Pi8sTyJWYFAnS2FpcXNrRm1ASkdEbmAydUs0MCJMcSFIaEY0bCVzcSEhISJMbGc8" +
        "SDljZDA+WVpZK1dySylibE8hISNyMmpNQi00NDZQYmdIRmVfL3o7cVQ5RypCSjo1OWhrOyZtZjM9ZiEhJFYuay8iakteZTBrQXAkVVhfcT08VzA7XFQzM3p6" +
        "enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6el5eSzhiaksp" +
        "TGpCI3AmV04+amc6TGtsPEYlLnNGaEIkSGNpXHAjUmBtYllZU2VRNis7KSlKLiJhLFtRQmVMRmUzQ3BASGdfU1gwaTxpdCllRm1FXDRJSjJLQGhxYmhhVmVH" +
        "NVJOYypeZHE7bXJNXkFSUUZoN0BfQjN0Zi46NCI9Qz1VdENmc3EwaS82SUpgRUAyZklRQ1JAMzwmais8SzVNLS5IO3Ajc2V1Z1U6cyddbUI/TFYrTG1wImM2" +
        "U19wJDEoY00yO2hOaW9uZGJObjFqYmwtY1MzWCdVLz9bR0pURGY8JjhkPmUrbjdmM09lIkFdL3VbZ3EhJGRFVD1lL2pyPGpmZCM+akMhISVfPD0/dV5zJDQk" +
        "bktsXSchLGE+Um5oTiNYaXVpUEw5dC40TGtWcCZBdDFyciNcazs+KiVIKmVcZiw1U0dbO25GMURhLycrSDBuYCNUbG5xYVFva05BSWVJZXI8LEs3ZWVmRnNP" +
        "NkFmXCJoXl02NSZPVTFyKVJXTidnalxDIVBeUyVocyhYQjZXUTVuJnV1Z084dG0pPGNmMEY2Q2hLb2lyYXFMRlFoR0FcSXAlUmouXm5TdWJcWDNwa2I4PTFf" +
        "WFJEWFFyS10zZydkcCN0VVFSMGIhYj0mNUdQPnIhL09cOGFtNVZSQlg2QWlWXDMhISdnOGtpVF41YDRFPSpzOE0zLFNiJHNcbzhha087UjBoKU5OU0VMQD9V" +
        "KW9zJ0A3aCU0bitKb11bVThRZlI1Z0pISCxxZlNlK2lIaFcySCJHW0plQERkaSIsIzpEdVVrOGRAP1QqRDdlPlpAbzQkLVpQSnVBMXRxPUVjQlBxJldxXE8x" +
        "V3IuNE01blVJT2dQV0U/RTcsITlUUydKaUl0UlQ1aDhELC1dZVhrUihOKzkyQnFPOVtBVVdwMC0kNVBLIVdnVTFncGgrQC9bYmNsNlVmJkZaLCluVShNb0FI" +
        "Y1ddLi1nXEE4RW43Jko1V0cjUEksPDU4TD0zJ0dQWl5sO14/cGE1MWEicT1FY0I8cScnMEkuWWZWJFssYDwldU4+TFVJR1twQkJucjhzMT50NWBxI2QhWF1k" +
        "Um86OjMxTVAhJDZxaz11ODooXVhQXCk4Z2FIbixDKDxZQCMnJDtgdHBpNnRMIyIySmo/SyhGUmU6SSFsM3IzZHBqSEJQPUAwPiVsTmpoPGBoJDdBUk04altz" +
        "LHNBcFNlImZzPj5rP1tNTWZDZGlYTCYiS1gkIWw5SWQ8LTJxUiJEaz86QkVVczo4akw/SkBaL0FHLyFmczEhPlM8Q1lUbVgoZWpLcXQmT2ZZMTcwZ0ghZkti" +
        "W3E3Ui5NKCFsKnBdU1hhdVw7c3A1dWNhZDotWUxhaitoWVpaR1RBP2kjQkFbP0dULW1tYyVpSS01biVKSCpCMWYlNGF1b0lPQHVrMWtjSUdzIlhbZUVuZjNF" +
        "Q0E5LyFzbTAoZyM2P01yVydocCVaT285RUJgXGMpNlYlbVUhXWV1YC9zbT5LLD4tXz5RbGNJOUZaXTBILGpXaVd1N3FGZCwqOF5rUkxhYitmb0giWUYuZSoi" +
        "UkdwWVMzP2x1cS0jR0tWU0k5VWRxV2RAcyZvclVhI0o0TjxGKEooZVY3LG4/WEZwJDVERjQ/NyZITSovO24zcFJjMmpIMkNXRk9TYT07ZU9GSnJTcnInL21Q" +
        "cFVOQW9dIjgmWmdFQ20lb3RRPFM1RSlBRlUwayc4WklTRTdxXChWUiFRSjMib3BqbkFHYHMjbmI7ZnM1MlxrS2VZTmVROVpNbWFiVFxTW11MQihTJ0ghQT5O" +
        "N1NlUSNbXmIlVEEtPVVCOixURUdLQEdbUG4rS0YlO0hqLFkhX0FCcF0qPSdnL2BMQ1A/aVY5PUtpVkcoLSZOQV1fJzFoYjdvUXQpL0UyLz83TyJpSyIzTVYy" +
        "ZEtdZVpLdWIpSGBfWFtTLWBSRlQ6JjlRJTJdMkNVYDAuLkFoO2pkMD5HVk1FbU9LQ2U2TWJJZTFuZDcsKF9EOy10LzREQiwvMyxyRDpAZzxNYyonVExUTkJk" +
        "OidVQ2MjOWBkQCdXOCdbRSNHcV4zPF1tQj5pNCpHRmtCNVlWamM4aydYNmsvK2o5Mmc0YSthQj1CSkg+Z288amQzLHBASFRwP1phW2FNPGctbzAiSWRkTVom" +
        "ZWJOXTg0XTZkTT5lPSVXciJZUEkrLyYpcjVWSDFVMWVeXS9jKkFdKlgrYUg3XUM1UTE1a2MwIT9FKyw+dSleU0ozNlQ3NEFNM2hMWlZNJUZGTWtKPWR1XGd0" +
        "K3VMKCwqWUVROSk+IUU1bWsvU2FbMF5GRzNeMnBWP3UybyMvX2ZZRDZLVztTajJWYmByL2xHREJjKSkxWnMuSilKSiE8Xz5NTyN1ZD5fWDIhW1IqaF4kUD4o" +
        "azhwZC09UEREbDRXVWEocF9FblttNy41dGVxXmskSkNEOyk/R24sOWxuPkpZOVdJL2BzKUFhQ0I6Vy1xK3JfKCxFW29uc1Y4aVYjUT9gLEdMQkhLcUlbViQ/" +
        "cU5LRyxVcHBrSz4iPjR0c0wqImZAdSJLNidqVFpoLHRVLiRIJUh1ZEJMaDdAYVBYPi01SmJFYV9xZjwvRSRrPyRaSGFzajZZJyl1a1UvQ2pHamJCXCdOUW5i" +
        "Ik8qSHAoRGk/b3I0XzQ+YycuQmBMLE8kQmRqT21pQ2pNYGdeY0BsPCkqP1VjdTZNW3MtQ3E7biZoPkBrc0EuPDNbYHBxbjJyZU1AMzBaQlVGVUI/Y2xGbigx" +
        "VV9aXlAxM0cwTGBqUWJXSHBwJ11mLmQlUTQ5KUJcZElJZW1jKHFnXE0xW2k3LzsmP2hoLEJjOVNXUCJZY01vZVVCcm4lU1w5JidtPytdWmEvVFs7Jl5rWyJh" +
        "RlAuZl1NQl5HQ3FUIyMxT0NSNj5uP2k+dV9pJidnZTNKLFQmbFk/Sz1XQ1RbOUsoTE82bC4xXj0nZDcwTm1nOikhbUFzJUgvXWY0Zmw+QXEzXzdVLEZqSypE" +
        "ZEJdLGBiRDwqP2U8T25GbE9hTzAsNykkIWNBOSN1VzhaR3Jfam1CdFBEWHRGOCxPWCFCaWxJRHFGYSxbOStbcjEhMm5bZSVoXUBCcWRwJExOLk5dWllXQzIx" +
        "MzVHMiJUSTVcWU1KTjVdPTNcb2QxaCVtRzcxZDc4LGdwIXRddVsmQ1JXNU4lJW88YFZCLFFTM2pMT1trRW9vI1QnNmdUNSw8VlE2bzdIRlJNUWV1ZFtBPSdT" +
        "VWQ6YnViMFMzLTJQMU5fQko5OisiNmtWZFA+WSRKWnNzOE1jVVNwVG5zY1RlJmpyTWM7OlxkLUFlTyQzM1gqLUdcImJNTjdyN2lgPjo9W2MlWzs2b3M/bjhQ" +
        "SVhwWUMmUExYVytwTV50YjZNKCokIV84TGJ1QzJbQS5BZmNtUUdlIS5MZ1QjXik2NjsqSys2aDpTO2pwLnBOPmlbUEIiPE5KJzJMXyZHPDonPWkzSUhvR2Zf" +
        "ZTRTKjs6MyE8RERkZGRsPFtxczR0TCdzOztPPyJFKkFSR2g9Ll4oJDNBYnIrK2NvWzZDSXJWInVuU10malk8TkFvZjRyI1pIOHIsZyU3NUFsT1dNYz9BR2lP" +
        "ZGlxcVFdJHJEdVRTWUo6J19hRDpUdD89U3NQNkkzV2JBLyZlSGhuT1knRWI4JmksUnRiT1hQLU10YklAZCxDPjNVN2MuKDREPWdSW0JeMG5kL2AlV3NjXjo3" +
        "NnImRDYiY0YyQTUyQTpDckhOXVVqJSpfdVAnSTpoWlluQT5oLzFJI1UnVzZwQUJJVFc3dT4wRFA8LDI3MWI0WW89K2lVYGU/aCZlMmtvOmRhYUgwJGFKcD5V" +
        "dWAva2BbMW5RNCR0bGBAQzYkTlI4NVY8LFBdaDdMXiM0byswJkwiaFkwNFpgT2hENE5iNGM4UzNZPW41dDJNUSljMF5NXlI/TDQkT0RoZ1A3JHJIXU42cHUr" +
        "V2BhUV8+ZU8laypXbitQTFM/WiE0JDtVdWdtSixUJERIN1NHKlcwLnUwamc8SzM5PVxiamBRcFtPRjZDaCM5PUlwaFxgXVFvb0JqJEVeNVNXR1BJTyRyVS4i" +
        "LUYycjhkaGszITBsMW9QUz5kdTlmQm5ELzxHTj88bEFnVUQqUCpibSY1L21jMU1vOG1URTgjQihTVCh1QG1IMToxLGNdMmNucEFNZ14lMydjcHAiLSRZOUko" +
        "MytEbj4yMVo/M19VbV5wTnFeQFRXM0FhJ3MxPUxoJklLM04vczdELi1OWUorMjVuXipzc2xQNCxkJTV1OlFWLVdbVD1ONGdNLGtCUj5aIiIwc2pRYi1mN2tO" +
        "PSswKDc2b1t1YiwuSSNdJ1w/P2ZuYDImRj5kQWAhU2kuWzEwKkM2I25IMkpjSnVdJDhxKDI6bCkzYG00L15cTGIpLDlmQ1pNcmArYiVuXEkibGFIMlg3azAv" +
        "SC5JX0M8azFqVCZtZkNTWjM0KmBNJCZiTkJQL3JQM1o2NjZQZDJhNyE1cFs7SiFJOkBUTyFtYFk6LmwjSzpaZDFnPm8wKmc0bGdELi86LFZKUiEycDh0YUZp" +
        "RywtPTNLRlVJTy1qcypfVyQlbTk+SF1qTCpcXGF0VWgsR241LHFPZGZja0dPciQkTkhZWkgxRCtUPmdUY2ZiSUNeTG1iUDNmPTRKdThRRkU1MkVXNFsnUVgv" +
        "MEpHTVQ5S0RtW25dPSddcktcRGYrMFNVW0wlZmtuNFNnOikhLXAkWmlvKW9CZCZEbldQZF8xRWJYcHUuRTQ0ckg0bTwrNXBwazA5QXNIXFdkclEpSyEkZVE1" +
        "ISdcRG5XWG5BMitCZF9pbVRCP20lMVNwTigwOTctJDE4KEIyYkkhTXEvSF88dWJvJi9vRWhrJkoxQUs4QCZYMCZnZjQrSDRDKFd0OUw1NGRzYE83MTshSEto" +
        "OF8jNFg1UWwiOydgUU5xMEtTL0RSVXJWInUhP09zUSsjSyx1U2dvcChzWiIpY25cLlxAP087TFEzbkRNLllwL2tmJ18uTHJcJ24xWmtdbDguLS07cHI+YlA4" +
        "RztRUyxDY1wqJ0I1SV85QyhnaT8nNHFaSG9hSmVJV15AYFhILyhOZ2xdYGc/M0k+LW8sMEg/P21AP1toZl5pYUtMRGZGQm5WNC5dLSJNTV5xK2BecVBLPCZI" +
        "J28qTjxdMF5rIkw5SVRXbWRhZSFrSSk3TTBwP3RZOlpNKzo/Z29dUWw+bmRAIShDWSNUVmwlQm1SQ087XkhMJThVVzphI3FcNFpAOTZwbCUjUyNhYjlDbS5u" +
        "W0RJdUlhUyUwKkwoVjk+OzgrKlY9VnAkOjVVSWAxYz03aW1MRzJUNU1NaDwpbjVfPElraVN1MEN0a0o3XnUxKFFCdTw7PW03MC1xTyRRM1kyKXJxalhHZV8+" +
        "I1dyTVdTZ2hMIl8pYEpZT2Y+U1toMEI6YUJcXmpsR1tSbDhOdSNvV1FkN2lzPjdWQnRBXzVPbFoqQ1luNyM1UUJOOTlxK2hLVSVpUT9xNTM8MSFXW0AvWjFI" +
        "P2peKlRwYG5FJUpHUmwicDMvR2wubCteQGIvWWVwcz03Q0xvYldyaSM/TVRwYmcuT0k2LCpJSS5YZUo7KVFvaXViI0thIyZZNCpQUV5iLy5JOmQoIWA3U29O" +
        "KTMqZFZLbyJyT0gqaURIOjIoZ00rJy0xdTwzRzIkbFs1NVhcJEEmYG9pZmsyVVFxb0w0WGA0XiQjbl0nWj9cOkV0VGdGZmhQNG5kTVFdMFY2LFhdcjlbWm9S" +
        "OTlBKCNJJys6ajJsWy0hZm1BcSUvMjAiXjMyVSFlNVVKZFgxWkh1LCU1Iz9VaGRsZCVaXV1aLHBNazwpO0BLaWdCSWIqNnBgPSVNdUI8NkI8c3FXYixaSCZd" +
        "KDRNNkAoXHJlVV5XPFhJPT1fLkNmVS9SNWdUW0ZcXygwaypRMUgrImkjZGNdcmxvP1RYXjA4Y3BFbV5iN0pdXmB1XG5OL19WNVE/LEZROjlvT2FxXixdP1oi" +
        "Tig4YjoyUFhqdG4yRjZBWUUkWzNRKUQ6R3EsZHNUSk06N0Ytbj9NZ1VoREhgNnQ0dEQmKzwlXF05clJtcjlPKlV0YF9yLjI/JV9ETiVYJF90P3EyRkVuMD44" +
        "OnQmQ1AuIyRJN2haMnJCU0dRdURRLTFYN3MpNSEwVDQ+I11AYiJuSCY/blxsPGJeTz5uOUxVMj8rQkhAUzNiby9IUzw3USdlcD9jcD07OT0qZmIsOmtccVVg" +
        "TF0yMy0sPFxwKlVsaGdQNiFIWztQKlUxY1k3Q2lJS0JRXiI/MEdcbCNZImpdNzFAJFFxY0Vbc2JsaVtsaVdwIypaPTFHLjNfRG5ZZjNZZic8ayg3NytfVyci" +
        "MW1ATThKZWpqPEcnPG1PIzBuVy5HXkkuR0IsaiRoZ1NzOEFKNGM5OnBXXDYwOT1VOT0uSTssUTtWbikobTBUJG5sdF9bU081ZEBmMko7OStfPisibkFUSThE" +
        "KldVUk91SDFzLEpUN2BhTS1AM01VTWdOMj05KGZuI2gzcFU9KmdUYjgvbSZxTl1RTnRFaigvR2koRzNta09jImBDSF5dNDwxQ1RrRTBDZmZRdCxEPkI4JW1L" +
        "Y1I9J2Q+ckIpUjUiKkh0Y0Ryc2gtVzptVW5Ya0tUS19NOWoiTVJyKTN0NG9wMkZVVTMvOVtWYWNdLD1gTmBuSWBQJ0ZtPnMoVkN1WjFUTCJ0TEYmKChZVjVR" +
        "TFlcUkgzSWxlUWRCOiIqdEhlQzEnVks3c1tMPG5sZyhmc0JeNVFvSyhwYz1YaWdoPSErOWc6KSEtX3R0QDs1ITNTcktGJjw4aEtlOiE6UjNLaGlyQSJyMC5x" +
        "M2xycVBON2E2aXAlYSQzVjNwYEciYEVVQzBjY0UtR2ksRT0yIVw5JGlZUj9rSlFcbmVeRShYQFhgWkpLVEVJVylUNkgxOkNKMEArYEU/cUFWLG9BRTdwXiFy" +
        "YENMUWs6dFByLFpgPTZtMDgvclRVQ2VaLi9ZKSlIJFtCOWc3MW07SSFqPSdUYDY9aDRiOmxBLSlEQCRgS09XZFI1YT5rYzU/LTU3TnQsOV9OYURwJ2BBYmgi" +
        "Q28qaXMkMEVXMF8wXVFvcXByK2BAKiFkY1J0Ol0xJU4qODlFLERuaDsjJmJQREAwN0QpKmYzJTpUVVhtKkpgWiZRXUZdTFYqQS0nPTJeQUN1PUk/aC4yVms4" +
        "RSFEQWkiYzdFYlxeXl0idE9ZNCouIl9oSlZZKXM9aGNTNUYsRXI4RURRMlY7RGk5Uzg8YGhEalRGXDNZaT5pLVhxK1FQaVQsQV1wQlxrMDFzIVpFdV9SRFZW" +
        "Yj85VWFeUFpkIklnaFk+QVhtc09gTzJyViE1MmZFIzlQOEkpbWxKaktOOG9xQzBOWjhyMmV0Ym0kSSxGNFJpNCRLIkQ1UUtOJTMqVGE0aEZyXiFzIUkwQyVo" +
        "ajpbOyVSLV9MciFITCtDOVJJPWhfNSlGYllDPThiXDgndCVyODNvO0Y4a0o8I2snQmNRYG1zTlloNFpRIjxhWE5lYmk7V1dCY1lhOkhCXUUrTUkuUFQzSGBt" +
        "PDFZZC9wVHJAcWclPl41NF1xc00oN0ktV0AtKjRJcnMnJVpYTktEWSxQSXFEdSdnVThcXVwrYmU2XjUwTjUzOkZOTyQ/b2ZuKlhsciJwIiRXSUBVUnFfak1C" +
        "cyRRY0ZTNCQ2Ui8zak07XWlCI0kndUtIbiVDbjZFOzNjLlptMFNgNEtLaUlobCVoUD1ONklKO1FjTSk0R3ExPm0hZTdrOixtSCNkLyNWL0tRPyE0QiQiR1Zc" +
        "NGJDQFU8JkRuYyYwRms5b2NcLSgwZEMiIStjNjcvaVo4ZXVHPl4vXnVUSywjc2JjPGkkWHE1VWlLJVFOQnMyIl8iQTlvXSY8XS5eZTRgLywvZjxTLV9sWlBG" +
        "JTNQcS9AYURuWWhwVEwiM041ISMnUFBgTGtpcl0iZG9kSUJZPGNebDAnRCkkImZeaE9ZbDVwNFhqOXErJXBvNCs0TyhqXyM8VW9fJl5ITF5KbVEsblU6Tycm" +
        "OXNuQ09IKWo2ZTVTXyNrMzEwV29dPkxfNT9xSitMb0RNJVI9KkkuPjAiW2NcSDpgZixiTEgzZjZjXlgiPzNpXVl0TSpkWGNEU0hBazc2MkxDKFE3Y0hoZzFk" +
        "SihOU0NqVlFaKWBrYHVdVC8/OSpcPmVwUi5kY1t1cmFWTStrSWZCUU0kK3FLXFM0I1VGPFVlLWJKcDBAS0FUTT5mK2MxJ10wJ0AuNXVDdFVUQF4hPyE/I0Fh" +
        "PDY9WyNubEVDRi1ZNDdCZ1xtMzdETTk+YCxaVFEtRF9SLC1xZStbXCQiLlhONSwzSEpmcGsxc2RRWFl0JU1bKTIzZ2MlQDFiYSo0ZTRmTzlPbDxCXjZHWmdo" +
        "WiRtRnNTTWByJnA1J11NNE9UbiZYSEk/bl4+aWwxRi5jZG80JlFYdFtjITIqKDlHIT5jPyVtTjBvSitWLFlrQTMlZ2JhPF88Om1PNEdkOixfaD9aaUtCJ3Vm" +
        "QFAoTEQ+MEFeQGQhI083dCRoNEFsUDI8Ul0ocGRNO21vJlJuLVdiOnFKMm07MjNuKGtVIkpjOFUxXycjPUcxO3I3dHBJZilESy4hMS42RGg0S0spY15XI0h0" +
        "b0FxUkdJMlBCVVFsQFopaHQ0Y25lMGo9OGpPLmMsLV4lNFJXPGwqZj5hTXUsYTltJ0ZtaTU2K0xyJ1VvPUQvUiRaPElKXDhPUzYpaExuJUdjbUM6al02bzNi" +
        "IkYuM1gya1s8Ym0ncVheR2hFLVRraUxFMVA/XVpJKEFFXzpNRUB1YFByRERRK11ZWSVsWHBhQWVhXDhnUFYiNjZJMi00X0ZtZDVnQltGL2pbcXAscSE6RHI3" +
        "IiVGZDgnSFpNUkdBb2U6Z2pvVFknSzVySkdxSytIQl0yLj1AVC07QnJJJFdkLyZdKixiNlxuZXM5UF8xPWMhMztEcG8zaCg2SixWNjFBRz1vKGNZaUBKZ01i" +
        "LExtXy9BT1o8KlZdJXAkJyknKFQrMFgoT2tZNEJVSjFBODopbVcsIUgja2IzNmJGKyxyaWhnS18oLUtSJT1gQ19EQk9wcktzNFBxWDprSjpRcF4lXWdaPlty" +
        "YU5XWztJY2ptbGdXSVYiVVoxPDQ1MnBPRTJaMDo6biFuPiZYPlJQZWpyMC9bRmlUXTEsYiRBaCdARzFnR25aRUY8MFchYzU9Sz9LVDdoZ1lJaWovJj5ubERb" +
        "MUBFJmVVQ0gkQUQ9QjlwNyVvUWRbOGBLWkohbDw2PiQ7V1BZPzk5cURQZ2MzY1tMQFwtaF9WbkFEXV9VXVplUXVONz1LaW5OVTpBbGxZSiF1bjEobk1ZZU82" +
        "PDFTYXJuWlcnJ2p0LyRIbU46SFwwYydydF5gbStKXlhnaCtzLjc0QVZWa2I9IXAiZm9uIztsMzYmaFJuLUg4QD9AS1FLSHREY10tTmg1KjhJPSJUVlpoOSN1" +
        "WSRVUTdTVzBAZElHZkJiRDhCJiU0L2Mscz06RVYlUlpZKmRaLj0nX2wpZyxwNGpfcks/cjU1SWtuKmQsYFdXRGhoVUImQSUyYy5PWklwanRhL2t0Z0NtLE9u" +
        "Nio7KTpFQFFYNHVQXVFpKS1TQlpyNkswZXRfXWViV2UyLzRzcnFYZGM/JiwsPkAwNWQhUUgtdFo1NTxmJHRAPkQ9LmZlUkdQbUBWLEAvNyNpL14lS3QiWEpL" +
        "KCtCJ0JzXSEzZzJlL0lyJz9ENzxmYGklclxBUEB1WGhpNHVrPScyTF9maFRDYyFLOVtzYVsvPHFVbDpJMEVjVENVLlxFNU1VYG1nZzA4Zm8qc2JydGdWJzYh" +
        "PW9gdUw7TGciVUdbKlQmbFo9NzFpWGowTChba2gza0QoQT9eQiszKF5aRzFwWGpSaF9DdFZiTldyTzAqYEwrYnMkMTU1T28vKkRiZ0wkU0k1QzotWT1ENExT" +
        "JmFxWGs0WUJfcGtcRG0kTSE+NEdjazNwRUhJZ0phcDtPaThpTmlVWyRMNVtARTIqX11mbUE1QFhRaEwiXzE9MTRZXEZYbGtUcSpqRF1hcVtpWXJpMGMsVXJl" +
        "ODxWYmBAaGhLNzRAaXNGYic8VSVEclkxW2EuclNtRkZeZlc9SnBcUy4lQ0Eka21ZRWxFNDc+aHJbcTlPUE5KK0NEXCsyZz46bC1sUGBES2psVWExL1EiSWVZ" +
        "KHU5c0p0X2BOUWEzYDFnMSozQDQqZW1jVkYpLUFUOiFBaEUmYE8sb1htZFxUVU5RNkEjZV1DKjdNaigiLTVbZFh1ZzIzRSwoWkEudG8hISUuRWFhI1JGTUtK" +
        "OkEzRjtFaWtGUD5SM1RROGhpV2IvWkhLYGVSOT9VPVw7MGZCZGBLWlwnbDw2PmJubUhGYDVtSl1OWS91JyUnZzRcSVxEclVabzZiYmZNRFxlV0o4Z09FKyJh" +
        "OzJtQzF0Wm4oMGMia2JFTmJrS2ZkWFtWYWQvRUprU2EtOzllJ2UvSDVNaDRbMmtbRlxfSHJvKlw5VC0mOF1nKV0jXiw9XVZCaklBMylLS001MGV1XFdoTipm" +
        "VmZbanBJVCx0ak9MIzVsLjVxV2VNQlo+ZWQ+L1tzTGo/Nm5FdEowMDc4QEBLL2xpZywjaXA5ITlVZ0JHZmgxRkpkU0llMWZDWUwmLixKQF9FNzRMY1NOM3BG" +
        "P09nKzFRWXFYaiQvVGIiX1htMkUtRVdwUClnXVFxRE0la21pJEhhLmsjNkgsMVojTz4+UC5KKUswbiVQOiRraj9jbWEzOXItJGhQRkFvP1REVk4xQUAtPTds" +
        "IVw6YGNdMWAkKGVFSEtxS1U1UT8sPVpjLkBtclYocEslOCZfYVJkXUBTTiUlXSYsYzZrODRuUydsbiRDPigvJWpnMmppYTlCbERhNC1dMnBSIWtKN11PaWdB" +
        "RSlBXXBVUXA4U2pOSWYrJEY1NVhdMWg1NWlmKGtncUY/NjQ1UiVnSFs8SCJMPCZMZj5OWmtBMSJbVms6dFE+M2wuWHBobkMnMms0NG9EUmEtMVo8KzU5QWMx" +
        "WzlnWClXXWo0LkZcamN1KjgoQkNcZmVLYlpbTXBJRCg0MyExazU/PSIzbzZAZE5tRzVCdSNSSm5WOiZGZVFZSlhyOmlsLWk9YmFCK3NxKnBEZElKMj8sR05t" +
        "UFBDQF5lM0pRLSVFYFRGdUQnaVQsb1hBZWVmYl8qYTE7NTRhXUxMcEFXbWlTdUxgZihlbUlmIXRRKTA9dWQlOV9yXFgwOnNCWF1uZD5iJzElYkVlZC5SbzNi" +
        "JVcySm5JOGxEU1tUKy0tN3A1NUFhKkpkJWsrNz5ocTByOi5nZzRSdSNLMy9RQk5fLnAkUVE9ZEBgN2ohLjkmSUdLcjNGcDZuS2pUPClqZjYhN25uKEUuO2w8" +
        "QyhvP0tGWTU1T0tdTlEhamY1NXQ/O25aUnM7cnFNWSM2QjwwTmltMCxDcl47Q2VvJipTVkk/alNWK0NnXFhLM3MqJ0FfPnN1Yi88MWFvP0I0TEsvam5kNVEx" +
        "NFE4Q1FWPU49LzZkaWQ2cXVpNHNuKF0sYFk7OEJ1WEhoVk5WSj8uPzwkW2I1OFxBT0VJLEI0Ik1vIXMjQmZSY006LUE7PkYibWJQS2ZcJTI0ZENaPz9AbCc0" +
        "Q0I8aGJ0TWpNQUUrOlxtMmAsM3VNTzFeWj8sPS8hTyxHNEslRzY1WStGaUoxQDdsYWMyTU1vVSZuNihBMldvXVslaC5YTS9vb0tyODFdKmEqOGRAZGovXDA+" +
        "X2RkWXEwTSYkR1w9QiMqYUlUPmw/V20/ZDotP1MxSFdNJV84VmJQKDtVMV09VFpeb0Y+ZkgjVkhCQVZqSnI/QDs6Xmx0NTp0TkQjNGhRVVc6bEArZ0k7QDMy" +
        "S3BvYjxZcks5N0JLLUpuNUFYUk5pJFByTG5rP2xuW11UdTZacWosNypzZ3BxSjo0blIrTDxuRyQzcEYwJTw8OyNrNT0ncFkwUmVddDxbVmAnbmRePVRvTVxh" +
        "OkBbbUY9bCR1QWdeKlhrMVo3U1RrZldvVXUyXkMxXjxJOGRZW0U2XihCXUpzOFJbQEBKXzRuZE5RZnMsJi1WSSgpPT07QkA/JWdJNzhJPFhJNl4zP0A3Y18j" +
        "Xjc3Mk5ARmQzP2c9OVk1SzhjOGsnWFwpMllIXmo4WiFLQjRcXmsnPTBgZTVMMCkkcE9PT1VWZFInWFFuM1ghXUpcPUs0MlpQL1FvQlRmKE1AbyRLXSojazw4" +
        "UnBGMlNZXGpHWVxkS0hxRDtsOlcwYDI4bShZJW1Ecl9GOVQkZGVaKy0hclwocVI2Sk9xOyY9QmZMM2llIzQrZCNZOUBuRHJVI0Q7IVFMTShQVC5bMTA+cjAi" +
        "Y2k0bls8OmMnaFowQDxNIm5xMS9ibC4sOCREay8hLyJSbDUsUD1gRChJZXVTIm1NPyhIbFRaKWcqNU91bD8raG0/Vm8/TmhBWjpWUypEcU06JmJFYV9JQz9Y" +
        "QlpsZ2AiO0osVCZsXlxkSDJzK0E7P18wWE9zM0hPPVcycjJ1YT0wSmE4aFJbanFlcjpTKkM+UVchTClZJF9VSVVDOmNUY3AtVWUqdWFNOCpmbUpJYXM+QC5g" +
        "WzErXjV0M2kuNG10KzBNRy1fNGc0Vz0/Q1pqJ186aEFUZ0wyJ1NtN1MsIT5bOm1HbFJhOiVpQCFuPklTS1kldTdZLGVlUWNnW1ZhZCdwPjNLMD9adCs2ajJS" +
        "KVVuXSlfZV4rTy5RSHNWJFFGaz1fX1Q9ay1yZ1VEK1k1KCRccWxyXFclWzVab2BlLEIkdUorMkM0aVA4UksyJCphWHp6enp6enp6enp6enp6enp6enp6enp6" +
        "enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6" +
        "enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6" +
        "enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6eno6YT51TkxDRUx1fj5lbmRzdHJlYW0KZW5kb2JqCjQgMCBvYmoK" +
        "PDwKL0NvbnRlbnRzIDggMCBSIC9NZWRpYUJveCBbIDAgMCA2MDAgNDAwIF0gL1BhcmVudCA3IDAgUiAvUmVzb3VyY2VzIDw8Ci9Gb250IDEgMCBSIC9Qcm9j" +
        "U2V0IFsgL1BERiAvVGV4dCAvSW1hZ2VCIC9JbWFnZUMgL0ltYWdlSSBdIC9YT2JqZWN0IDw8Ci9Gb3JtWG9iLjk0Mzk3YmQ4YWI0ZWNmNzY3YjE5OGMyZDZi" +
        "M2UyYjJiIDMgMCBSCj4+Cj4+IC9Sb3RhdGUgMCAvVHJhbnMgPDwKCj4+IAogIC9UeXBlIC9QYWdlCj4+CmVuZG9iago1IDAgb2JqCjw8Ci9QYWdlTW9kZSAv" +
        "VXNlTm9uZSAvUGFnZXMgNyAwIFIgL1R5cGUgL0NhdGFsb2cKPj4KZW5kb2JqCjYgMCBvYmoKPDwKL0F1dGhvciAoYW5vbnltb3VzKSAvQ3JlYXRpb25EYXRl" +
        "IChEOjIwMDAwMTAxMDAwMDAwKzAwJzAwJykgL0NyZWF0b3IgKGFub255bW91cykgL0tleXdvcmRzICgpIC9Nb2REYXRlIChEOjIwMDAwMTAxMDAwMDAwKzAw" +
        "JzAwJykgL1Byb2R1Y2VyIChSZXBvcnRMYWIgUERGIExpYnJhcnkgLSBcKG9wZW5zb3VyY2VcKSkgCiAgL1N1YmplY3QgKHVuc3BlY2lmaWVkKSAvVGl0bGUg" +
        "KHVudGl0bGVkKSAvVHJhcHBlZCAvRmFsc2UKPj4KZW5kb2JqCjcgMCBvYmoKPDwKL0NvdW50IDEgL0tpZHMgWyA0IDAgUiBdIC9UeXBlIC9QYWdlcwo+Pgpl" +
        "bmRvYmoKOCAwIG9iago8PAovRmlsdGVyIFsgL0FTQ0lJODVEZWNvZGUgL0ZsYXRlRGVjb2RlIF0gL0xlbmd0aCAxMjcKPj4Kc3RyZWFtCkdhcEBDMGFgRmIm" +
        "LVZsV2BQK1RZYWNWcmtlQjE2T0BJJEFMNkk+YEtNVEVTLDFDV2xsVDV0JjBLUV5oLzc2ODk1I2giWWRkNylvWm11a0dyOmNtND5YQXRgPC9kQy5EOiQnSC9u" +
        "M0snWGckVzImQEZaaV44ZGcxazIob2lcfj5lbmRzdHJlYW0KZW5kb2JqCnhyZWYKMCA5CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDA2MSAwMDAwMCBu" +
        "IAowMDAwMDAwMDkyIDAwMDAwIG4gCjAwMDAwMDAxOTkgMDAwMDAgbiAKMDAwMDAzMTEzMiAwMDAwMCBuIAowMDAwMDMxMzg4IDAwMDAwIG4gCjAwMDAwMzE0" +
        "NTYgMDAwMDAgbiAKMDAwMDAzMTcxNyAwMDAwMCBuIAowMDAwMDMxNzc2IDAwMDAwIG4gCnRyYWlsZXIKPDwKL0lEIApbPDFjMTc4MTk4ZmJkZmE1MWIyNTk5" +
        "NWQ4OWQ0MTAyMDQzPjwxYzE3ODE5OGZiZGZhNTFiMjU5OTVkODlkNDEwMjA0Mz5dCiUgUmVwb3J0TGFiIGdlbmVyYXRlZCBQREYgZG9jdW1lbnQgLS0gZGln" +
        "ZXN0IChvcGVuc291cmNlKQoKL0luZm8gNiAwIFIKL1Jvb3QgNSAwIFIKL1NpemUgOQo+PgpzdGFydHhyZWYKMzE5OTMKJSVFT0YK");

static async Task M3d1RealRecognition(string extension, string encoded)
{
    using var setup = new TestContext();
    string path = Path.Combine(setup.Workspace, "real-ocr." + extension);
    File.WriteAllBytes(path, Convert.FromBase64String(encoded));
    string hash = M3bHash(path);
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace));
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path, page = 1, languageTag = "en"}), default);
    Assert(receipt.Success, "Real OCR failed: " + receipt.ErrorCode + " " + receipt.Summary + " " + receipt.Data.GetRawText());
    string text = receipt.Data.GetProperty("text").GetString() ?? "";
    Assert(receipt.Data.GetProperty("backend").GetString() == "rapidocr-cpu" &&
        text.Contains("Inventory", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("chairs", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("24", StringComparison.Ordinal) && text.Contains("4827", StringComparison.Ordinal),
        "Real CPU OCR did not recognize expected inventory fields: " + text);
    Assert(!receipt.ChangedState && M3bHash(path) == hash &&
        receipt.Data.GetProperty("sourceSha256").GetString() == hash, "Real OCR changed the source.");
}

static async Task M3d1Diagnostic()
{
    using var setup = new TestContext();
    string path = M3cFixture(setup);
    var tool = new OcrReadTool(new WorkspacePathResolver(setup.Workspace), (_, _, _, _) =>
        Task.FromResult(new OcrPageResult(false, "OCR_BACKENDS_FAILED", "", "", 0, 0, 0)
            { Backend = "rapidocr-cpu + windows-ocr", Diagnostic = "RapidOCR: OCR_CPU_FAILED; Windows: OCR_INVALID_RESULT" }));
    var receipt = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new {path}), default);
    Assert(!receipt.Success && !receipt.ChangedState && receipt.ErrorCode == "OCR_BACKENDS_FAILED" &&
        receipt.Data.GetProperty("diagnostic").GetString()!.Contains("OCR_INVALID_RESULT", StringComparison.Ordinal),
        "Both backend failures must be visible without inventing text.");
}

static VisionProvider M3eProvider() => new(new Uri("https://vision.example/v1/"), "visual-test", () => "test-vision-key");

static string M3eImage(TestContext setup)
{
    string path = Path.Combine(setup.Workspace, "vision.png");
    File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAoAAAAGkCAIAAAAuegPJAAAIGElEQVR42u3bzU3zWhSGUYzSARINOBN3kTpojA5oKZO4gTSxmSIxCQh8ft61RndwpWvvbx89Pl90l6p6AgCO9WwEACDAACDAAIAAA4AAAwACDAACDAAIMAAIMAAgwAAgwAAgwACAAAOAAAMAAgwAAgwACDAACDAAIMAAIMAAIMAAgAADgAADAAIMAAIMAAgwAAgwACDAACDAACDAAIAAA4AAAwACDAACDAAIMAAIMAAgwAAgwAAgwACAAAOAAAMAAgwAAgwACDAACDAAIMAAIMAAIMAAgAADwKxORtCh27o98q+d96tZ2Q27gd0Y1FJVpjDQ4XGo7IbdwG4IMI0Pj0NlN+yG3bAbAkwXR8hxsht2w27YDQGm2RFynOyG3bAbdkOAaXaEHCe7YTfsht0QYEdo6+ExHCe7YTfsht3oh/8POOUUdfUk2A3sBgKctbvOkj8Lu2E37EYn/BV06Mr6ayW7YTfsht1wA3aKPKHd8ITYDQEmZkedJTO3G2ZuNwTYKfK0dsPT2g1PK8Ak7aWzZM52w5zthgA7RZ7cbnhyu+HJBZikXXSWzNZumK3dEGAAEGBiPgN9zJqq3TBVuyHA9s+7mKd3sRveRYBJ2jxnySTthkl6IwEGAAEm5qPPx6wZ2g0z9F4CDAACTMznno9Z07MbpuftBBgABJiYDz0fs+ZmN8zNOwowAAiwz9iYTzwfsybmTU3MmwowAAgwACDAACDAk0v7ecPPOWblfc3K+wowAAgwACDAACDA08r8YcPPOabkrU3JWwswAAgwACDAACDAAIAAA4AAAwACDAACDAACDAAIMAAIMAAgwAAgwNM671dvjSnZDVPy1gIMAAIMAAgwAAjwPNJ+2PBDjll5X7PyvgIMAAIMAAgwAAjw5HJ+3vBDjol5UxPzpgIMAALsYzbgE89nrLnZDXPzjgIMAAJMwIeez1jTsxum5+0EGAAEmIDPPZ+xZmg3zNB7CTAACDABH30+Y03SbpikNxJgm+ddzNO72A3vIsDMvn9OkanaDVO1GwIMAAJMwGegz1iztRtmazcE2C56crvhye2GJxdgZt9Ip8ic7YY52w0BdpY8rd3wtHbD0wows2+nU2TmdsPM7YYAO0ue0G54QrthN7IsVWUK/+e2bo4QdgO7gRtw+tY6Rf4s7IbdsBsC7Cw5RXbDbmA3cvkr6OM0/GslR8hu2A27YTcE2HHaHCHsBnYDAZ72ODlCdsNu2A27IcAcepwcIbthN+yG3RBgDjpUDo/dsBt2w24IMAcdKofHbtgN7IYAAwA/4P8DBgABBgABBgAEGAAEGAAQYAAQYABAgAFAgAEAAQYAAQYAAQYABBgABBgAEGAAEGAAQIABQIABAAEGAAEGAAEGAAQYAAQYABBgABBgAECAAUCAAQABBgABBgABBgAEGAAEGAAQYAAQYABAgAFAgAEAAQYAAQYAAQYABBgABBgAEGAAEGAAQIABQIABAAEGAAEGAAEGAAQYAAQYABBgABjayQj4nde33RC+un+shmA37AZuwAAgwACAAAOAAAOAAAMAAgwAAgwACDAACDAAIMAAIMAAgAADgAADgAADAAIMAAIMAAgwAAgwACDAACDAAIAAA4AAA4AAAwACDAACDAAIMAAIMAAgwAAgwACAAAOAAAOAAAMAAgwAAgwACDAACDAAIMAAIMAAgAADgAADgAADAAIMAAIMAAgwAAgwACDAACDAAIAAA4AAAwACDAACDAACDAAIMAAIMAAgwAAgwACAAAOAAAMAAgwAAgwAAgwACDAACDAAIMAAIMAAgAADgAADAAIMAAIMAAIMAAgwAAgwACDAACDAAIAAA4AAAwACDAACDAACDAAIMAAIMAAgwAAgwACAAAOAAAMAAgwAAgwAAgwACDAACDAA8FeWqjIFAHADBgABBgAEGAAEGAAQYAAQYABAgAFAgAFAgI0AAAQYAAQYABBgABBgAECAAUCAAQABBgABBgAEGAAEGAAEGAAQYAAQYABAgAFAgAEAAQYAAQYABBgABBgABBgYwcv7xRBAgIEG9dVgEGAAQIAh4Pr7/Z8BAQYABBgmvf66BIMAA23qq8EgwACAAEPM9dclGAQYaFNfDQYBBgAEGGKuvy7BIMAAgABDzPXXJRgEGGhTXw0GAQYABBhirr8uwSDAQJv6ajAIMAAgwBBz/XUJBgEGAAQYYq6/LsEgwEDLXmowCDAAIMAQcP11CQYBBlo2UoNBgAFAgIGA669LMAgwAAgwEHP9dQkGAQZaFlGDQYABQICBgOuvSzAIMKjvJfa/DgIMAAgwuP66BIMAAwACDK6/LsEgwMAszdNgEGAAEGAg5rrpEgwCDOrr2UCAAQABBtdfl2AQYABAgMHl0iUYBBgYt2oaDAIMAAIMxFwoXYJBgEF9PTkIMAAgwOAS6flBgAEAAQbXR28BAgzq610AAQYAAQbXX28EAgxolQaDAAOAAAPZ10SXYBBgABBgIOaC6BIMAgzK5E1BgAEAAQaXQu8LAgxq5K0BAQYAAQYXQe8OAgwACDC4ApoACDCgPeYAAgwAAgyufaYBCDDojZmAAAMAAgyueiYDAgwAAgy45JkPCDCoiymBAAMAAgwudmYFw1uqyhQAwA0YAAQYABBgABBgAECAAUCAAQABBgABBgABNgIAEGAAEGAAQIABQIABAAEGAAEGAAQYAAQYABBgABBgABBgAECAAUCAAQABBgABBgAEGAAEGAAQYAAQYAAQYABAgAFAgAEAAQYAAQYAHvEJ3OlUdWoJ+m4AAAAASUVORK5CYII="));
    return path;
}

static JsonElement M3eArgs(string path) => JsonSerializer.SerializeToElement(new { path, question = "Count and identify the colored shapes." });

static Task M3eRegistry()
{
    using var setup = new TestContext();
    using var http = new HttpClient();
    var ordinary = NiraStandardTools.Create(setup.Workspace, setup.Store, http);
    var enabled = NiraStandardTools.Create(setup.Workspace, setup.Store, http, vision: M3eProvider());
    Assert(!ordinary.TryGet("vision.analyze", out _) && enabled.TryGet("vision.analyze", out var tool) &&
        tool is not null && tool.Descriptor.Risk == ToolRisk.ReadExternal && !tool.Descriptor.HasSideEffects &&
        enabled.TryGet("productivity.ocr_read", out _), "Vision must be opt-in, external read, and retain OCR.");
    Assert(!enabled.Validate("vision.analyze", "{\"path\":\"a.png\",\"question\":\"What is it?\",\"endpoint\":\"https://evil.example/\"}", out _, out _),
        "Model must never change the destination.");
    return Task.CompletedTask;
}

static Task M3eEndpoint()
{
    foreach (string url in new[] { "http://vision.example/v1/", "https://user:pass@vision.example/v1/", "https://vision.example/v1/?key=x", "https://vision.example/v1" })
    {
        bool rejected = false;
        try { _ = new VisionProvider(new Uri(url), "model", () => null); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "Unsafe endpoint accepted: " + url);
    }
    var local = new VisionProvider(new Uri("http://127.0.0.1:11434/v1/"), "local-model", () => null);
    Assert(local.Endpoint.AbsoluteUri == "http://127.0.0.1:11434/v1/chat/completions", "Local endpoint incorrect.");
    return Task.CompletedTask;
}

static async Task M3eWire()
{
    using var setup = new TestContext();
    string path = M3eImage(setup), hash = M3bHash(path);
    var handler = new FakeApiHandler("{\"choices\":[{\"message\":{\"content\":\"Three red circles, two blue squares and a green triangle.\"}}]}");
    using var http = new HttpClient(handler);
    var tool = new VisionAnalyzeTool(new WorkspacePathResolver(setup.Workspace), http, M3eProvider());
    ToolReceipt r = await tool.ExecuteAsync(M3eArgs(path), default);
    Assert(r.Success && !r.ChangedState && handler.Calls == 1, "Image wire operation failed: " + r.Summary);
    using var doc = JsonDocument.Parse(handler.RequestBody!);
    JsonElement body = doc.RootElement;
    JsonElement parts = body.GetProperty("messages")[0].GetProperty("content");
    string uri = parts[1].GetProperty("image_url").GetProperty("url").GetString()!;
    const string prefix = "data:image/jpeg;base64,";
    Assert(uri.StartsWith(prefix, StringComparison.Ordinal) && body.GetProperty("model").GetString() == "visual-test" &&
        !body.TryGetProperty("tools", out _) && handler.AuthorizationHeader == "Bearer test-vision-key", "Wrong vision protocol.");
    byte[] sent = Convert.FromBase64String(uri[prefix.Length..]);
    Assert(sent.Length > 100 && sent[0] == 0xff && sent[1] == 0xd8 &&
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sent)).ToLowerInvariant() == r.Data.GetProperty("sentImageSha256").GetString(), "Image bytes do not match receipt.");
    Assert(r.Data.GetProperty("sourceSha256").GetString() == hash && M3bHash(path) == hash &&
        !r.Data.GetProperty("independentlyVerified").GetBoolean() &&
        r.Data.GetProperty("analysis").GetString()!.Contains("green triangle", StringComparison.Ordinal), "Source or analysis evidence incorrect.");
    Assert(!handler.RequestBody!.Contains(path, StringComparison.Ordinal) && !r.Data.GetRawText().Contains("test-vision-key", StringComparison.Ordinal), "Path or credential leaked.");
}

static async Task M3eDenial()
{
    using var setup = new TestContext();
    var handler = new FakeApiHandler("{}");
    using var http = new HttpClient(handler);
    var tool = new VisionAnalyzeTool(new WorkspacePathResolver(setup.Workspace), http, M3eProvider());
    string path = M3eImage(setup);
    Assert(tool.GetTarget(M3eArgs(path)).Contains("https://vision.example/v1/chat/completions", StringComparison.Ordinal), "Approval must display destination.");
    var model = new FakeModel(new ModelReply[] { new(null, [Call("vision-denied", "vision.analyze", new { path, question = "Describe it" })]) });
    var approvals = new FakeApprovals(false);
    var engine = new NiraEngine(model, new ToolRegistry([tool]), setup.Store, new WorkspaceAuthority(setup.Workspace));
    var result = await engine.RunAsync(Guid.NewGuid(), "Describe the image", approvals);
    Assert(result.Status == ConversationStatus.Paused && approvals.Calls == 1 && handler.Calls == 0, "Denied image transfer occurred.");
}

static async Task M3eInvalid()
{
    using var setup = new TestContext();
    var handler = new FakeApiHandler("{}");
    using var http = new HttpClient(handler);
    var tool = new VisionAnalyzeTool(new WorkspacePathResolver(setup.Workspace), http, M3eProvider());
    string path = Path.Combine(setup.Workspace, "invalid.png");
    File.WriteAllText(path, "not an image");
    var bad = await tool.ExecuteAsync(M3eArgs(path), default);
    var pdf = await tool.ExecuteAsync(M3eArgs("sample.pdf"), default);
    Assert(!bad.Success && !pdf.Success && handler.Calls == 0, "Invalid image dispatched.");
}

static async Task M3eEmpty()
{
    using var setup = new TestContext();
    var handler = new FakeApiHandler("{\"choices\":[{\"message\":{\"content\":\"\"}}]}");
    using var http = new HttpClient(handler);
    var tool = new VisionAnalyzeTool(new WorkspacePathResolver(setup.Workspace), http, M3eProvider());
    var r = await tool.ExecuteAsync(M3eArgs(M3eImage(setup)), default);
    Assert(!r.Success && r.ErrorCode == "VISION_EMPTY" && handler.Calls == 1, "Empty vision result incorrectly succeeded: " + r.Summary);
}

static async Task M3eHttpError()
{
    using var setup = new TestContext();
    using var http = new HttpClient(new ModelRejectionHandler("private-response-token"));
    var tool = new VisionAnalyzeTool(new WorkspacePathResolver(setup.Workspace), http, M3eProvider());
    var r = await tool.ExecuteAsync(M3eArgs(M3eImage(setup)), default);
    Assert(!r.Success && r.ErrorCode == "VISION_PROVIDER_HTTP" && !r.Summary.Contains("private-response-token", StringComparison.Ordinal), "HTTP failure leaked or succeeded.");
}

static async Task M3eAgent()
{
    using var setup = new TestContext();
    string path = M3eImage(setup);
    var handler = new FakeApiHandler("{\"choices\":[{\"message\":{\"content\":\"Three red circles.\"}}]}");
    using var http = new HttpClient(handler);
    var tool = new VisionAnalyzeTool(new WorkspacePathResolver(setup.Workspace), http, M3eProvider());
    var model = new FakeModel(new ModelReply[] { new(null, [Call("vision-approved", "vision.analyze", new { path, question = "Count circles" })]), new("Three red circles.", []) });
    var engine = new NiraEngine(model, new ToolRegistry([tool]), setup.Store, new WorkspaceAuthority(setup.Workspace));
    var approvals = new FakeApprovals(true);
    Guid id = Guid.NewGuid();
    var result = await engine.RunAsync(id, "Count the circles", approvals);
    var saved = await setup.Store.LoadAsync(id, default);
    Assert(result.Status == ConversationStatus.Completed && approvals.Calls == 1 && handler.Calls == 1 &&
        saved is not null && saved.Operations.Any(op => op.ToolName == "vision.analyze" && op.Status == OperationStatus.Succeeded && op.ResultJson!.Contains("sentImageSha256", StringComparison.Ordinal)), "Approved vision result did not reach the durable journal.");
}

internal sealed class GenericRecoveryTool(string name, bool effect, Func<JsonElement, ToolReceipt> execute) : IAgentTool
{
    public int Calls { get; private set; }
    public ToolDescriptor Descriptor { get; } = new(name, "Test capability", effect ? ToolRisk.Dangerous : ToolRisk.ReadLocal,
        JsonToolSchema.Schema("""{"type":"object","properties":{"target":{"type":"string"}},"required":["target"],"additionalProperties":false}"""), effect);
    public string GetTarget(JsonElement args) => "external-test/" + args.GetProperty("target").GetString();
    public Task<ToolReceipt> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    { Calls++; return Task.FromResult(execute(arguments)); }
}

internal sealed class SequenceDesktopBackend : IDesktopAutomationBackend, IDesktopWindowCaptureBackend
{
    public int AdditionalControls;
    public List<string> Invocations { get; } = [];
    public bool StaleAfterFirst, WrongDisplay, NonemptyFirst;
    public bool DuplicateFirstName;
    public int OccupancyChecks;
    public string InitialDisplay = "Initial result";
    public string FinalDisplay = "Final result";
    public string? Typed;
    private static DesktopWindow Item => new("win-seq-0001", "Test window", "test", 3001, true, false);
    public IReadOnlyList<DesktopWindow> ListWindows() => [Item];
    public DesktopInspection Inspect(string windowId)
    {
        var controls = new List<DesktopControl> {
            new("ctrl-first-01", "First", "ControlType.Button", "first", true, false, true, false),
            new("ctrl-display-01", Invocations.Count == 2 && !WrongDisplay ? FinalDisplay : InitialDisplay, "ControlType.Text", "display", true, false, false, false),
            new(Invocations.Count == 0 ? "ctrl-editor-01" : "ctrl-editor-02", "Editor", "ControlType.Document", "editor", true, false, false, false, true)
        };
        if (!StaleAfterFirst || Invocations.Count == 0) controls.Add(new("ctrl-second-02", "Second", "ControlType.Button", "second", true, false, true, false));
        if (DuplicateFirstName) controls.Add(new("ctrl-other-first", "First", "ControlType.Button", "other", true, false, true, false));
        for (int i = 0; i < AdditionalControls; i++)
            controls.Add(new DesktopControl("ctrl-extra-" + i, "Additional label " + i, "ControlType.Text", "extra-" + i, true, false, false, false));
        return new DesktopInspection(Item, controls, false);
    }
    public DesktopInspection Inspect(string windowId, bool checkEditorEmpty)
    {
        var result = Inspect(windowId);
        if (!checkEditorEmpty) return result;
        OccupancyChecks++;
        return result with { EditorEmptyByControlId = result.Controls.Where(c => c.CanTypeText)
            .ToDictionary(c => c.ControlId, c => (bool?)!NonemptyFirst, StringComparer.Ordinal) };
    }
    public int CaptureCalls { get; private set; }
    public DesktopWindowCapture Capture(string windowId, string title)
    {
        if (windowId != Item.WindowId || title != Item.Title || Invocations.Count != 2)
            throw new DesktopAutomationException("CAPTURE_STALE", "Expected completed action sequence and exact window.");
        CaptureCalls++;
        return new DesktopWindowCapture(Item, CaptureTestBackend.FixturePng(), 3, 2);
    }
    public DesktopAction Invoke(string windowId, string controlId)
    { Invocations.Add(controlId); return new DesktopAction(true, true, "Dispatched", Verified: false); }
    public DesktopAction Activate(string windowId) => throw new NotSupportedException();
    public DesktopAction SetText(string windowId, string controlId, string text) => throw new NotSupportedException();
    public DesktopAction TypeText(string windowId, string controlId, string text)
    {
        if (NonemptyFirst && controlId == "ctrl-editor-01") return new DesktopAction(false, false, "Not empty", "EDITOR_NOT_EMPTY");
        Typed = text; return new DesktopAction(true, true, "Typed and verified", Verified: true);
    }
}

internal sealed class FakeDesktopBackend : IDesktopAutomationBackend
{
    public int ListCalls, InspectCalls, ActivateCalls, InvokeCalls, SetTextCalls, TypeTextCalls;
    public bool UnverifiedTypeText, UnverifiedSetText;
    public string? LastText;
    private const string Window = "win-demo-0001";
    private static DesktopWindow Item => new(Window, "Demo editor", "demo", 3000, false, false);
    public IReadOnlyList<DesktopWindow> ListWindows()
    { ListCalls++; return new[] { Item }; }
    public DesktopInspection Inspect(string windowId)
    {
        InspectCalls++;
        if (windowId != Window) throw new DesktopAutomationException("WINDOW_STALE", "window changed");
        return new DesktopInspection(Item, new DesktopControl[] {
            new("ctrl-button-01", "Run", "ControlType.Button", "run", true, false, true, false),
            new("ctrl-edit-001", "Editor", "ControlType.Edit", "editor", true, false, false, true),
            new("ctrl-doc-01", "Rich editor", "ControlType.Document", "document", true, false,
                false, false, true)
        }, false);
    }
    public DesktopAction Activate(string windowId)
    {
        if (windowId != Window) throw new DesktopAutomationException("WINDOW_STALE", "window changed");
        ActivateCalls++;
        return new DesktopAction(true, true, "Observed selected window in foreground", Verified: true);
    }
    public DesktopAction Invoke(string windowId, string controlId)
    {
        if (windowId != Window || controlId != "ctrl-button-01")
            throw new DesktopAutomationException("CONTROL_STALE", "control changed");
        InvokeCalls++;
        return new DesktopAction(true, true, "Button invocation dispatched; outcome unknown", Verified: false);
    }
    public DesktopAction SetText(string windowId, string controlId, string text)
    {
        if (windowId != Window || controlId is not ("ctrl-edit-001" or "ctrl-doc-01"))
            throw new DesktopAutomationException("CONTROL_STALE", "control changed");
        if (controlId == "ctrl-doc-01")
            return new DesktopAction(false, false,
                "Rich editor has TextPattern, not ValuePattern; requires separately approved foreground input.",
                "CONTROL_REQUIRES_KEYBOARD");
        SetTextCalls++; LastText = text;
        return new DesktopAction(true, true, UnverifiedSetText
            ? "Edit value sent but not verified" : "Text set and verified",
            Verified: !UnverifiedSetText);
    }
    public DesktopAction TypeText(string windowId, string controlId, string text)
    {
        if (windowId != Window || controlId != "ctrl-doc-01")
            throw new DesktopAutomationException("CONTROL_STALE", "control changed");
        TypeTextCalls++; LastText = text;
        return new DesktopAction(true, true, UnverifiedTypeText
            ? "Keyboard input dispatched, actual text differs" : "Approved typing verified",
            Verified: !UnverifiedTypeText);
    }
}

internal sealed class FakeMultipleBrowserCloser : IAgentTool
{
    public List<string> Ids { get; } = [];
    public ToolDescriptor Descriptor { get; } =
        new BrowserSessionCloseTool(new FakePublicBrowserSessions()).Descriptor;
    public string GetTarget(JsonElement arguments) => "workspace";
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string id = args.GetProperty("sessionId").GetString()!;
        Ids.Add(id);
        return Task.FromResult(ToolReceipt.Ok("Closed and discarded public browser session",
            new { sessionId = id }, changedState: true));
    }
}

internal sealed class PlanningTestModel(ModelReply planningDecision, FakeModel execution,
    ModelReply? planningFallback = null) : IChatModel, IWorkflowPlanningModel
{
    public int PlanCalls { get; private set; }
    public int FallbackCalls { get; private set; }
    public int FallbackToolCount { get; private set; } = -1;
    public ToolDescriptor? DecisionTool { get; private set; }
    public Task<ModelReply> ProposeWorkflowAsync(IReadOnlyList<ChatEntry> messages,
        ToolDescriptor decisionTool, CancellationToken ct)
    {
        PlanCalls++;
        DecisionTool = decisionTool;
        return Task.FromResult(planningDecision);
    }
    public Task<ModelReply> CompleteAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, CancellationToken ct)
    {
        if (messages.Count == 2 && messages[0].Role == "system" &&
            messages[0].Content?.Contains("NIRA PLANNING FALLBACK", StringComparison.Ordinal) == true)
        {
            FallbackCalls++;
            FallbackToolCount = tools.Count;
            return Task.FromResult(planningFallback ?? new ModelReply("{}", []));
        }
        return execution.CompleteAsync(messages, tools, ct);
    }
}

/// <summary>Real model transport: invalid native planning, strict tool-free
/// JSON retry, then a normal structured-tool execution transcript.</summary>
internal sealed class PlanningRetryWireHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken ct)
    {
        string body = await request.Content!.ReadAsStringAsync(ct);
        using JsonDocument parsed = JsonDocument.Parse(body);
        JsonElement input = parsed.RootElement;
        JsonElement[] messages = input.GetProperty("messages").EnumerateArray().ToArray();
        int round = RequestCount++;
        if (round == 0)
        {
            JsonElement[] functions = input.GetProperty("tools").EnumerateArray().ToArray();
            if (functions.Length != 1 ||
                functions[0].GetProperty("function").GetProperty("name").GetString() != "nira_decide_workflow" ||
                messages.Length != 2)
                throw new InvalidOperationException("Initial planning preflight exposed executor tools");
            return Answer(new { role="assistant", content="I have an unstructured idea." });
        }
        if (round == 1)
        {
            if (input.TryGetProperty("tools", out _) || input.TryGetProperty("tool_choice", out _) ||
                messages.Length != 2 ||
                messages[0].GetProperty("role").GetString() != "system" ||
                !messages[0].GetProperty("content").GetString()!.Contains("NIRA PLANNING FALLBACK", StringComparison.Ordinal) ||
                messages[1].GetProperty("role").GetString() != "user" ||
                messages.Any(m => m.TryGetProperty("tool_calls", out _) || m.TryGetProperty("tool_call_id", out _)))
                throw new InvalidOperationException("Planning fallback offered executable tools or provider history");
            return Answer(new { role="assistant", content=JsonSerializer.Serialize(new {
                mode="plan",steps=new[] {
                    new { description="Analyze observation one", successCriterion="First interpretation verified", kind="analysis" },
                    new { description="Analyze observation two", successCriterion="Second interpretation verified", kind="analysis" }
                }
            }) });
        }
        if (round == 2 || round == 3)
        {
            JsonElement[] functions = input.GetProperty("tools").EnumerateArray().ToArray();
            if (functions.Any(f => f.GetProperty("function").GetProperty("name").GetString() == "nira_decide_workflow") ||
                !functions.Any(f => f.GetProperty("function").GetProperty("name").GetString() == "nira_plan_update") ||
                !messages.Any(m => m.GetProperty("role").GetString() == "system" &&
                    m.GetProperty("content").GetString()!.Contains("A durable plan was already created automatically", StringComparison.Ordinal)))
                throw new InvalidOperationException("Execution transcript lost its approved plan or tool isolation");
            if (round == 3 && !messages.Any(m =>
                m.GetProperty("role").GetString() == "tool" && m.TryGetProperty("tool_call_id", out _)))
                throw new InvalidOperationException("Missing first plan-update receipt in provider history");
            return Answer(new { role="assistant", content=(string?)null,
                tool_calls=new[] {new { id="update-"+round, type="function", function=new {
                    name="nira_plan_update",
                    arguments=JsonSerializer.Serialize(new { index=round-2, status="completed" })
                }}}
            });
        }
        if (round == 4)
        {
            if (messages.Count(m => m.GetProperty("role").GetString() == "tool") < 2)
                throw new InvalidOperationException("Execution receipts missing before final answer");
            return Answer(new { role="assistant", content="Both analysis steps are complete." });
        }
        throw new InvalidOperationException("Unexpected model request after verified completion");
    }
    private static HttpResponseMessage Answer(object message) => new(System.Net.HttpStatusCode.OK) {
        Content = new StringContent(JsonSerializer.Serialize(new {
            choices=new[] {new {message}}
        }), System.Text.Encoding.UTF8, "application/json")
    };
}

internal sealed class TestContext : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nira-kernel-" + Guid.NewGuid().ToString("N"));
    public string Workspace { get; }
    public string Database => Path.Combine(Root, "test.db");
    public SqliteConversationStore Store { get; }
    private readonly HttpClient _http = new();
    public TestContext()
    {
        Workspace = Path.Combine(Root, "workspace");
        Directory.CreateDirectory(Workspace);
        Store = new SqliteConversationStore(Database);
    }
    public NiraEngine Engine(IChatModel model, IDesktopAutomationBackend? desktop = null, bool routing = false,
        bool adaptive = false) => new(model,
        NiraStandardTools.Create(Workspace, Store, _http, desktopBackend: desktop), Store,
        new WorkspaceAuthority(Workspace), enableCapabilityRouting: routing, enableAdaptivePlanning: adaptive);
    public void Dispose()
    {
        _http.Dispose();
        // SQLite pooling may hold database handles; clearing ensures cleanup on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
    }
}

internal sealed class FakeApprovals(bool approved) : IApprovalBroker
{
    public int Calls { get; private set; }
    public List<ApprovalRequest> Requests { get; } = [];
    public Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        Calls++;
        Requests.Add(request);
        return Task.FromResult(approved);
    }
}

internal sealed class ScriptedApprovals(IEnumerable<bool> outcomes) : IApprovalBroker
{
    private readonly Queue<bool> _responses = new(outcomes);
    public int Calls { get; private set; }
    public Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        Calls++;
        if (_responses.Count==0) throw new InvalidOperationException("Unexpected extra approval request");
        return Task.FromResult(_responses.Dequeue());
    }
}

internal sealed class M5bDesktopProgress : IStreamingProgress
{
    public void Report(AgentProgress value) { }
}

internal sealed class M9MetricsProgress : IProgress<AgentProgress>
{
    public List<AgentProgress> Events { get; } = [];
    public void Report(AgentProgress value) => Events.Add(value);
}

internal sealed class FakeModel : IChatModel
{
    private readonly Queue<ModelReply>? _script;
    private readonly Func<IReadOnlyList<ChatEntry>, ModelReply>? _respond;
    public List<IReadOnlyList<ToolDescriptor>> ToolSnapshots { get; } = [];
    public List<IReadOnlyList<ChatEntry>> PromptSnapshots { get; } = [];
    public FakeModel(IEnumerable<ModelReply> replies) { _script = new Queue<ModelReply>(replies); }
    public FakeModel(Func<IReadOnlyList<ChatEntry>, ModelReply> responder) { _respond = responder; }
    public Task<ModelReply> CompleteAsync(IReadOnlyList<ChatEntry> messages,
        IReadOnlyList<ToolDescriptor> tools, CancellationToken ct)
    {
        ToolSnapshots.Add(tools.ToArray());
        PromptSnapshots.Add(messages.ToArray());
        if (_respond is not null) return Task.FromResult(_respond(messages));
        if (_script is null || _script.Count == 0) throw new InvalidOperationException("Fake model out of script");
        return Task.FromResult(_script.Dequeue());
    }
}

internal sealed class EmptyContextSequenceHandler(bool alwaysEmpty = false) : HttpMessageHandler
{
    public List<string> RequestBodies { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        string body = alwaysEmpty || RequestBodies.Count == 1
            ? "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":null,\"reasoning_content\":\"private\"}}]}"
            : "{\"choices\":[{\"message\":{\"content\":\"Recovered visible response\"}}]}";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class ContextRecoverySequenceHandler(
    bool alwaysReject = false,
    string errorBody = "{\"error\":{\"code\":\"context_length_exceeded\"}}")
    : HttpMessageHandler
{
    public List<string> RequestBodies { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        RequestBodies.Add(request.Content is null ? "" :
            await request.Content.ReadAsStringAsync(ct));
        if (alwaysReject || RequestBodies.Count == 1)
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) {
                Content = new StringContent(errorBody, System.Text.Encoding.UTF8, "application/json")
            };
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(
                "{\"choices\":[{\"message\":{\"content\":\"Recovered normal answer\"}}]}",
                System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class ModelRejectionHandler(string responseBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        });
}

// The second network segment remains unavailable until the test explicitly releases it.
internal sealed class M8d2GatedStream(string first, string last) : Stream
{
    private readonly byte[] _first = System.Text.Encoding.UTF8.GetBytes(first);
    private readonly byte[] _last = System.Text.Encoding.UTF8.GetBytes(last);
    private readonly TaskCompletionSource<bool> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _position;
    public void Release() => _gate.TrySetResult(true);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_position < _first.Length)
        {
            int count = Math.Min(buffer.Length, _first.Length - _position);
            _first.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        await _gate.Task.WaitAsync(cancellationToken);
        int index = _position - _first.Length;
        int remaining = Math.Min(buffer.Length, _last.Length - index);
        if (remaining <= 0) return 0;
        _last.AsMemory(index, remaining).CopyTo(buffer);
        _position += remaining;
        return remaining;
    }
}

internal sealed class M8d2GatedHandler(Stream body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new StreamContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
    }
}

internal sealed class M4b3StreamHandler(string body) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public bool StreamRequested { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        StreamRequested = json.RootElement.GetProperty("stream").GetBoolean();
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/event-stream") };
    }
}

internal sealed class FakeApiHandler(string responseBody) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? RequestBody { get; private set; }
    public string? AuthorizationHeader { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        AuthorizationHeader = request.Headers.Authorization?.ToString();
        RequestBody = request.Content is null ? null :
            await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>
/// Exercises the real OpenAI-compatible HTTP transport, model tool calls,
/// matching tool_call_id transcript, workspace writes, reads and hash results.
/// This is a deterministic wire-protocol integration test, not a live Ollama test.
/// </summary>
// Planning is an isolated, non-executing HTTP turn. Existing wire-pipeline tests
// must acknowledge it without counting it as an ordinary tool invocation.
// This also verifies that the preflight advertises NO executable operations.
internal static class WirePlanningPreflight
{
    public static HttpResponseMessage? AnswerIfRequested(JsonElement request)
    {
        if (!request.TryGetProperty("tools", out JsonElement tools) ||
            tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() != 1 ||
            tools[0].GetProperty("function").GetProperty("name").GetString() != "nira_decide_workflow")
            return null;

        JsonElement[] messages = request.GetProperty("messages").EnumerateArray().ToArray();
        if (messages.Length != 2 ||
            messages[0].GetProperty("role").GetString() != "system" ||
            messages[1].GetProperty("role").GetString() != "user" ||
            messages.Any(entry => entry.TryGetProperty("tool_calls", out _) ||
                entry.GetProperty("role").GetString() == "tool"))
            throw new InvalidOperationException("Planning request leaked an executable transcript");
        if (!tools[0].GetProperty("function").GetProperty("parameters")
                .GetProperty("properties").TryGetProperty("mode", out _))
            throw new InvalidOperationException("Planning decision schema is missing");

        // The legacy wire tests are about executable-tool transcripts. The
        // dedicated auto-planning tests separately exercise nontrivial plans.
        string response = JsonSerializer.Serialize(new { choices = new[] { new { message = new {
            role = "assistant", content = (string?)null,
            tool_calls = new[] { new { id = "wire-plan-only", type = "function",
                function = new { name = "nira_decide_workflow",
                    arguments = JsonSerializer.Serialize(new { mode = "direct",
                        steps = Array.Empty<object>() }) } } }
        } } } });
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class WireToolPipelineHandler(string workspace) : HttpMessageHandler
{
    public const string FileName = "wire-created.txt";
    public const string FileContent = "NIRA_TOOL_PIPELINE_CONFIRMED";
    public int PlanningRequests { get; private set; }
    public int RequestCount { get; private set; }
    public bool ReadbackSeen { get; private set; }
    public bool HashSeen { get; private set; }
    private string? _mutationId;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString() != "Bearer wire-test-key")
            throw new InvalidOperationException("Wire request lost the Bearer header");
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        HttpResponseMessage? planning = WirePlanningPreflight.AnswerIfRequested(json.RootElement);
        if (planning is not null) { PlanningRequests++; return planning; }
        JsonElement root = json.RootElement;
        var wire = root.GetProperty("messages").EnumerateArray().ToArray();
        int step = RequestCount++;
        var toolMessages = wire.Where(m => m.GetProperty("role").GetString() == "tool").ToArray();
        if (toolMessages.Length != step)
            throw new InvalidOperationException("Previous tool observations missing from provider transcript");
        if (step > 0)
        {
            JsonElement last = toolMessages[^1];
            string? callId = last.GetProperty("tool_call_id").GetString();
            var declared = wire.Where(m => m.GetProperty("role").GetString() == "assistant" &&
                m.TryGetProperty("tool_calls", out _))
                .SelectMany(m => m.GetProperty("tool_calls").EnumerateArray().ToArray())
                .Any(c => c.GetProperty("id").GetString() == callId);
            if (!declared) throw new InvalidOperationException("Tool response ID was not declared by assistant");
        }
        if (step == 3)
        {
            using var receipt = JsonDocument.Parse(toolMessages[^1].GetProperty("content").GetString()!);
            if (receipt.RootElement.GetProperty("ChangedState").ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("The write must have an actual changed-state receipt");
            _mutationId = receipt.RootElement.GetProperty("receiptId").GetString();
        }
        if (step == 4)
        {
            using var read = JsonDocument.Parse(toolMessages[^1].GetProperty("content").GetString()!);
            ReadbackSeen = read.RootElement.GetProperty("Data").GetProperty("content").GetString() == FileContent;
            if (!ReadbackSeen) throw new InvalidOperationException("Readback did not match the written bytes");
        }
        if (step == 5)
        {
            using var hash = JsonDocument.Parse(toolMessages[^1].GetProperty("content").GetString()!);
            string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(FileContent))).ToLowerInvariant();
            HashSeen = hash.RootElement.GetProperty("Data").GetProperty("sha256").GetString() == expected;
            if (!HashSeen) throw new InvalidOperationException("Independent SHA-256 did not match");
        }
        object parameters = step switch
        {
            0 => new { },
            1 => new { path = workspace },
            2 => new { path = FileName, content = FileContent },
            3 => new { path = FileName },
            4 => new { path = FileName },
            5 => new { outcome = "action", answer = "Created, read and hash-verified a file.",
                evidence_ids = new[] { _mutationId ?? throw new InvalidOperationException("No mutation receipt") } },
            _ => throw new InvalidOperationException("Unexpected HTTP model round")
        };
        string name = step switch
        {
            0 => "workspace.info", 1 => "filesystem.list", 2 => "filesystem.write_text",
            3 => "filesystem.read_text", 4 => "filesystem.sha256", 5 => "nira_finish",
            _ => throw new InvalidOperationException("Unexpected model tool")
        };
        string body = JsonSerializer.Serialize(new {
            choices = new[] { new { message = new { role = "assistant", content = (string?)null,
                tool_calls = new[] { new { id = "wire-" + step, type = "function",
                    function = new { name, arguments = JsonSerializer.Serialize(parameters) } } }
            } } }
        });
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class WireEditPipelineHandler(string path) : HttpMessageHandler
{
    public int PlanningRequests { get; private set; }
    public int Rounds { get; private set; }
    public bool ReadbackVerified { get; private set; }
    private string? _hash;
    private string? _editReceiptId;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString() != "Bearer integration-key")
            throw new InvalidOperationException("Authorization header lost in edit wire test");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        HttpResponseMessage? planning = WirePlanningPreflight.AnswerIfRequested(body.RootElement);
        if (planning is not null) { PlanningRequests++; return planning; }
        JsonElement[] messages = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        int round = Rounds++;
        JsonElement[] tools = messages.Where(m => m.GetProperty("role").GetString() == "tool").ToArray();
        if (tools.Length != round)
            throw new InvalidOperationException("Tool observation count mismatches provider turns");
        if (round == 1)
        {
            using var receipt = JsonDocument.Parse(tools[^1].GetProperty("content").GetString()!);
            _hash = receipt.RootElement.GetProperty("Data").GetProperty("sha256").GetString();
            if (_hash is null) throw new InvalidOperationException("Hash receipt missing");
        }
        if (round == 2)
        {
            using var receipt = JsonDocument.Parse(tools[^1].GetProperty("content").GetString()!);
            if (receipt.RootElement.GetProperty("ChangedState").ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("Edit did not yield a changed-state receipt");
            _editReceiptId = receipt.RootElement.GetProperty("receiptId").GetString();
        }
        if (round == 3)
        {
            using var receipt = JsonDocument.Parse(tools[^1].GetProperty("content").GetString()!);
            ReadbackVerified = receipt.RootElement.GetProperty("Data").GetProperty("content").GetString() == "V2 UPDATED\n";
            if (!ReadbackVerified) throw new InvalidOperationException("Readback mismatch");
        }
        string name = round switch {
            0 => "filesystem.sha256", 1 => "filesystem.edit_text",
            2 => "filesystem.read_text", 3 => "nira_finish",
            _ => throw new InvalidOperationException("Unexpected edit wire round")
        };
        object args = round switch {
            0 => new { path },
            1 => new { path, expectedSha256 = _hash, oldText = "V1 INITIAL", newText = "V2 UPDATED" },
            2 => new { path },
            3 => new { outcome = "action", answer = "Edited file and verified readback.",
                evidence_ids = new[] { _editReceiptId ?? throw new InvalidOperationException("Edit receipt not observed") } },
            _ => throw new InvalidOperationException("Unexpected edit wire round")
        };
        string serialized = JsonSerializer.Serialize(new {
            choices = new[] { new { message = new { role = "assistant", content = (string?)null,
                tool_calls = new[] { new { id = "edit-wire-" + round, type = "function",
                    function = new { name, arguments = JsonSerializer.Serialize(args) } } }
            } } }
        });
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(serialized, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class WirePermissionGateHandler : HttpMessageHandler
{
    public int PlanningRequests { get; private set; }
    public int Rounds { get; private set; }
    public bool HostDenialObserved { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString() != "Bearer wire-auth")
            throw new InvalidOperationException("Wire gate request lost model credential");
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        HttpResponseMessage? planning = WirePlanningPreflight.AnswerIfRequested(body.RootElement);
        if (planning is not null) { PlanningRequests++; return planning; }
        JsonElement[] entries = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        int turn = Rounds++;
        JsonElement[] receipts = entries.Where(x => x.GetProperty("role").GetString() == "tool").ToArray();
        if (turn == 1)
        {
            HostDenialObserved = receipts.Length == 1 &&
                receipts[0].GetProperty("content").GetString()?.Contains(
                    "USE_HOST_TOOL_APPROVAL", StringComparison.Ordinal) == true;
            if (!HostDenialObserved) throw new InvalidOperationException("Permission rejection was not preserved");
        }
        if (turn == 2 && (receipts.Length != 2 ||
            !receipts[1].GetProperty("content").GetString()!.Contains("Current workspace", StringComparison.Ordinal)))
            throw new InvalidOperationException("Missing actual workspace observation");
        string response = turn switch
        {
            0 => ToolReply("ask-permission", "nira_ask_user",
                new { question = "May I inspect the workspace with the registered tool?" }),
            1 => ToolReply("inspect", "workspace.info", new { }),
            2 => JsonSerializer.Serialize(new { choices = new[] {
                new { message = new { role = "assistant", content = "Workspace inspected using the available tool." } }
            } }),
            _ => throw new InvalidOperationException("Unexpected provider round")
        };
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json")
        };
    }
    private static string ToolReply(string id, string name, object args) => JsonSerializer.Serialize(new {
        choices = new[] { new { message = new { role = "assistant", content = (string?)null,
            tool_calls = new[] { new { id, type = "function",
                function = new { name, arguments = JsonSerializer.Serialize(args) } } }
        } } }
    });
}

internal sealed class ModelAuthHttpHandler(System.Net.HttpStatusCode code) : HttpMessageHandler
{
    public string? RequestAuthorization { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestAuthorization = request.Headers.Authorization?.ToString();
        return Task.FromResult(new HttpResponseMessage(code));
    }
}

internal sealed class FakeBrowserReader : IBrowserPageReader
{
    public int Calls { get; private set; }
    public int TextCalls { get; private set; }
    public Task<BrowserSnapshot> ReadTextAsync(Uri target, CancellationToken ct)
    {
        TextCalls++;
        return Task.FromResult(new BrowserSnapshot(target.AbsoluteUri, FinalUrl, "Example Page", 200,
            "Example body", false, [], []));
    }
    public string FinalUrl { get; set; } = "https://www.example.com/";
    public Task<BrowserSnapshot> ReadAsync(Uri target, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new BrowserSnapshot(target.AbsoluteUri, FinalUrl,
            "Example Page", 200, "Example body", false,
            new[] { new BrowserLink("About", "https://www.example.com/about") }));
    }
}

internal sealed class BrowserWireHandler : HttpMessageHandler
{
    public int PlanningRequests { get; private set; }
    public int Rounds { get; private set; }
    public bool ObservedReceipt { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString() != "Bearer wire-browser-key")
            throw new InvalidOperationException("Browser wire tool lost model API authorization");
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        HttpResponseMessage? planning = WirePlanningPreflight.AnswerIfRequested(document.RootElement);
        if (planning is not null) { PlanningRequests++; return planning; }
        int step = Rounds++;
        JsonElement[] wire = document.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        if (step == 1)
        {
            JsonElement[] toolMessages = wire.Where(m => m.GetProperty("role").GetString() == "tool").ToArray();
            ObservedReceipt = toolMessages.Length == 1 &&
                toolMessages[0].GetProperty("content").GetString()!.Contains("Example Page", StringComparison.Ordinal);
            if (!ObservedReceipt) throw new InvalidOperationException("Page observation missing from wire transcript");
        }
        string body = step switch
        {
            0 => JsonSerializer.Serialize(new { choices = new[] { new { message = new {
                role = "assistant", content = (string?)null, tool_calls = new[] { new {
                    id = "browser-wire-0", type = "function", function = new {
                        name = "browser.inspect_public", arguments = "{\"url\":\"https://www.example.com/\"}" } } }
            } } } }),
            1 => JsonSerializer.Serialize(new { choices = new[] { new { message = new {
                role = "assistant", content = "The observed title was Example Page."
            } } } }),
            _ => throw new InvalidOperationException("Unexpected additional browser tool round")
        };
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class BrowserJourneyReader : IBrowserPageReader
{
    public int Calls { get; private set; }
    public string SourceLinkUrl { get; set; } = "https://www.example.com/about";
    public string SourceFinalUrl { get; set; } = "https://www.example.com/";
    public string DestinationFinalUrl { get; set; } = "https://www.example.com/about";
    public Task<BrowserSnapshot> ReadAsync(Uri target, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Calls == 1
            ? new BrowserSnapshot(target.AbsoluteUri, SourceFinalUrl, "Source Page", 200,
                "source content", false, [new BrowserLink("About", SourceLinkUrl)])
            : new BrowserSnapshot(target.AbsoluteUri, DestinationFinalUrl, "About Page", 200,
                "destination content", false, []));
    }
}

internal sealed class BrowserLinkWireHandler : HttpMessageHandler
{
    public int PlanningRequests { get; private set; }
    public int Rounds { get; private set; }
    public bool ObservedReceipt { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.ToString()!="Bearer wire-link-key")
            throw new InvalidOperationException("Link wire authorization header not forwarded");
        using var requestJson=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        HttpResponseMessage? planning = WirePlanningPreflight.AnswerIfRequested(requestJson.RootElement);
        if (planning is not null) { PlanningRequests++; return planning; }
        JsonElement[] entries=requestJson.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        int round=Rounds++;
        if (round==1)
        {
            JsonElement[] messages=entries.Where(x=>x.GetProperty("role").GetString()=="tool").ToArray();
            ObservedReceipt=messages.Length==1 && messages[0].GetProperty("content").GetString()!
                .Contains("About Page",StringComparison.Ordinal);
            if (!ObservedReceipt) throw new InvalidOperationException("Model never received observed destination title");
        }
        string response=round switch
        {
            0 => JsonSerializer.Serialize(new { choices=new[] { new { message=new {
                role="assistant", content=(string?)null, tool_calls=new[] { new {
                    id="follow-wire", type="function", function=new {name="browser.follow_public_link",
                    arguments=JsonSerializer.Serialize(new {sourceUrl="https://www.example.com/",
                        linkIndex=0, expectedLinkUrl="https://www.example.com/about"})} }
                } } } } }),
            1 => JsonSerializer.Serialize(new { choices=new[] { new { message=new {
                role="assistant", content="Observed destination title: About Page." } } } }),
            _ => throw new InvalidOperationException("Unexpected wire link round")
        };
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content=new StringContent(response,System.Text.Encoding.UTF8,"application/json") };
    }
}

internal sealed class FakePublicBrowserSessions : IPublicBrowserSessions
{
    public string Id { get; } = "aabbccddeeff00112233445566778899";
    public int OpenCalls { get; private set; }
    public int InspectCalls { get; private set; }
    public int PageControlInspectCalls { get; private set; }
    public int PageLinkInspectCalls { get; private set; }
    public int PageTextInspectCalls { get; private set; }
    public string PublicVisibleText { get; set; } = new string('X',10000) + "LATE ARTICLE PARAGRAPH";
    public bool LaterLinkChangedHref { get; set; }
    public bool LaterLinkSameNameDifferentHref { get; set; }
    public bool LaterLinkExactDuplicate { get; set; }
    public string? LastNamedNavigationUrl { get; private set; }
    public int FollowCalls { get; private set; }
    public int CloseCalls { get; private set; }
    public bool RejectClose { get; set; }
    public bool ThrowClose { get; set; }
    public int SuccessfulNavigations { get; private set; }
    public bool ChangedLink { get; set; }
    public bool ChangedNamedName { get; set; }
    public bool DuplicateNamedLink { get; set; }
    public bool NamedNavigationUncertain { get; set; }
    public bool DuplicateSelect { get; set; }
    public bool DuplicateSelectOption { get; set; }
    public bool MissingSelectOption { get; set; }
    public bool DisabledSelectOption { get; set; }
    public bool ChangedSelectName { get; set; }
    public bool SelectUncertain { get; set; }
    public int SelectCalls { get; private set; }
    public int SuccessfulSelections { get; private set; }
    public int SetCheckedCalls { get; private set; }
    public int SuccessfulCheckedChanges { get; private set; }
    public bool CheckboxChecked { get; private set; }
    public bool RadioChecked { get; private set; }
    public bool DuplicateCheckedControl { get; set; }
    public bool DuplicateRadioControl { get; set; }
    public bool AlternateCheckboxChecked { get; private set; }
    public bool AlternateRadioChecked { get; private set; }
    public bool ChangedCheckedId { get; set; }
    public bool DisableCheckedControl { get; set; }
    public bool ChangeCheckedName { get; set; }
    public bool CheckedUncertain { get; set; }

    public int NamedFollowCalls { get; private set; }
    public bool ChangedLabel { get; set; }
    public bool ChangedSecondLabel { get; set; }
    public BrowserInteractionSequence Actions { get; } = new();
    public bool NetworkBlocked { get; private set; }
    public bool PasswordField { get; set; }
    public bool DisabledField { get; set; }
    public bool ThrowUncertain { get; set; }
    public bool ChangedButtonText { get; set; }
    public bool ButtonSubmit { get; set; }
    public bool ButtonDisabled { get; set; }
    public bool ThrowClickUncertain { get; set; }
    public bool ButtonAriaAvailable { get; set; } = true;
    public bool ButtonChangesState { get; set; } = true;
    public bool ButtonAriaExpanded { get; private set; }
    public int ClickCalls { get; private set; }
    public int SuccessfulClicks { get; private set; }
    public int SemanticCalls { get; private set; }
    public int FillCalls { get; private set; }
    public int SuccessfulEntries { get; private set; }
    public bool Closed { get; private set; }
    public string Current { get; private set; } = "https://www.example.com/";

    private BrowserSnapshot Snapshot() => new(Current,Current,
        Current.EndsWith("/about",StringComparison.Ordinal) ? "About Page" : "Example",
        200,"Observed public text",false,
        Current.EndsWith("/about",StringComparison.Ordinal)
            ? Array.Empty<BrowserLink>()
            : DuplicateNamedLink
                ? new[] {
                    new BrowserLink("About","https://www.example.com/about","About"),
                    new BrowserLink("More","https://www.example.com/about","About")
                }
                : new[] { new BrowserLink("About",ChangedLink ? "https://www.example.com/changed" :
                    "https://www.example.com/about",ChangedNamedName ? "Something else" : "About") },
        new [] {
            new BrowserElement("input", "", ChangedLabel ? "Altered" : "Keyword",
                PasswordField ? "password" : "search", "", DisabledField, 2,
                AccessibleName: ChangedLabel ? "Altered" : "Keyword",SemanticRole:"searchbox"),
            new BrowserElement("button", "", "Show more",
                ButtonSubmit ? "submit" : "button",
                ChangedButtonText ? "Different" : "Expand details", ButtonDisabled, 3,
                ButtonAriaAvailable ? ButtonAriaExpanded : null,
                AccessibleName:"Show more",SemanticRole:"button"),
            new BrowserElement("input", "", ChangedSecondLabel ? "Changed" : "Second keyword",
                "text", "", false, 4,AccessibleName: ChangedSecondLabel ? "Changed" : "Second keyword",
                SemanticRole:"textbox"),
            new BrowserElement("select", "", "Theme", "", "",false,5,
                AccessibleName:ChangedSelectName ? "Other theme" : "Theme", SemanticRole:"combobox",
                OptionLabels:MissingSelectOption ? new[]{"Choose", "Green"} :
                    DuplicateSelectOption ? new[]{"Choose", "Blue", "Blue", "Green"} :
                    new[]{"Choose", "Blue", "Green"})
        }.Concat(DuplicateSelect ? new[]{new BrowserElement("select", "", "Theme", "", "",false,6,
            AccessibleName:"Theme", SemanticRole:"combobox",
            OptionLabels:new[]{"Choose", "Blue", "Green"})} : Array.Empty<BrowserElement>())
        .Concat(new [] {
            new BrowserElement("input", "", "Accept updates", "checkbox", "",
                DisableCheckedControl, 7,
                AccessibleName:ChangeCheckedName ? "Changed updates" : "Accept updates",
                SemanticRole:"checkbox", NativeChecked:CheckboxChecked,
                HtmlId:ChangedCheckedId ? "changedUpdates" : "acceptUpdates"),
            new BrowserElement("input", "", "Basic option", "radio", "", false, 8,
                AccessibleName:"Basic option", SemanticRole:"radio", NativeChecked:RadioChecked,
                HtmlId:"basicOption")
        }).Concat(DuplicateCheckedControl ? new[]{new BrowserElement("input", "", "Accept updates",
            "checkbox", "", false, 9, AccessibleName:"Accept updates",
            SemanticRole:"checkbox", NativeChecked:AlternateCheckboxChecked,
            HtmlId:"acceptUpdatesAlt")} : Array.Empty<BrowserElement>())
        .Concat(DuplicateRadioControl ? new[]{new BrowserElement("input", "", "Basic option",
            "radio", "", false, 10, AccessibleName:"Basic option", SemanticRole:"radio",
            NativeChecked:AlternateRadioChecked, HtmlId:"basicOptionAlt")} : Array.Empty<BrowserElement>())
        .ToArray());

    public BrowserSnapshot SnapshotForTests() => Snapshot();

    public Task<BrowserSessionObservation> OpenAsync(Uri url,CancellationToken ct)
    {
        OpenCalls++;
        if(Closed) throw new InvalidOperationException("Closed");
        if(url.AbsoluteUri!="https://www.example.com/") throw new InvalidOperationException("Invalid URL");
        return Task.FromResult(new BrowserSessionObservation(Id,Snapshot()));
    }
    public Task<BrowserSnapshot> InspectAsync(string id,CancellationToken ct)
    {
        InspectCalls++;
        if(Closed || id!=Id) throw new InvalidOperationException("Session missing");
        return Task.FromResult(Snapshot());
    }
    public Task<BrowserSessionControlPageObservation> InspectControlsAsync(string id,
        int pageNumber, CancellationToken ct)
    {
        PageControlInspectCalls++;
        _ = BrowserControlPagination.OffsetForPage(pageNumber);
        if(Closed || id!=Id) throw new InvalidOperationException("Session closed");
        // One late radio at global control index 37. Never leak field contents.
        BrowserElement[] controls=pageNumber==1 ? [new BrowserElement("input", "", "", "radio", "",
            false,37,AccessibleName:"Default radio",SemanticRole:"radio",
            NativeChecked:false,HtmlId:"radioDefault1")] : [];
        return Task.FromResult(new BrowserSessionControlPageObservation(id,Current,pageNumber,56,controls));
    }
    private BrowserLink LaterLink() => new("Developer guide",
        LaterLinkChangedHref ? "https://www.example.com/changed" : "https://www.example.com/guide",
        "Developer guide");
    public Task<BrowserSessionVisibleTextPageObservation> InspectVisibleTextAsync(
        string id, int pageNumber, CancellationToken ct)
    {
        _=BrowserVisibleTextPagination.OffsetForPage(pageNumber);
        if(Closed || id!=Id) throw new InvalidOperationException("Session closed");
        PageTextInspectCalls++;
        return Task.FromResult(BrowserVisibleTextPagination.Slice(id,Current,
            "Example",PublicVisibleText,pageNumber));
    }

    public Task<BrowserSessionLinkPageObservation> InspectLinksAsync(string id,
        int pageNumber, CancellationToken ct)
    {
        PageLinkInspectCalls++;
        _ = BrowserLinkPagination.OffsetForPage(pageNumber);
        if (Closed || id != Id) throw new InvalidOperationException("Session closed");
        // Anchor 37 belongs to page 1; only link metadata, never form contents.
        BrowserLink[] links = pageNumber == 1 && Current == "https://www.example.com/"
            ? LaterLinkExactDuplicate ? [LaterLink(), LaterLink()]
            : LaterLinkSameNameDifferentHref ? [LaterLink(), new BrowserLink(
                "Developer guide", "https://www.example.com/another", "Developer guide")]
            : [LaterLink()] : [];
        return Task.FromResult(new BrowserSessionLinkPageObservation(id, Current,
            pageNumber, 52, links));
    }
    public Task<BrowserSessionLinkObservation> FollowAsync(string id,Uri source,int index,
        Uri expected,CancellationToken ct)
    {
        FollowCalls++;
        if(Closed || NetworkBlocked || id!=Id || source.AbsoluteUri!=Current || index!=0 ||
            expected.AbsoluteUri!="https://www.example.com/about" ||
            Snapshot().Links[index].Url!=expected.AbsoluteUri)
            throw new InvalidOperationException("Link changed or session unavailable");
        Current=expected.AbsoluteUri;
        SuccessfulNavigations++;
        return Task.FromResult(new BrowserSessionLinkObservation(Id,source.AbsoluteUri,"About",
            expected.AbsoluteUri,Snapshot()));
    }
    public Task<BrowserSessionLinkObservation> FollowByNameAsync(string id,Uri source,
        string name,Uri expected,CancellationToken ct, int? inspectedLinkPageNumber = null,
        bool preferCanonicalTrailingSlash = false)
    {
        NamedFollowCalls++;
        if(Closed || NetworkBlocked || id!=Id || source.AbsoluteUri!=Current ||
            source.Host != expected.Host)
            throw new InvalidOperationException("Named link is unavailable, or navigation is blocked");
        if (inspectedLinkPageNumber.HasValue)
            _ = BrowserLinkPagination.OffsetForPage(inspectedLinkPageNumber.Value);
        IReadOnlyList<BrowserLink> choices = inspectedLinkPageNumber.HasValue
            ? (inspectedLinkPageNumber.Value == 1 && Current == "https://www.example.com/"
                ? LaterLinkExactDuplicate ? [LaterLink(), LaterLink()]
                : LaterLinkSameNameDifferentHref ? [LaterLink(), new BrowserLink(
                    "Developer guide", "https://www.example.com/another", "Developer guide")]
                : [LaterLink()] : [])
            : Snapshot().Links;
        BrowserLink selected=inspectedLinkPageNumber.HasValue
            ? BrowserSemanticLink.RequireExactInPage(choices,name,expected)
            : BrowserSemanticLink.RequireUnique(choices,name,expected);
        Uri navigation = BrowserNamedLinkDestination.Resolve(expected, preferCanonicalTrailingSlash);
        if(NamedNavigationUncertain)
            throw new BrowserInteractionUncertainException("Navigation might have occurred",
                new InvalidOperationException("Synthetic uncertain browser navigation"));
        Current=navigation.AbsoluteUri;
        LastNamedNavigationUrl=Current;
        SuccessfulNavigations++;
        return Task.FromResult(new BrowserSessionLinkObservation(id,source.AbsoluteUri,
            selected.AccessibleName,expected.AbsoluteUri,Snapshot(),navigation.AbsoluteUri));
    }
    public Task<BrowserSessionTextObservation> FillTextAsync(string id,Uri page,int index,
        string tag,string label,string text,CancellationToken ct)
    {
        FillCalls++;
        if (Closed || id != Id || page.AbsoluteUri != Current || tag != "input" ||
            (index==2 && (label!="Keyword" || ChangedLabel || PasswordField || DisabledField)) ||
            (index==4 && (label!="Second keyword" || ChangedSecondLabel)) ||
            (index!=2 && index!=4))
            throw new InvalidOperationException("Field absent, changed or session closed");
        Actions.EnsureReady();
        Actions.Begin(); NetworkBlocked=true;
        if (ThrowUncertain)
            throw new BrowserInteractionUncertainException("Uncertain browser write",
                new InvalidOperationException("Test uncertainty"));
        SuccessfulEntries++;
        Actions.Verify();
        return Task.FromResult(new BrowserSessionTextObservation(id,Current,index,tag,label,
            text.Length,Snapshot()));
    }
    public Task<BrowserSessionButtonObservation> ClickButtonAsync(string id,Uri page,int index,
        string label,string text,CancellationToken ct)
    {
        ClickCalls++;
        if (Closed || id != Id || page.AbsoluteUri != Current || index != 3 ||
            label != "Show more" || text != "Expand details" || ChangedButtonText ||
            ButtonSubmit || ButtonDisabled)
            throw new InvalidOperationException("Button absent, changed or closed");
        Actions.EnsureReady();
        Actions.Begin(); NetworkBlocked=true;
        if (ThrowClickUncertain)
            throw new BrowserInteractionUncertainException("Uncertain button click",
                new InvalidOperationException("Test uncertainty"));
        BrowserElement before = Snapshot().Elements!.First(e => e.DomIndex == index);
        SuccessfulClicks++;
        if (ButtonChangesState) ButtonAriaExpanded = !ButtonAriaExpanded;
        BrowserSnapshot after = Snapshot();
        Actions.Verify();
        return Task.FromResult(new BrowserSessionButtonObservation(
            id,Current,index,label,text,after,
            BrowserButtonStateEvidence.Compare(before,after)));
    }
    public async Task<BrowserSemanticActionObservation> InteractByRoleAsync(
        string id, Uri page, string action, string role, string name, string? text, CancellationToken ct)
    {
        SemanticCalls++;
        if(Closed || id!=Id || page.AbsoluteUri!=Current)
            throw new InvalidOperationException("Session closed or wrong page");
        BrowserElement selected = BrowserSemanticControl.RequireUnique(
            Snapshot().Elements!,action,role,name);
        if (action == "click")
        {
            BrowserSessionButtonObservation result = await ClickButtonAsync(
                id,page,selected.DomIndex,selected.Label,selected.Text,ct);
            return new BrowserSemanticActionObservation(id,Current,action,role,name,null,
                result.Page,result.AccessibleState);
        }
        BrowserSessionTextObservation fill = await FillTextAsync(
            id,page,selected.DomIndex,selected.Tag,selected.Label,text!,ct);
        return new BrowserSemanticActionObservation(id,Current,action,role,name,text!.Length,
            fill.Page);
    }
    public Task<BrowserSessionSelectObservation> SelectOptionByNameAsync(string id, Uri page,
        string accessibleName, string optionLabel, CancellationToken ct)
    {
        SelectCalls++;
        if (Closed || id != Id || page.AbsoluteUri != Current || DisabledSelectOption)
            throw new InvalidOperationException("Dropdown, session or option disabled");
        Actions.EnsureReady();
        _ = BrowserSemanticSelect.RequireUnique(Snapshot().Elements!,accessibleName,optionLabel);
        Actions.Begin(); NetworkBlocked=true;
        if (SelectUncertain)
            throw new BrowserInteractionUncertainException("The selection might have occurred",
                new InvalidOperationException("Synthetic uncertain selection"));
        SuccessfulSelections++;
        Actions.Verify();
        return Task.FromResult(new BrowserSessionSelectObservation(id,Current,accessibleName,
            optionLabel,true,Snapshot()));
    }
    public Task<BrowserSessionCheckedObservation> SetCheckedByNameAsync(string id, Uri page,
        string role, string accessibleName, bool isChecked, CancellationToken ct,
        string? expectedHtmlId = null)
    {
        SetCheckedCalls++;
        if (Closed || id != Id || page.AbsoluteUri != Current || (role == "radio" && !isChecked))
            throw new InvalidOperationException("Session invalid or radio uncheck forbidden");
        Actions.EnsureReady();
        BrowserElement selected = BrowserNativeCheckedControl.RequireUnique(
            Snapshot().Elements!, role, accessibleName, expectedHtmlId);
        bool before = selected.NativeChecked!.Value;
        if (before == isChecked)
            return Task.FromResult(new BrowserSessionCheckedObservation(id, Current,
                role, accessibleName, isChecked, before, before, false, Snapshot(),
                selected.HtmlId));
        Actions.Begin(); NetworkBlocked = true;
        if (CheckedUncertain)
            throw new BrowserInteractionUncertainException("Native checked effect uncertain",
                new InvalidOperationException("Synthetic uncertain checked state"));
        if (selected.HtmlId == "acceptUpdatesAlt") AlternateCheckboxChecked = isChecked;
        else if (selected.HtmlId == "acceptUpdates" || selected.HtmlId == "changedUpdates")
            CheckboxChecked = isChecked;
        else if (selected.HtmlId == "basicOptionAlt") AlternateRadioChecked = isChecked;
        else RadioChecked = isChecked;
        SuccessfulCheckedChanges++;
        Actions.Verify();
        return Task.FromResult(new BrowserSessionCheckedObservation(id, Current,
            role, accessibleName, isChecked, before, isChecked, true, Snapshot(),
            selected.HtmlId));
    }
    public Task<bool> CloseAsync(string id,CancellationToken ct)
    {
        CloseCalls++;
        if (ThrowClose) throw new BrowserInteractionUncertainException(
            "Synthetic uncertain browser close", new InvalidOperationException("Simulated"));
        if (RejectClose || Closed || id!=Id) return Task.FromResult(false);
        Closed=true;
        return Task.FromResult(true);
    }
}


internal sealed class M7cFakeApplicationBackend(ApplicationCandidate candidate) : IApplicationDesktopBackend
{
    public int LaunchCalls { get; private set; }
    public IReadOnlyList<ApplicationCandidate> Find(string executable, CancellationToken ct) => [candidate];
    public ToolReceipt Launch(string executablePath, string fingerprint, CancellationToken ct)
    {
        LaunchCalls++;
        if (executablePath != candidate.ExecutablePath || fingerprint != candidate.Fingerprint) throw new InvalidOperationException("Wrong candidate");
        return ToolReceipt.Ok("Fake process started; window unverified", new { processStarted = true, windowVerified = false }, changedState: true);
    }
}

internal sealed class CaptureTestBackend : IDesktopAutomationBackend, IDesktopWindowCaptureBackend
{
    internal static DesktopWindow Item { get; } = new("window-fixture-01", "Sample window", "fixture", 1234, true, false);
    public bool Stale { get; init; }
    public int CaptureCalls { get; private set; }
    public IReadOnlyList<DesktopWindow> ListWindows() => [Item];
    public DesktopWindowCapture Capture(string windowId, string title)
    {
        CaptureCalls++;
        if (Stale || windowId != Item.WindowId || title != Item.Title)
            throw new DesktopAutomationException("CAPTURE_STALE", "Window changed before capture.");
        return new DesktopWindowCapture(Item, FixturePng(), 3, 2);
    }
    internal static byte[] FixturePng() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAIAAAASFvFNAAAAHElEQVR4nGMU0bDZwqXh8+0Go4iGDQMDAwMDAwAyxAPz8ep/7QAAAABJRU5ErkJggg==");
    public DesktopInspection Inspect(string windowId) => new(Item, [], false);
    public DesktopAction Activate(string windowId) => throw new NotSupportedException();
    public DesktopAction Invoke(string windowId, string controlId) => throw new NotSupportedException();
    public DesktopAction SetText(string windowId, string controlId, string text) => throw new NotSupportedException();
    public DesktopAction TypeText(string windowId, string controlId, string text) => throw new NotSupportedException();
}
