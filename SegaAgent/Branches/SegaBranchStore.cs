/*
 * filename: SegaBranchStore.cs
 */

using System.Text.Json;

using Microsoft.Data.Sqlite;

using SegaAgent.Goals;

namespace SegaAgent.Branches;

public sealed class SegaBranchStore
{
    private const int SchemaVersion =
        1;


    private readonly SegaGoalStore
        _goalStore;


    private readonly SemaphoreSlim
        _initializationLock =
            new(
                1,
                1);


    private bool
        _initialized;


    private readonly string
        _databasePath;


    private readonly string
        _connectionString;


    public string DatabasePath =>
        _databasePath;


    public SegaBranchStore(
        SegaGoalStore goalStore)
    {
        _goalStore =
            goalStore
            ?? throw new ArgumentNullException(
                nameof(goalStore));


        _databasePath =
            _goalStore.DatabasePath;


        _connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _databasePath,

                Mode =
                    SqliteOpenMode.ReadWriteCreate,

                Cache =
                    SqliteCacheMode.Shared,

                Pooling =
                    true,

                DefaultTimeout =
                    5
            }
            .ToString();
    }


    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }


        await _initializationLock.WaitAsync(
            cancellationToken);


        try
        {
            if (_initialized)
            {
                return;
            }


            // The branch graph deliberately shares Sega's executive
            // database with persistent goals. Ensure the goal schema
            // exists before creating foreign keys into sega_goals.
            await _goalStore.InitializeAsync(
                cancellationToken);


            await using SqliteConnection connection =
                CreateConnection();


            await connection.OpenAsync(
                cancellationToken);


            await ConfigureConnectionAsync(
                connection,
                cancellationToken);


            await EnsureSchemaAsync(
                connection,
                cancellationToken);


            _initialized =
                true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }


    public async Task<IReadOnlyList<SegaBranchState>>
        ReadBranchesAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(
            cancellationToken);


        await using SqliteConnection connection =
            CreateConnection();


        await connection.OpenAsync(
            cancellationToken);


        await ConfigureConnectionAsync(
            connection,
            cancellationToken);


        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            SELECT
                id,
                goal_id,
                parent_branch_id,
                fingerprint,
                objective,
                status,
                join_policy,
                priority,
                completion_criteria_json,
                dependencies_json,
                waiting_for,
                blocker,
                failure_reason,
                result_summary,
                evidence_count,
                last_evidence_summary,
                last_reason,
                source_event_id,
                source_event_name,
                created_at_utc,
                updated_at_utc,
                resolved_at_utc
            FROM sega_branches
            ORDER BY resolved_at_utc IS NOT NULL ASC,
                     priority DESC,
                     updated_at_utc DESC;
            """;


        List<SegaBranchState> result =
            new();


        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);


        while (
            await reader.ReadAsync(
                cancellationToken))
        {
            result.Add(
                ReadBranch(
                    reader));
        }


        return result;
    }


    public async Task UpsertBranchAsync(
        SegaBranchState branch,
        SegaBranchEvidenceRecord? evidence,
        CancellationToken cancellationToken = default)
    {
        SegaBranchState normalized =
            branch.Normalize();


        await InitializeAsync(
            cancellationToken);


        await using SqliteConnection connection =
            CreateConnection();


        await connection.OpenAsync(
            cancellationToken);


        await ConfigureConnectionAsync(
            connection,
            cancellationToken);


        await using SqliteTransaction transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);


        await using (
            SqliteCommand command =
                connection.CreateCommand())
        {
            command.Transaction =
                transaction;


            command.CommandText =
                """
                INSERT INTO sega_branches (
                    id,
                    goal_id,
                    parent_branch_id,
                    fingerprint,
                    objective,
                    status,
                    join_policy,
                    priority,
                    completion_criteria_json,
                    dependencies_json,
                    waiting_for,
                    blocker,
                    failure_reason,
                    result_summary,
                    evidence_count,
                    last_evidence_summary,
                    last_reason,
                    source_event_id,
                    source_event_name,
                    created_at_utc,
                    updated_at_utc,
                    resolved_at_utc
                )
                VALUES (
                    $id,
                    $goal_id,
                    $parent_branch_id,
                    $fingerprint,
                    $objective,
                    $status,
                    $join_policy,
                    $priority,
                    $completion_criteria_json,
                    $dependencies_json,
                    $waiting_for,
                    $blocker,
                    $failure_reason,
                    $result_summary,
                    $evidence_count,
                    $last_evidence_summary,
                    $last_reason,
                    $source_event_id,
                    $source_event_name,
                    $created_at_utc,
                    $updated_at_utc,
                    $resolved_at_utc
                )
                ON CONFLICT(id) DO UPDATE SET
                    goal_id = excluded.goal_id,
                    parent_branch_id = excluded.parent_branch_id,
                    fingerprint = excluded.fingerprint,
                    objective = excluded.objective,
                    status = excluded.status,
                    join_policy = excluded.join_policy,
                    priority = excluded.priority,
                    completion_criteria_json = excluded.completion_criteria_json,
                    dependencies_json = excluded.dependencies_json,
                    waiting_for = excluded.waiting_for,
                    blocker = excluded.blocker,
                    failure_reason = excluded.failure_reason,
                    result_summary = excluded.result_summary,
                    evidence_count = excluded.evidence_count,
                    last_evidence_summary = excluded.last_evidence_summary,
                    last_reason = excluded.last_reason,
                    source_event_id = excluded.source_event_id,
                    source_event_name = excluded.source_event_name,
                    updated_at_utc = excluded.updated_at_utc,
                    resolved_at_utc = excluded.resolved_at_utc;
                """;


            AddBranchParameters(
                command,
                normalized);


            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }


        if (evidence !=
            null)
        {
            await InsertEvidenceAsync(
                connection,
                transaction,
                evidence,
                cancellationToken);
        }


        await transaction.CommitAsync(
            cancellationToken);
    }


    public async Task<bool> HasIncompleteRequiredRootBranchesAsync(
        Guid goalId,
        CancellationToken cancellationToken = default)
    {
        if (goalId ==
            Guid.Empty)
        {
            return false;
        }


        await InitializeAsync(
            cancellationToken);


        await using SqliteConnection connection =
            CreateConnection();


        await connection.OpenAsync(
            cancellationToken);


        await ConfigureConnectionAsync(
            connection,
            cancellationToken);


        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            SELECT 1
            FROM sega_branches
            WHERE goal_id = $goal_id
              AND parent_branch_id IS NULL
              AND join_policy = $required
              AND status <> $completed
            LIMIT 1;
            """;


        command.Parameters.AddWithValue(
            "$goal_id",
            goalId.ToString("D"));


        command.Parameters.AddWithValue(
            "$required",
            (int)SegaBranchJoinPolicy.Required);


        command.Parameters.AddWithValue(
            "$completed",
            (int)SegaBranchStatus.Completed);


        object? value =
            await command.ExecuteScalarAsync(
                cancellationToken);


        return value !=
            null;
    }


    private static async Task InsertEvidenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SegaBranchEvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.Transaction =
            transaction;


        command.CommandText =
            """
            INSERT INTO sega_branch_evidence (
                id,
                branch_id,
                goal_id,
                source_event_id,
                source_event_name,
                evidence_source,
                quote,
                summary,
                reason,
                recorded_at_utc
            )
            VALUES (
                $id,
                $branch_id,
                $goal_id,
                $source_event_id,
                $source_event_name,
                $evidence_source,
                $quote,
                $summary,
                $reason,
                $recorded_at_utc
            );
            """;


        command.Parameters.AddWithValue(
            "$id",
            evidence.Id.ToString("D"));


        command.Parameters.AddWithValue(
            "$branch_id",
            evidence.BranchId.ToString("D"));


        command.Parameters.AddWithValue(
            "$goal_id",
            evidence.GoalId.ToString("D"));


        command.Parameters.AddWithValue(
            "$source_event_id",
            evidence.SourceEventId.HasValue
                ? evidence.SourceEventId.Value.ToString("D")
                : DBNull.Value);


        command.Parameters.AddWithValue(
            "$source_event_name",
            (object?)evidence.SourceEventName
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$evidence_source",
            (int)evidence.Source);


        command.Parameters.AddWithValue(
            "$quote",
            evidence.Quote
            ?? string.Empty);


        command.Parameters.AddWithValue(
            "$summary",
            evidence.Summary
            ?? string.Empty);


        command.Parameters.AddWithValue(
            "$reason",
            evidence.Reason
            ?? string.Empty);


        command.Parameters.AddWithValue(
            "$recorded_at_utc",
            evidence.RecordedAt.ToUnixTimeMilliseconds());


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    private static void AddBranchParameters(
        SqliteCommand command,
        SegaBranchState branch)
    {
        command.Parameters.AddWithValue(
            "$id",
            branch.Id.ToString("D"));


        command.Parameters.AddWithValue(
            "$goal_id",
            branch.GoalId.ToString("D"));


        command.Parameters.AddWithValue(
            "$parent_branch_id",
            branch.ParentBranchId.HasValue
                ? branch.ParentBranchId.Value.ToString("D")
                : DBNull.Value);


        command.Parameters.AddWithValue(
            "$fingerprint",
            branch.Fingerprint);


        command.Parameters.AddWithValue(
            "$objective",
            branch.Objective);


        command.Parameters.AddWithValue(
            "$status",
            (int)branch.Status);


        command.Parameters.AddWithValue(
            "$join_policy",
            (int)branch.JoinPolicy);


        command.Parameters.AddWithValue(
            "$priority",
            branch.Priority);


        command.Parameters.AddWithValue(
            "$completion_criteria_json",
            JsonSerializer.Serialize(
                branch.CompletionCriteria));


        command.Parameters.AddWithValue(
            "$dependencies_json",
            JsonSerializer.Serialize(
                branch.DependsOnBranchIds
                    .Select(
                        id =>
                            id.ToString("D"))
                    .ToArray()));


        command.Parameters.AddWithValue(
            "$waiting_for",
            (object?)branch.WaitingFor
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$blocker",
            (object?)branch.Blocker
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$failure_reason",
            (object?)branch.FailureReason
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$result_summary",
            (object?)branch.ResultSummary
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$evidence_count",
            branch.EvidenceCount);


        command.Parameters.AddWithValue(
            "$last_evidence_summary",
            (object?)branch.LastEvidenceSummary
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$last_reason",
            (object?)branch.LastReason
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$source_event_id",
            branch.SourceEventId.HasValue
                ? branch.SourceEventId.Value.ToString("D")
                : DBNull.Value);


        command.Parameters.AddWithValue(
            "$source_event_name",
            (object?)branch.SourceEventName
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$created_at_utc",
            branch.CreatedAt.ToUnixTimeMilliseconds());


        command.Parameters.AddWithValue(
            "$updated_at_utc",
            branch.UpdatedAt.ToUnixTimeMilliseconds());


        command.Parameters.AddWithValue(
            "$resolved_at_utc",
            branch.ResolvedAt.HasValue
                ? branch.ResolvedAt.Value.ToUnixTimeMilliseconds()
                : DBNull.Value);
    }


    private static SegaBranchState ReadBranch(
        SqliteDataReader reader)
    {
        string criteriaJson =
            reader.GetString(
                8);


        string dependenciesJson =
            reader.GetString(
                9);


        string[] criteria =
            JsonSerializer.Deserialize<string[]>(
                criteriaJson)
            ?? Array.Empty<string>();


        string[] dependencyStrings =
            JsonSerializer.Deserialize<string[]>(
                dependenciesJson)
            ?? Array.Empty<string>();


        Guid[] dependencies =
            dependencyStrings
                .Select(
                    value =>
                        Guid.TryParse(
                            value,
                            out Guid id)
                            ? id
                            : Guid.Empty)
                .Where(
                    id =>
                        id != Guid.Empty)
                .ToArray();


        return new SegaBranchState
        {
            Id =
                Guid.Parse(
                    reader.GetString(
                        0)),

            GoalId =
                Guid.Parse(
                    reader.GetString(
                        1)),

            ParentBranchId =
                reader.IsDBNull(
                    2)
                    ? null
                    : Guid.Parse(
                        reader.GetString(
                            2)),

            Fingerprint =
                reader.GetString(
                    3),

            Objective =
                reader.GetString(
                    4),

            Status =
                (SegaBranchStatus)
                reader.GetInt32(
                    5),

            JoinPolicy =
                (SegaBranchJoinPolicy)
                reader.GetInt32(
                    6),

            Priority =
                reader.GetDouble(
                    7),

            CompletionCriteria =
                criteria,

            DependsOnBranchIds =
                dependencies,

            WaitingFor =
                reader.IsDBNull(
                    10)
                    ? null
                    : reader.GetString(
                        10),

            Blocker =
                reader.IsDBNull(
                    11)
                    ? null
                    : reader.GetString(
                        11),

            FailureReason =
                reader.IsDBNull(
                    12)
                    ? null
                    : reader.GetString(
                        12),

            ResultSummary =
                reader.IsDBNull(
                    13)
                    ? null
                    : reader.GetString(
                        13),

            EvidenceCount =
                reader.GetInt32(
                    14),

            LastEvidenceSummary =
                reader.IsDBNull(
                    15)
                    ? null
                    : reader.GetString(
                        15),

            LastReason =
                reader.IsDBNull(
                    16)
                    ? null
                    : reader.GetString(
                        16),

            SourceEventId =
                reader.IsDBNull(
                    17)
                    ? null
                    : Guid.Parse(
                        reader.GetString(
                            17)),

            SourceEventName =
                reader.IsDBNull(
                    18)
                    ? null
                    : reader.GetString(
                        18),

            CreatedAt =
                DateTimeOffset.FromUnixTimeMilliseconds(
                    reader.GetInt64(
                        19)),

            UpdatedAt =
                DateTimeOffset.FromUnixTimeMilliseconds(
                    reader.GetInt64(
                        20)),

            ResolvedAt =
                reader.IsDBNull(
                    21)
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(
                        reader.GetInt64(
                            21))
        }
        .Normalize();
    }


    private async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS sega_branch_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO sega_branch_meta (key, value)
            VALUES ('schema_version', '{SchemaVersion}');

            CREATE TABLE IF NOT EXISTS sega_branches (
                id TEXT PRIMARY KEY,
                goal_id TEXT NOT NULL,
                parent_branch_id TEXT NULL,
                fingerprint TEXT NOT NULL,
                objective TEXT NOT NULL,
                status INTEGER NOT NULL,
                join_policy INTEGER NOT NULL,
                priority REAL NOT NULL,
                completion_criteria_json TEXT NOT NULL,
                dependencies_json TEXT NOT NULL,
                waiting_for TEXT NULL,
                blocker TEXT NULL,
                failure_reason TEXT NULL,
                result_summary TEXT NULL,
                evidence_count INTEGER NOT NULL,
                last_evidence_summary TEXT NULL,
                last_reason TEXT NULL,
                source_event_id TEXT NULL,
                source_event_name TEXT NULL,
                created_at_utc INTEGER NOT NULL,
                updated_at_utc INTEGER NOT NULL,
                resolved_at_utc INTEGER NULL,
                FOREIGN KEY(goal_id) REFERENCES sega_goals(id),
                FOREIGN KEY(parent_branch_id) REFERENCES sega_branches(id)
            );

            CREATE TABLE IF NOT EXISTS sega_branch_evidence (
                id TEXT PRIMARY KEY,
                branch_id TEXT NOT NULL,
                goal_id TEXT NOT NULL,
                source_event_id TEXT NULL,
                source_event_name TEXT NULL,
                evidence_source INTEGER NOT NULL,
                quote TEXT NOT NULL,
                summary TEXT NOT NULL,
                reason TEXT NOT NULL,
                recorded_at_utc INTEGER NOT NULL,
                FOREIGN KEY(branch_id) REFERENCES sega_branches(id),
                FOREIGN KEY(goal_id) REFERENCES sega_goals(id)
            );

            CREATE INDEX IF NOT EXISTS idx_sega_branches_goal
                ON sega_branches(goal_id, status, priority DESC);

            CREATE INDEX IF NOT EXISTS idx_sega_branches_parent
                ON sega_branches(parent_branch_id, status);

            CREATE INDEX IF NOT EXISTS idx_sega_branches_status
                ON sega_branches(status);

            CREATE INDEX IF NOT EXISTS idx_sega_branches_join
                ON sega_branches(goal_id, parent_branch_id, join_policy, status);

            CREATE INDEX IF NOT EXISTS idx_sega_branch_evidence_branch
                ON sega_branch_evidence(branch_id, recorded_at_utc DESC);
            """;


        await command.ExecuteNonQueryAsync(
            cancellationToken);


        await using SqliteCommand versionCommand =
            connection.CreateCommand();


        versionCommand.CommandText =
            "SELECT value FROM sega_branch_meta WHERE key = 'schema_version';";


        object? value =
            await versionCommand.ExecuteScalarAsync(
                cancellationToken);


        if (!int.TryParse(
                Convert.ToString(
                    value),
                out int version)
            ||
            version !=
                SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported Sega branch database schema. " +
                $"Expected {SchemaVersion}, found '{value}'.");
        }
    }


    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(
            _connectionString);
    }


    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            """;


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }
}
