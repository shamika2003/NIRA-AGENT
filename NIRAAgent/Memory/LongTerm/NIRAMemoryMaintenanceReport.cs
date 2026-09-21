/*
 * filename: NIRAMemoryMaintenanceReport.cs
 */

namespace NIRAAgent.Memory.LongTerm;


public sealed record NIRAMemoryDatabaseIntegrityResult
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


public sealed record NIRAMemoryAssociationRepairResult
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


public sealed record NIRAMemoryMaintenanceReport
{
    public DateTimeOffset CompletedAt
    {
        get;
        init;
    }


    public NIRAMemoryDatabaseIntegrityResult Integrity
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


    public NIRAMemoryAssociationRepairResult AssociationRepair
    {
        get;
        init;
    } =
        new();
}

