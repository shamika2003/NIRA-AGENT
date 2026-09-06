/*
 * filename: SegaDynamicToolStore.cs
 */

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Data.Sqlite;

using SegaAgent.Memory.LongTerm;

namespace SegaAgent.Tools;

public sealed class SegaDynamicToolStore
{
    private const int SchemaVersion = 2;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _initialized;

    public string DatabasePath => _databasePath;

    public SegaDynamicToolStore()
        : this(BuildDefaultDatabasePath())
    {
    }

    public SegaDynamicToolStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath =
            Path.GetFullPath(
                databasePath.Trim());

        string? directory =
            Path.GetDirectoryName(
                _databasePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "Dynamic tool database path must include a directory.",
                nameof(databasePath));
        }

        Directory.CreateDirectory(
            directory);

        _connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true,
                DefaultTimeout = 5
            }
            .ToString();

        _jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

        _jsonOptions.Converters.Add(
            new JsonStringEnumConverter());
    }

    private static string BuildDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "SegaAgent",
            "tools",
            "sega-dynamic-tools.db");

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

    public async Task<IReadOnlyList<SegaDynamicToolRecord>> ReadAllAsync(
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
            SELECT id, name, version, status, persistence, definition_json,
                   execution_count, success_count, failure_count, last_result,
                   last_executed_at_utc, created_at_utc, updated_at_utc
            FROM sega_dynamic_tools
            ORDER BY status ASC, updated_at_utc DESC, name COLLATE NOCASE ASC;
            """;

        List<SegaDynamicToolRecord> result =
            new();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                ReadRecord(
                    reader));
        }

        return result;
    }

    public async Task<SegaDynamicToolRecord?> ReadByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return null;
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
            SELECT id, name, version, status, persistence, definition_json,
                   execution_count, success_count, failure_count, last_result,
                   last_executed_at_utc, created_at_utc, updated_at_utc
            FROM sega_dynamic_tools
            WHERE id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            id.ToString("D"));

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        return await reader.ReadAsync(
            cancellationToken)
                ? ReadRecord(reader)
                : null;
    }

    public async Task<IReadOnlyList<SegaDynamicToolExecutionHistoryRecord>> ReadRecentExecutionHistoryAsync(
        int maximumResults = 64,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(
            cancellationToken);

        maximumResults =
            Math.Clamp(
                maximumResults,
                1,
                256);

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
            SELECT execution_id, tool_id, tool_version, succeeded, aborted,
                   summary, failure_reason, started_at_utc, finished_at_utc,
                   recorded_at_utc
            FROM sega_dynamic_tool_execution_history
            ORDER BY recorded_at_utc DESC
            LIMIT $maximum;
            """;

        command.Parameters.AddWithValue(
            "$maximum",
            maximumResults);

        List<SegaDynamicToolExecutionHistoryRecord> result =
            new();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                new SegaDynamicToolExecutionHistoryRecord
                {
                    ExecutionId = Guid.Parse(reader.GetString(0)),
                    ToolId = Guid.Parse(reader.GetString(1)),
                    ToolVersion = reader.GetInt32(2),
                    Succeeded = reader.GetInt32(3) != 0,
                    Aborted = reader.GetInt32(4) != 0,
                    Summary = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    FailureReason = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    StartedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
                    FinishedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8)),
                    RecordedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(9))
                });
        }

        return result;
    }

    public async Task UpsertAsync(
        SegaDynamicToolRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            record);

        SegaDynamicToolDefinition definition =
            record.Definition.Normalize();

        if (definition.Id == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Cannot persist a dynamic tool without an ID.");
        }

        if (definition.Persistence !=
            SegaDynamicToolPersistence.Persistent)
        {
            throw new InvalidOperationException(
                "Only persistent dynamic tools belong in the durable tool store.");
        }

        string definitionJson =
            JsonSerializer.Serialize(
                definition,
                _jsonOptions);

        await InitializeAsync(
            cancellationToken);

        await _writeLock.WaitAsync(
            cancellationToken);

        try
        {
            await using SqliteConnection connection =
                CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await ConfigureConnectionAsync(
                connection,
                cancellationToken);

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(
                    cancellationToken);

            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction =
                    transaction;

                command.CommandText =
                    """
                    INSERT INTO sega_dynamic_tools (
                        id, name, version, status, persistence, definition_json,
                        execution_count, success_count, failure_count, last_result,
                        last_executed_at_utc, created_at_utc, updated_at_utc
                    ) VALUES (
                        $id, $name, $version, $status, $persistence, $definition_json,
                        $execution_count, $success_count, $failure_count, $last_result,
                        $last_executed_at_utc, $created_at_utc, $updated_at_utc
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        name = excluded.name,
                        version = excluded.version,
                        status = excluded.status,
                        persistence = excluded.persistence,
                        definition_json = excluded.definition_json,
                        execution_count = excluded.execution_count,
                        success_count = excluded.success_count,
                        failure_count = excluded.failure_count,
                        last_result = excluded.last_result,
                        last_executed_at_utc = excluded.last_executed_at_utc,
                        updated_at_utc = excluded.updated_at_utc;
                    """;

                AddRecordParameters(
                    command,
                    record,
                    definitionJson);

                await command.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await using (SqliteCommand versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction =
                    transaction;

                versionCommand.CommandText =
                    """
                    INSERT OR IGNORE INTO sega_dynamic_tool_versions (
                        tool_id, version, definition_json, recorded_at_utc
                    ) VALUES ($tool_id, $version, $definition_json, $recorded_at_utc);
                    """;

                versionCommand.Parameters.AddWithValue(
                    "$tool_id",
                    definition.Id.ToString("D"));

                versionCommand.Parameters.AddWithValue(
                    "$version",
                    definition.Version);

                versionCommand.Parameters.AddWithValue(
                    "$definition_json",
                    definitionJson);

                versionCommand.Parameters.AddWithValue(
                    "$recorded_at_utc",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                await versionCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SetStatusAsync(
        Guid id,
        SegaDynamicToolStatus status,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(
            cancellationToken);

        await _writeLock.WaitAsync(
            cancellationToken);

        try
        {
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
                "UPDATE sega_dynamic_tools " +
                "SET status = $status, updated_at_utc = $updated " +
                "WHERE id = $id;";

            command.Parameters.AddWithValue(
                "$status",
                (int)status);

            command.Parameters.AddWithValue(
                "$updated",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            command.Parameters.AddWithValue(
                "$id",
                id.ToString("D"));

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RecordExecutionAsync(
        Guid id,
        SegaDynamicToolExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            result);

        await InitializeAsync(
            cancellationToken);

        await _writeLock.WaitAsync(
            cancellationToken);

        try
        {
            await using SqliteConnection connection =
                CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await ConfigureConnectionAsync(
                connection,
                cancellationToken);

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(
                    cancellationToken);

            DateTimeOffset recordedAt =
                DateTimeOffset.UtcNow;

            string summary =
                SanitizeHistoryText(
                    result.Summary,
                    2400);

            string failureReason =
                result.Succeeded
                    ? string.Empty
                    : SanitizeHistoryText(
                        result.FailureReason,
                        2400);

            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction =
                    transaction;

                command.CommandText =
                    """
                    UPDATE sega_dynamic_tools
                    SET execution_count = execution_count + 1,
                        success_count = success_count + $success,
                        failure_count = failure_count + $failure,
                        last_result = $result,
                        last_executed_at_utc = $executed,
                        updated_at_utc = $executed
                    WHERE id = $id;
                    """;

                command.Parameters.AddWithValue(
                    "$success",
                    result.Succeeded ? 1 : 0);

                command.Parameters.AddWithValue(
                    "$failure",
                    result.Succeeded ? 0 : 1);

                command.Parameters.AddWithValue(
                    "$result",
                    summary);

                command.Parameters.AddWithValue(
                    "$executed",
                    recordedAt.ToUnixTimeMilliseconds());

                command.Parameters.AddWithValue(
                    "$id",
                    id.ToString("D"));

                int changed =
                    await command.ExecuteNonQueryAsync(
                        cancellationToken);

                if (changed != 1)
                {
                    throw new InvalidOperationException(
                        "The persisted dynamic tool disappeared before its execution history could be recorded.");
                }
            }

            await using (SqliteCommand historyCommand = connection.CreateCommand())
            {
                historyCommand.Transaction =
                    transaction;

                historyCommand.CommandText =
                    """
                    INSERT INTO sega_dynamic_tool_execution_history (
                        execution_id, tool_id, tool_version, succeeded, aborted,
                        summary, failure_reason, evidence_json,
                        started_at_utc, finished_at_utc, recorded_at_utc
                    ) VALUES (
                        $execution_id, $tool_id, $tool_version, $succeeded, $aborted,
                        $summary, $failure_reason, $evidence_json,
                        $started_at_utc, $finished_at_utc, $recorded_at_utc
                    );
                    """;

                historyCommand.Parameters.AddWithValue(
                    "$execution_id",
                    Guid.NewGuid().ToString("D"));

                historyCommand.Parameters.AddWithValue(
                    "$tool_id",
                    id.ToString("D"));

                historyCommand.Parameters.AddWithValue(
                    "$tool_version",
                    result.ToolVersion);

                historyCommand.Parameters.AddWithValue(
                    "$succeeded",
                    result.Succeeded ? 1 : 0);

                historyCommand.Parameters.AddWithValue(
                    "$aborted",
                    result.Aborted ? 1 : 0);

                historyCommand.Parameters.AddWithValue(
                    "$summary",
                    summary);

                historyCommand.Parameters.AddWithValue(
                    "$failure_reason",
                    failureReason);

                historyCommand.Parameters.AddWithValue(
                    "$evidence_json",
                    BuildHistoryEvidenceJson(
                        result));

                historyCommand.Parameters.AddWithValue(
                    "$started_at_utc",
                    result.StartedAtUtc.ToUnixTimeMilliseconds());

                historyCommand.Parameters.AddWithValue(
                    "$finished_at_utc",
                    result.FinishedAtUtc.ToUnixTimeMilliseconds());

                historyCommand.Parameters.AddWithValue(
                    "$recorded_at_utc",
                    recordedAt.ToUnixTimeMilliseconds());

                await historyCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await using (SqliteCommand pruneCommand = connection.CreateCommand())
            {
                pruneCommand.Transaction =
                    transaction;

                pruneCommand.CommandText =
                    """
                    DELETE FROM sega_dynamic_tool_execution_history
                    WHERE tool_id = $tool_id
                      AND execution_id NOT IN (
                          SELECT execution_id
                          FROM sega_dynamic_tool_execution_history
                          WHERE tool_id = $tool_id
                          ORDER BY recorded_at_utc DESC
                          LIMIT 128
                      );
                    """;

                pruneCommand.Parameters.AddWithValue(
                    "$tool_id",
                    id.ToString("D"));

                await pruneCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private SegaDynamicToolRecord ReadRecord(
        SqliteDataReader reader)
    {
        SegaDynamicToolDefinition? definition =
            JsonSerializer.Deserialize<SegaDynamicToolDefinition>(
                reader.GetString(5),
                _jsonOptions);

        if (definition == null)
        {
            throw new InvalidOperationException(
                "A persisted dynamic tool definition could not be read.");
        }

        DateTimeOffset created =
            DateTimeOffset.FromUnixTimeMilliseconds(
                reader.GetInt64(11));

        DateTimeOffset updated =
            DateTimeOffset.FromUnixTimeMilliseconds(
                reader.GetInt64(12));

        definition =
            definition with
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                Version = reader.GetInt32(2),
                Status = (SegaDynamicToolStatus)reader.GetInt32(3),
                Persistence = (SegaDynamicToolPersistence)reader.GetInt32(4),
                CreatedAtUtc = created,
                UpdatedAtUtc = updated
            };

        return new SegaDynamicToolRecord
        {
            Definition = definition.Normalize(),
            ExecutionCount = reader.GetInt32(6),
            SuccessCount = reader.GetInt32(7),
            FailureCount = reader.GetInt32(8),
            LastResult = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            LastExecutedAtUtc = reader.IsDBNull(10)
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(10))
        };
    }

    private static void AddRecordParameters(
        SqliteCommand command,
        SegaDynamicToolRecord record,
        string definitionJson)
    {
        SegaDynamicToolDefinition definition =
            record.Definition;

        command.Parameters.AddWithValue(
            "$id",
            definition.Id.ToString("D"));

        command.Parameters.AddWithValue(
            "$name",
            definition.Name);

        command.Parameters.AddWithValue(
            "$version",
            definition.Version);

        command.Parameters.AddWithValue(
            "$status",
            (int)definition.Status);

        command.Parameters.AddWithValue(
            "$persistence",
            (int)definition.Persistence);

        command.Parameters.AddWithValue(
            "$definition_json",
            definitionJson);

        command.Parameters.AddWithValue(
            "$execution_count",
            Math.Max(
                0,
                record.ExecutionCount));

        command.Parameters.AddWithValue(
            "$success_count",
            Math.Max(
                0,
                record.SuccessCount));

        command.Parameters.AddWithValue(
            "$failure_count",
            Math.Max(
                0,
                record.FailureCount));

        command.Parameters.AddWithValue(
            "$last_result",
            Truncate(
                record.LastResult,
                2400));

        command.Parameters.AddWithValue(
            "$last_executed_at_utc",
            record.LastExecutedAtUtc.HasValue
                ? record.LastExecutedAtUtc.Value.ToUnixTimeMilliseconds()
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$created_at_utc",
            definition.CreatedAtUtc.ToUnixTimeMilliseconds());

        command.Parameters.AddWithValue(
            "$updated_at_utc",
            definition.UpdatedAtUtc.ToUnixTimeMilliseconds());
    }

    private async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await CreateBaseSchemaAsync(
            connection,
            cancellationToken);

        int? version =
            await ReadSchemaVersionAsync(
                connection,
                cancellationToken);

        if (!version.HasValue)
        {
            await EnsureVersion2SchemaAsync(
                connection,
                cancellationToken);

            await WriteSchemaVersionAsync(
                connection,
                SchemaVersion,
                cancellationToken);

            return;
        }

        if (version.Value == 1)
        {
            await MigrateVersion1To2Async(
                connection,
                cancellationToken);

            version =
                2;
        }

        if (version.Value !=
            SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported dynamic tool schema version '{version.Value}'.");
        }

        await EnsureVersion2SchemaAsync(
            connection,
            cancellationToken);
    }

    private static async Task CreateBaseSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sega_dynamic_tool_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sega_dynamic_tools (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                version INTEGER NOT NULL,
                status INTEGER NOT NULL,
                persistence INTEGER NOT NULL,
                definition_json TEXT NOT NULL,
                execution_count INTEGER NOT NULL DEFAULT 0,
                success_count INTEGER NOT NULL DEFAULT 0,
                failure_count INTEGER NOT NULL DEFAULT 0,
                last_result TEXT NOT NULL DEFAULT '',
                last_executed_at_utc INTEGER NULL,
                created_at_utc INTEGER NOT NULL,
                updated_at_utc INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sega_dynamic_tool_versions (
                tool_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                definition_json TEXT NOT NULL,
                recorded_at_utc INTEGER NOT NULL,
                PRIMARY KEY(tool_id, version),
                FOREIGN KEY(tool_id) REFERENCES sega_dynamic_tools(id)
            );

            CREATE INDEX IF NOT EXISTS idx_sega_dynamic_tools_status
                ON sega_dynamic_tools(status, updated_at_utc DESC);

            CREATE INDEX IF NOT EXISTS idx_sega_dynamic_tool_versions_tool
                ON sega_dynamic_tool_versions(tool_id, version DESC);
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task EnsureVersion2SchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sega_dynamic_tool_execution_history (
                execution_id TEXT PRIMARY KEY,
                tool_id TEXT NOT NULL,
                tool_version INTEGER NOT NULL,
                succeeded INTEGER NOT NULL,
                aborted INTEGER NOT NULL,
                summary TEXT NOT NULL DEFAULT '',
                failure_reason TEXT NOT NULL DEFAULT '',
                evidence_json TEXT NOT NULL DEFAULT '',
                started_at_utc INTEGER NOT NULL,
                finished_at_utc INTEGER NOT NULL,
                recorded_at_utc INTEGER NOT NULL,
                FOREIGN KEY(tool_id) REFERENCES sega_dynamic_tools(id)
            );

            CREATE INDEX IF NOT EXISTS idx_sega_dynamic_tool_history_tool
                ON sega_dynamic_tool_execution_history(tool_id, recorded_at_utc DESC);

            CREATE INDEX IF NOT EXISTS idx_sega_dynamic_tool_history_recent
                ON sega_dynamic_tool_execution_history(recorded_at_utc DESC);
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task<int?> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            "SELECT value FROM sega_dynamic_tool_meta WHERE key = 'schema_version';";

        object? value =
            await command.ExecuteScalarAsync(
                cancellationToken);

        if (value == null ||
            value == DBNull.Value)
        {
            return null;
        }

        if (!int.TryParse(
                Convert.ToString(value),
                out int version))
        {
            throw new InvalidOperationException(
                $"Invalid dynamic tool schema version '{value}'.");
        }

        return version;
    }

    private static async Task WriteSchemaVersionAsync(
        SqliteConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO sega_dynamic_tool_meta (key, value)
            VALUES ('schema_version', $version)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;

        command.Parameters.AddWithValue(
            "$version",
            version.ToString());

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task MigrateVersion1To2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction =
                    transaction;

                command.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS sega_dynamic_tool_execution_history (
                        execution_id TEXT PRIMARY KEY,
                        tool_id TEXT NOT NULL,
                        tool_version INTEGER NOT NULL,
                        succeeded INTEGER NOT NULL,
                        aborted INTEGER NOT NULL,
                        summary TEXT NOT NULL DEFAULT '',
                        failure_reason TEXT NOT NULL DEFAULT '',
                        evidence_json TEXT NOT NULL DEFAULT '',
                        started_at_utc INTEGER NOT NULL,
                        finished_at_utc INTEGER NOT NULL,
                        recorded_at_utc INTEGER NOT NULL,
                        FOREIGN KEY(tool_id) REFERENCES sega_dynamic_tools(id)
                    );

                    CREATE INDEX IF NOT EXISTS idx_sega_dynamic_tool_history_tool
                        ON sega_dynamic_tool_execution_history(tool_id, recorded_at_utc DESC);

                    CREATE INDEX IF NOT EXISTS idx_sega_dynamic_tool_history_recent
                        ON sega_dynamic_tool_execution_history(recorded_at_utc DESC);

                    UPDATE sega_dynamic_tool_meta
                    SET value = '2'
                    WHERE key = 'schema_version';
                    """;

                await command.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }

    private string BuildHistoryEvidenceJson(
        SegaDynamicToolExecutionResult result)
    {
        object evidence =
            new
            {
                result.ToolId,
                result.ToolName,
                result.ToolVersion,
                result.Succeeded,
                result.Aborted,
                Steps = result.StepResults.Select(
                    step =>
                        new
                        {
                            step.StepId,
                            Status = step.Status.ToString(),
                            Summary = SanitizeHistoryText(
                                step.Summary,
                                1200),
                            CapabilityId = step.CapabilityResult?.CapabilityId,
                            CapabilityStatus = step.CapabilityResult?.Status.ToString(),
                            ExitCode = step.CapabilityResult?.ExitCode,
                            HttpStatusCode = step.CapabilityResult?.HttpStatusCode,
                            OutcomeUncertain = step.CapabilityResult?.OutcomeUncertain,
                            AuditAttemptId = step.CapabilityResult?.AuditAttemptId
                        })
                    .ToArray()
            };

        string json =
            JsonSerializer.Serialize(
                evidence,
                _jsonOptions);

        return json.Length <=
            12000
                ? json
                : json[..12000];
    }

    private static string SanitizeHistoryText(
        string? value,
        int maximum)
    {
        string text =
            Truncate(
                value,
                maximum);

        if (SegaSensitiveMemoryPolicy.ContainsSensitiveSecret(
                text))
        {
            return
                "Sensitive execution detail omitted from durable dynamic-tool history.";
        }

        return text;
    }

    private SqliteConnection CreateConnection() =>
        new(
            _connectionString);

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA foreign_keys = ON; " +
            "PRAGMA busy_timeout = 5000; " +
            "PRAGMA journal_mode = WAL;";

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static string Truncate(
        string? value,
        int maximum)
    {
        string text =
            value?.Trim() ??
            string.Empty;

        return text.Length <=
            maximum
                ? text
                : text[..maximum];
    }
}
