/*
 * filename: SegaMemoryKind.cs
 */

namespace SegaAgent.Memory.LongTerm;


// =============================================================
// MEMORY KIND
//
// These describe what a durable memory fundamentally represents.
//
// Relationship/mood values do NOT belong here. Those remain
// authoritative inside SegaCharacterStateService.
// =============================================================

public enum SegaMemoryKind
{
    UserFact,

    UserPreference,

    ProjectKnowledge,

    SharedExperience,

    ImportantEvent,

    SegaLearnedPreference
}


// =============================================================
// MEMORY STATUS
//
// Superseded/Archived are preserved instead of being deleted so
// Sega can later maintain history without treating old facts as
// current truth.
// =============================================================

public enum SegaMemoryStatus
{
    Active,

    Superseded,

    Archived
}


// =============================================================
// MEMORY SOURCE TYPE
//
// Provenance matters because "the user explicitly told me this"
// and "I inferred this" are not the same level of evidence.
// =============================================================

public enum SegaMemorySourceType
{
    Unknown,

    UserExplicit,

    SegaInference,

    SharedExperience,

    SystemDerived,

    Imported
}
