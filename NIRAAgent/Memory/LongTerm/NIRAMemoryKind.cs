/*
 * filename: NIRAMemoryKind.cs
 */

namespace NIRAAgent.Memory.LongTerm;


// =============================================================
// MEMORY KIND
//
// These describe what a durable memory fundamentally represents.
//
// Relationship/mood values do NOT belong here. Those remain
// authoritative inside NIRACharacterStateService.
// =============================================================

public enum NIRAMemoryKind
{
    UserFact,

    UserPreference,

    ProjectKnowledge,

    SharedExperience,

    ImportantEvent,

    NIRALearnedPreference
}


// =============================================================
// MEMORY STATUS
//
// Superseded/Archived are preserved instead of being deleted so
// NIRA can later maintain history without treating old facts as
// current truth.
// =============================================================

public enum NIRAMemoryStatus
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

public enum NIRAMemorySourceType
{
    Unknown,

    UserExplicit,

    NIRAInference,

    SharedExperience,

    SystemDerived,

    Imported
}

