/*
 * filename: SegaMemoryMaintenanceReport.cs
 */

namespace SegaAgent.Memory.LongTerm;


public sealed record SegaMemoryDatabaseIntegrityResult
{
    public bool IsHealthy
    {
        get;
        init;
    }


    public string QuickCheckResult
    {
        get;
        init;
    } =
        string.Empty;


    public int ForeignKeyViolationCount
    {
        get;
        init;
    }
}


public sealed record SegaMemoryAssociationRepairResult
{
    public int ActiveMemoryCount
    {
        get;
        init;
    }


    public int InactiveIndexRowsPruned
    {
        get;
        init;
    }


    public int ProfilesBackfilled
    {
        get;
        init;
    }


    public int MemoriesRelinked
    {
        get;
        init;
    }
}


public sealed record SegaMemoryMaintenanceReport
{
    public DateTimeOffset CompletedAt
    {
        get;
        init;
    }


    public SegaMemoryDatabaseIntegrityResult Integrity
    {
        get;
        init;
    } =
        new();


    public int ActiveBefore
    {
        get;
        init;
    }


    public int ActiveAfter
    {
        get;
        init;
    }


    public int CanonicalConflictGroups
    {
        get;
        init;
    }


    public int ExactDuplicateGroups
    {
        get;
        init;
    }


    public int ExactDuplicatesMerged
    {
        get;
        init;
    }


    public int TestArtifactsArchived
    {
        get;
        init;
    }


    public int SensitiveActiveMemories
    {
        get;
        init;
    }


    public SegaMemoryAssociationRepairResult AssociationRepair
    {
        get;
        init;
    } =
        new();
}
