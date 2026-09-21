/*
 * filename: NIRALearnedSkillStore.cs
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using NIRAAgent.Memory.LongTerm;

namespace NIRAAgent.Skills;

public sealed class NIRALearnedSkillStore
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile bool _initialized;

    public NIRALearnedSkillStore()
        : this(BuildDefaultDatabasePath())
    {
    }

    public NIRALearnedSkillStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath.Trim());
        string? directory = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Learned skill database path must include a directory.", nameof(databasePath));
        Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();

        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private static string BuildDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NIRAAgent",
            "skills",
            "NIRA-learned-skills.db");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using SqliteConnection connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS NIRA_learned_skills (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    scope INTEGER NOT NULL,
                    source_tool_id TEXT NOT NULL,
                    source_tool_version INTEGER NOT NULL,
                    definition_json TEXT NOT NULL,
                    source_execution_count INTEGER NOT NULL DEFAULT 0,
                    source_success_count INTEGER NOT NULL DEFAULT 0,
                    source_failure_count INTEGER NOT NULL DEFAULT 0,
                    last_evidence_summary TEXT NOT NULL DEFAULT '',
                    last_observed_at_utc INTEGER NULL,
                    created_at_utc INTEGER NOT NULL,
                    updated_at_utc INTEGER NOT NULL
                );
                DROP INDEX IF EXISTS ux_NIRA_learned_skills_active_name;
                CREATE UNIQUE INDEX ux_NIRA_learned_skills_active_name
                    ON NIRA_learned_skills(name COLLATE NOCASE)
                    WHERE status IN (0, 1);
                CREATE INDEX IF NOT EXISTS ix_NIRA_learned_skills_source_tool
                    ON NIRA_learned_skills(source_tool_id, status);

                CREATE TABLE IF NOT EXISTS NIRA_learned_skill_evidence_history (
                    evidence_id TEXT PRIMARY KEY,
                    skill_id TEXT NOT NULL,
                    source_tool_id TEXT NOT NULL,
                    source_tool_version INTEGER NOT NULL,
                    succeeded INTEGER NOT NULL,
                    summary TEXT NOT NULL DEFAULT '',
                    failure_reason TEXT NOT NULL DEFAULT '',
                    observed_at_utc INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_NIRA_learned_skill_evidence_skill
                    ON NIRA_learned_skill_evidence_history(skill_id, observed_at_utc DESC);

                CREATE TABLE IF NOT EXISTS NIRA_learned_skill_reuse_history (
                    reuse_id TEXT PRIMARY KEY,
                    skill_id TEXT NOT NULL,
                    source_tool_id TEXT NOT NULL,
                    source_tool_version INTEGER NOT NULL,
                    succeeded INTEGER NOT NULL,
                    summary TEXT NOT NULL DEFAULT '',
                    failure_reason TEXT NOT NULL DEFAULT '',
                    reused_at_utc INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_NIRA_learned_skill_reuse_skill
                    ON NIRA_learned_skill_reuse_history(skill_id, reused_at_utc DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<IReadOnlyList<NIRALearnedSkillRecord>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, version, status, scope, source_tool_id, source_tool_version,
                   definition_json, source_execution_count, source_success_count,
                   source_failure_count, last_evidence_summary, last_observed_at_utc,
                   created_at_utc, updated_at_utc
            FROM NIRA_learned_skills
            ORDER BY status ASC, updated_at_utc DESC, name COLLATE NOCASE ASC;
            """;
        List<NIRALearnedSkillRecord> result = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadRecord(reader));
        return result;
    }

    public async Task<NIRALearnedSkillRecord?> ReadByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return null;
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, version, status, scope, source_tool_id, source_tool_version,
                   definition_json, source_execution_count, source_success_count,
                   source_failure_count, last_evidence_summary, last_observed_at_utc,
                   created_at_utc, updated_at_utc
            FROM NIRA_learned_skills WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    public async Task<IReadOnlyList<NIRALearnedSkillRecord>> ReadBySourceToolIdAsync(
        Guid toolId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, version, status, scope, source_tool_id, source_tool_version,
                   definition_json, source_execution_count, source_success_count,
                   source_failure_count, last_evidence_summary, last_observed_at_utc,
                   created_at_utc, updated_at_utc
            FROM NIRA_learned_skills
            WHERE source_tool_id = $tool_id
            ORDER BY updated_at_utc DESC;
            """;
        command.Parameters.AddWithValue("$tool_id", toolId.ToString("D"));
        List<NIRALearnedSkillRecord> result = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadRecord(reader));
        return result;
    }

    public async Task<IReadOnlyList<NIRALearnedSkillEvidenceRecord>> ReadEvidenceHistoryAsync(
        Guid skillId,
        int maximumResults = 32,
        CancellationToken cancellationToken = default)
    {
        if (skillId == Guid.Empty)
            return Array.Empty<NIRALearnedSkillEvidenceRecord>();

        await InitializeAsync(cancellationToken);
        maximumResults = Math.Clamp(maximumResults, 1, 128);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT evidence_id, skill_id, source_tool_id, source_tool_version,
                   succeeded, summary, failure_reason, observed_at_utc
            FROM NIRA_learned_skill_evidence_history
            WHERE skill_id = $skill_id
            ORDER BY observed_at_utc DESC
            LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$skill_id", skillId.ToString("D"));
        command.Parameters.AddWithValue("$maximum", maximumResults);

        List<NIRALearnedSkillEvidenceRecord> result = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NIRALearnedSkillEvidenceRecord
            {
                EvidenceId = Guid.Parse(reader.GetString(0)),
                SkillId = Guid.Parse(reader.GetString(1)),
                SourceToolId = Guid.Parse(reader.GetString(2)),
                SourceToolVersion = reader.GetInt32(3),
                Succeeded = reader.GetInt32(4) != 0,
                Summary = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                FailureReason = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                ObservedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7))
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<NIRALearnedSkillReuseRecord>> ReadReuseHistoryAsync(
        Guid skillId,
        int maximumResults = 32,
        CancellationToken cancellationToken = default)
    {
        if (skillId == Guid.Empty)
            return Array.Empty<NIRALearnedSkillReuseRecord>();

        await InitializeAsync(cancellationToken);
        maximumResults = Math.Clamp(maximumResults, 1, 128);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT reuse_id, skill_id, source_tool_id, source_tool_version,
                   succeeded, summary, failure_reason, reused_at_utc
            FROM NIRA_learned_skill_reuse_history
            WHERE skill_id = $skill_id
            ORDER BY reused_at_utc DESC
            LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$skill_id", skillId.ToString("D"));
        command.Parameters.AddWithValue("$maximum", maximumResults);

        List<NIRALearnedSkillReuseRecord> result = new();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NIRALearnedSkillReuseRecord
            {
                ReuseId = Guid.Parse(reader.GetString(0)),
                SkillId = Guid.Parse(reader.GetString(1)),
                SourceToolId = Guid.Parse(reader.GetString(2)),
                SourceToolVersion = reader.GetInt32(3),
                Succeeded = reader.GetInt32(4) != 0,
                Summary = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                FailureReason = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                ReusedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7))
            });
        }
        return result;
    }

    public async Task RecordReuseAsync(
        NIRALearnedSkillReuseRecord reuse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reuse);
        if (reuse.SkillId == Guid.Empty)
            throw new InvalidOperationException("Learned skill reuse requires a skill ID.");
        if (reuse.SourceToolId == Guid.Empty)
            throw new InvalidOperationException("Learned skill reuse requires a source tool ID.");
        if (reuse.SourceToolVersion <= 0)
            throw new InvalidOperationException("Learned skill reuse requires a positive source tool version.");

        await InitializeAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
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
                    INSERT INTO NIRA_learned_skill_reuse_history (
                        reuse_id, skill_id, source_tool_id, source_tool_version,
                        succeeded, summary, failure_reason, reused_at_utc
                    ) VALUES (
                        $reuse_id, $skill_id, $source_tool_id, $source_tool_version,
                        $succeeded, $summary, $failure_reason, $reused_at_utc
                    );
                    """;
                command.Parameters.AddWithValue("$reuse_id",
                    (reuse.ReuseId == Guid.Empty ? Guid.NewGuid() : reuse.ReuseId).ToString("D"));
                command.Parameters.AddWithValue("$skill_id", reuse.SkillId.ToString("D"));
                command.Parameters.AddWithValue("$source_tool_id", reuse.SourceToolId.ToString("D"));
                command.Parameters.AddWithValue("$source_tool_version", reuse.SourceToolVersion);
                command.Parameters.AddWithValue("$succeeded", reuse.Succeeded ? 1 : 0);
                command.Parameters.AddWithValue("$summary", Sanitize(reuse.Summary, 2400));
                command.Parameters.AddWithValue("$failure_reason", Sanitize(reuse.FailureReason, 2400));
                command.Parameters.AddWithValue("$reused_at_utc",
                    (reuse.ReusedAtUtc == default ? DateTimeOffset.UtcNow : reuse.ReusedAtUtc).ToUnixTimeMilliseconds());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (SqliteCommand prune = connection.CreateCommand())
            {
                prune.Transaction = transaction;
                prune.CommandText =
                    """
                    DELETE FROM NIRA_learned_skill_reuse_history
                    WHERE skill_id = $skill_id
                      AND reuse_id NOT IN (
                          SELECT reuse_id
                          FROM NIRA_learned_skill_reuse_history
                          WHERE skill_id = $skill_id
                          ORDER BY reused_at_utc DESC
                          LIMIT 128
                      );
                    """;
                prune.Parameters.AddWithValue("$skill_id", reuse.SkillId.ToString("D"));
                await prune.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpsertAsync(
        NIRALearnedSkillRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        NIRALearnedSkillDefinition definition = record.Definition.Normalize();
        if (definition.Id == Guid.Empty) throw new InvalidOperationException("Learned skill ID is required.");
        if (definition.SourceToolId == Guid.Empty) throw new InvalidOperationException("Learned skill source tool ID is required.");

        string json = JsonSerializer.Serialize(definition, _jsonOptions);
        await InitializeAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO NIRA_learned_skills (
                    id, name, version, status, scope, source_tool_id, source_tool_version,
                    definition_json, source_execution_count, source_success_count,
                    source_failure_count, last_evidence_summary, last_observed_at_utc,
                    created_at_utc, updated_at_utc
                ) VALUES (
                    $id, $name, $version, $status, $scope, $source_tool_id, $source_tool_version,
                    $definition_json, $source_execution_count, $source_success_count,
                    $source_failure_count, $last_evidence_summary, $last_observed_at_utc,
                    $created_at_utc, $updated_at_utc
                )
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    version = excluded.version,
                    status = excluded.status,
                    scope = excluded.scope,
                    source_tool_id = excluded.source_tool_id,
                    source_tool_version = excluded.source_tool_version,
                    definition_json = excluded.definition_json,
                    source_execution_count = excluded.source_execution_count,
                    source_success_count = excluded.source_success_count,
                    source_failure_count = excluded.source_failure_count,
                    last_evidence_summary = excluded.last_evidence_summary,
                    last_observed_at_utc = excluded.last_observed_at_utc,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            BindRecord(command, record with { Definition = definition }, json);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RecordEvidenceAsync(
        NIRALearnedSkillRecord updated,
        int sourceToolVersion,
        bool succeeded,
        string summary,
        string failureReason,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, cancellationToken);
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            NIRALearnedSkillDefinition definition = updated.Definition.Normalize();
            string json = JsonSerializer.Serialize(definition, _jsonOptions);
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE NIRA_learned_skills
                    SET version = $version,
                        status = $status,
                        source_tool_version = $source_tool_version,
                        definition_json = $definition_json,
                        source_execution_count = $source_execution_count,
                        source_success_count = $source_success_count,
                        source_failure_count = $source_failure_count,
                        last_evidence_summary = $last_evidence_summary,
                        last_observed_at_utc = $last_observed_at_utc,
                        updated_at_utc = $updated_at_utc
                    WHERE id = $id;
                    """;
                command.Parameters.AddWithValue("$id", definition.Id.ToString("D"));
                command.Parameters.AddWithValue("$version", definition.Version);
                command.Parameters.AddWithValue("$status", (int)definition.Status);
                command.Parameters.AddWithValue("$source_tool_version", definition.SourceToolVersion);
                command.Parameters.AddWithValue("$definition_json", json);
                command.Parameters.AddWithValue("$source_execution_count", updated.SourceExecutionCount);
                command.Parameters.AddWithValue("$source_success_count", updated.SourceSuccessCount);
                command.Parameters.AddWithValue("$source_failure_count", updated.SourceFailureCount);
                command.Parameters.AddWithValue("$last_evidence_summary", Sanitize(summary, 2400));
                command.Parameters.AddWithValue("$last_observed_at_utc", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$updated_at_utc", definition.UpdatedAtUtc.ToUnixTimeMilliseconds());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (SqliteCommand history = connection.CreateCommand())
            {
                history.Transaction = transaction;
                history.CommandText =
                    """
                    INSERT INTO NIRA_learned_skill_evidence_history (
                        evidence_id, skill_id, source_tool_id, source_tool_version,
                        succeeded, summary, failure_reason, observed_at_utc
                    ) VALUES (
                        $evidence_id, $skill_id, $source_tool_id, $source_tool_version,
                        $succeeded, $summary, $failure_reason, $observed_at_utc
                    );
                    """;
                history.Parameters.AddWithValue("$evidence_id", Guid.NewGuid().ToString("D"));
                history.Parameters.AddWithValue("$skill_id", definition.Id.ToString("D"));
                history.Parameters.AddWithValue("$source_tool_id", definition.SourceToolId.ToString("D"));
                history.Parameters.AddWithValue("$source_tool_version", sourceToolVersion);
                history.Parameters.AddWithValue("$succeeded", succeeded ? 1 : 0);
                history.Parameters.AddWithValue("$summary", Sanitize(summary, 2400));
                history.Parameters.AddWithValue("$failure_reason", Sanitize(failureReason, 2400));
                history.Parameters.AddWithValue("$observed_at_utc", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                await history.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (SqliteCommand prune = connection.CreateCommand())
            {
                prune.Transaction = transaction;
                prune.CommandText =
                    """
                    DELETE FROM NIRA_learned_skill_evidence_history
                    WHERE skill_id = $skill_id
                      AND evidence_id NOT IN (
                          SELECT evidence_id
                          FROM NIRA_learned_skill_evidence_history
                          WHERE skill_id = $skill_id
                          ORDER BY observed_at_utc DESC
                          LIMIT 128
                      );
                    """;
                prune.Parameters.AddWithValue("$skill_id", definition.Id.ToString("D"));
                await prune.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private NIRALearnedSkillRecord ReadRecord(SqliteDataReader reader)
    {
        NIRALearnedSkillDefinition? definition =
            JsonSerializer.Deserialize<NIRALearnedSkillDefinition>(reader.GetString(7), _jsonOptions);
        if (definition == null) throw new InvalidOperationException("A persisted learned skill could not be read.");
        definition = definition with
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            Version = reader.GetInt32(2),
            Status = (NIRALearnedSkillStatus)reader.GetInt32(3),
            Scope = (NIRALearnedSkillScope)reader.GetInt32(4),
            SourceToolId = Guid.Parse(reader.GetString(5)),
            SourceToolVersion = reader.GetInt32(6),
            CreatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(13)),
            UpdatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(14))
        };
        return new NIRALearnedSkillRecord
        {
            Definition = definition.Normalize(),
            SourceExecutionCount = reader.GetInt32(8),
            SourceSuccessCount = reader.GetInt32(9),
            SourceFailureCount = reader.GetInt32(10),
            LastEvidenceSummary = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
            LastObservedAtUtc = reader.IsDBNull(12)
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(12))
        };
    }

    private static void BindRecord(SqliteCommand command, NIRALearnedSkillRecord record, string json)
    {
        NIRALearnedSkillDefinition definition = record.Definition;
        command.Parameters.AddWithValue("$id", definition.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", definition.Name);
        command.Parameters.AddWithValue("$version", definition.Version);
        command.Parameters.AddWithValue("$status", (int)definition.Status);
        command.Parameters.AddWithValue("$scope", (int)definition.Scope);
        command.Parameters.AddWithValue("$source_tool_id", definition.SourceToolId.ToString("D"));
        command.Parameters.AddWithValue("$source_tool_version", definition.SourceToolVersion);
        command.Parameters.AddWithValue("$definition_json", json);
        command.Parameters.AddWithValue("$source_execution_count", record.SourceExecutionCount);
        command.Parameters.AddWithValue("$source_success_count", record.SourceSuccessCount);
        command.Parameters.AddWithValue("$source_failure_count", record.SourceFailureCount);
        command.Parameters.AddWithValue("$last_evidence_summary", Sanitize(record.LastEvidenceSummary, 2400));
        command.Parameters.AddWithValue("$last_observed_at_utc",
            record.LastObservedAtUtc.HasValue ? record.LastObservedAtUtc.Value.ToUnixTimeMilliseconds() : DBNull.Value);
        command.Parameters.AddWithValue("$created_at_utc", definition.CreatedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$updated_at_utc", definition.UpdatedAtUtc.ToUnixTimeMilliseconds());
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private static async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Sanitize(string? value, int max)
    {
        string clean = value?.Trim() ?? string.Empty;
        if (clean.Length > max) clean = clean[..max];
        if (NIRASensitiveMemoryPolicy.ContainsSensitiveSecret(clean))
            return "Sensitive execution detail omitted from durable learned-skill history.";
        return clean;
    }
}

