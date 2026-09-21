/*
 * filename: NIRAGoalStore.cs
 */

using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NIRAAgent.Goals;

public sealed class NIRAGoalStore
{
    private const int SchemaVersion = 1;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    private readonly string _databasePath;
    private readonly string _connectionString;

    public string DatabasePath => _databasePath;

    public NIRAGoalStore()
        : this(BuildDefaultDatabasePath())
    {
    }

    public NIRAGoalStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath.Trim());

        string? directory = Path.GetDirectoryName(_databasePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "NIRA goal database path must include a directory.",
                nameof(databasePath));
        }

        Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();
    }

    private static string BuildDefaultDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "NIRAAgent",
            "executive",
            "NIRA-goals.db");
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
            {
                return;
            }

            await using SqliteConnection connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<IReadOnlyList<NIRAGoalState>> ReadGoalsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                id,
                fingerprint,
                objective,
                status,
                source,
                priority,
                linked_commitment_id,
                completion_criteria_json,
                dependencies_json,
                waiting_for,
                blocker,
                next_wake_at_utc,
                evidence_count,
                last_evidence_summary,
                last_reason,
                source_event_id,
                source_event_name,
                created_at_utc,
                updated_at_utc,
                resolved_at_utc
            FROM NIRA_goals
            ORDER BY resolved_at_utc IS NOT NULL ASC,
                     priority DESC,
                     updated_at_utc DESC;
            """;

        List<NIRAGoalState> result = new();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadGoal(reader));
        }

        return result;
    }

    public async Task UpsertGoalAsync(
        NIRAGoalState goal,
        NIRAGoalEvidenceRecord? evidence,
        CancellationToken cancellationToken = default)
    {
        NIRAGoalState normalized = goal.Normalize();

        await InitializeAsync(cancellationToken);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);

        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;

            command.CommandText =
                """
                INSERT INTO NIRA_goals (
                    id,
                    fingerprint,
                    objective,
                    status,
                    source,
                    priority,
                    linked_commitment_id,
                    completion_criteria_json,
                    dependencies_json,
                    waiting_for,
                    blocker,
                    next_wake_at_utc,
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
                    $fingerprint,
                    $objective,
                    $status,
                    $source,
                    $priority,
                    $linked_commitment_id,
                    $completion_criteria_json,
                    $dependencies_json,
                    $waiting_for,
                    $blocker,
                    $next_wake_at_utc,
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
                    fingerprint = excluded.fingerprint,
                    objective = excluded.objective,
                    status = excluded.status,
                    source = excluded.source,
                    priority = excluded.priority,
                    linked_commitment_id = excluded.linked_commitment_id,
                    completion_criteria_json = excluded.completion_criteria_json,
                    dependencies_json = excluded.dependencies_json,
                    waiting_for = excluded.waiting_for,
                    blocker = excluded.blocker,
                    next_wake_at_utc = excluded.next_wake_at_utc,
                    evidence_count = excluded.evidence_count,
                    last_evidence_summary = excluded.last_evidence_summary,
                    last_reason = excluded.last_reason,
                    source_event_id = excluded.source_event_id,
                    source_event_name = excluded.source_event_name,
                    updated_at_utc = excluded.updated_at_utc,
                    resolved_at_utc = excluded.resolved_at_utc;
                """;

            AddGoalParameters(command, normalized);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (evidence != null)
        {
            await InsertEvidenceAsync(
                connection,
                transaction,
                evidence,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertEvidenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NIRAGoalEvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText =
            """
            INSERT INTO NIRA_goal_evidence (
                id,
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

        command.Parameters.AddWithValue("$id", evidence.Id.ToString("D"));
        command.Parameters.AddWithValue("$goal_id", evidence.GoalId.ToString("D"));
        command.Parameters.AddWithValue(
            "$source_event_id",
            evidence.SourceEventId.HasValue
                ? evidence.SourceEventId.Value.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$source_event_name",
            (object?)evidence.SourceEventName ?? DBNull.Value);
        command.Parameters.AddWithValue("$evidence_source", (int)evidence.Source);
        command.Parameters.AddWithValue("$quote", evidence.Quote ?? string.Empty);
        command.Parameters.AddWithValue("$summary", evidence.Summary ?? string.Empty);
        command.Parameters.AddWithValue("$reason", evidence.Reason ?? string.Empty);
        command.Parameters.AddWithValue(
            "$recorded_at_utc",
            evidence.RecordedAt.ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddGoalParameters(
        SqliteCommand command,
        NIRAGoalState goal)
    {
        command.Parameters.AddWithValue("$id", goal.Id.ToString("D"));
        command.Parameters.AddWithValue("$fingerprint", goal.Fingerprint);
        command.Parameters.AddWithValue("$objective", goal.Objective);
        command.Parameters.AddWithValue("$status", (int)goal.Status);
        command.Parameters.AddWithValue("$source", (int)goal.Source);
        command.Parameters.AddWithValue("$priority", goal.Priority);
        command.Parameters.AddWithValue(
            "$linked_commitment_id",
            goal.LinkedCommitmentId.HasValue
                ? goal.LinkedCommitmentId.Value.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$completion_criteria_json",
            JsonSerializer.Serialize(goal.CompletionCriteria));
        command.Parameters.AddWithValue(
            "$dependencies_json",
            JsonSerializer.Serialize(
                goal.DependsOnGoalIds.Select(id => id.ToString("D")).ToArray()));
        command.Parameters.AddWithValue(
            "$waiting_for",
            (object?)goal.WaitingFor ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$blocker",
            (object?)goal.Blocker ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$next_wake_at_utc",
            goal.NextWakeAtUtc.HasValue
                ? goal.NextWakeAtUtc.Value.ToUnixTimeMilliseconds()
                : DBNull.Value);
        command.Parameters.AddWithValue("$evidence_count", goal.EvidenceCount);
        command.Parameters.AddWithValue(
            "$last_evidence_summary",
            (object?)goal.LastEvidenceSummary ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$last_reason",
            (object?)goal.LastReason ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$source_event_id",
            goal.SourceEventId.HasValue
                ? goal.SourceEventId.Value.ToString("D")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$source_event_name",
            (object?)goal.SourceEventName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$created_at_utc",
            goal.CreatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue(
            "$updated_at_utc",
            goal.UpdatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue(
            "$resolved_at_utc",
            goal.ResolvedAt.HasValue
                ? goal.ResolvedAt.Value.ToUnixTimeMilliseconds()
                : DBNull.Value);
    }

    private static NIRAGoalState ReadGoal(SqliteDataReader reader)
    {
        string criteriaJson = reader.GetString(7);
        string dependenciesJson = reader.GetString(8);

        string[] criteria =
            JsonSerializer.Deserialize<string[]>(criteriaJson)
            ?? Array.Empty<string>();

        string[] dependencyStrings =
            JsonSerializer.Deserialize<string[]>(dependenciesJson)
            ?? Array.Empty<string>();

        Guid[] dependencies = dependencyStrings
            .Select(value => Guid.TryParse(value, out Guid id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray();

        return new NIRAGoalState
        {
            Id = Guid.Parse(reader.GetString(0)),
            Fingerprint = reader.GetString(1),
            Objective = reader.GetString(2),
            Status = (NIRAGoalStatus)reader.GetInt32(3),
            Source = (NIRAGoalSource)reader.GetInt32(4),
            Priority = reader.GetDouble(5),
            LinkedCommitmentId = reader.IsDBNull(6)
                ? null
                : Guid.Parse(reader.GetString(6)),
            CompletionCriteria = criteria,
            DependsOnGoalIds = dependencies,
            WaitingFor = reader.IsDBNull(9) ? null : reader.GetString(9),
            Blocker = reader.IsDBNull(10) ? null : reader.GetString(10),
            NextWakeAtUtc = reader.IsDBNull(11)
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(11)),
            EvidenceCount = reader.GetInt32(12),
            LastEvidenceSummary = reader.IsDBNull(13) ? null : reader.GetString(13),
            LastReason = reader.IsDBNull(14) ? null : reader.GetString(14),
            SourceEventId = reader.IsDBNull(15)
                ? null
                : Guid.Parse(reader.GetString(15)),
            SourceEventName = reader.IsDBNull(16) ? null : reader.GetString(16),
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(17)),
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(18)),
            ResolvedAt = reader.IsDBNull(19)
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(19))
        }.Normalize();
    }

    private async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS NIRA_goal_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO NIRA_goal_meta (key, value)
            VALUES ('schema_version', '{SchemaVersion}');

            CREATE TABLE IF NOT EXISTS NIRA_goals (
                id TEXT PRIMARY KEY,
                fingerprint TEXT NOT NULL,
                objective TEXT NOT NULL,
                status INTEGER NOT NULL,
                source INTEGER NOT NULL,
                priority REAL NOT NULL,
                linked_commitment_id TEXT NULL,
                completion_criteria_json TEXT NOT NULL,
                dependencies_json TEXT NOT NULL,
                waiting_for TEXT NULL,
                blocker TEXT NULL,
                next_wake_at_utc INTEGER NULL,
                evidence_count INTEGER NOT NULL,
                last_evidence_summary TEXT NULL,
                last_reason TEXT NULL,
                source_event_id TEXT NULL,
                source_event_name TEXT NULL,
                created_at_utc INTEGER NOT NULL,
                updated_at_utc INTEGER NOT NULL,
                resolved_at_utc INTEGER NULL
            );

            CREATE TABLE IF NOT EXISTS NIRA_goal_evidence (
                id TEXT PRIMARY KEY,
                goal_id TEXT NOT NULL,
                source_event_id TEXT NULL,
                source_event_name TEXT NULL,
                evidence_source INTEGER NOT NULL,
                quote TEXT NOT NULL,
                summary TEXT NOT NULL,
                reason TEXT NOT NULL,
                recorded_at_utc INTEGER NOT NULL,
                FOREIGN KEY(goal_id) REFERENCES NIRA_goals(id)
            );

            CREATE INDEX IF NOT EXISTS idx_NIRA_goals_status
                ON NIRA_goals(status);

            CREATE INDEX IF NOT EXISTS idx_NIRA_goals_priority
                ON NIRA_goals(priority DESC);

            CREATE INDEX IF NOT EXISTS idx_NIRA_goals_next_wake
                ON NIRA_goals(next_wake_at_utc);

            CREATE INDEX IF NOT EXISTS idx_NIRA_goal_evidence_goal
                ON NIRA_goal_evidence(goal_id, recorded_at_utc DESC);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText =
            "SELECT value FROM NIRA_goal_meta WHERE key = 'schema_version';";

        object? value = await versionCommand.ExecuteScalarAsync(cancellationToken);

        if (!int.TryParse(Convert.ToString(value), out int version) ||
            version != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported NIRA goal database schema. Expected {SchemaVersion}, found '{value}'.");
        }
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

