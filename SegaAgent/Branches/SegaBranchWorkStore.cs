/*
 * filename: SegaBranchWorkStore.cs
 */

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Data.Sqlite;

namespace SegaAgent.Branches;

public sealed class SegaBranchWorkStore
{
    private const int SchemaVersion =
        1;

    private readonly SegaBranchStore
        _branchStore;

    private readonly SemaphoreSlim
        _initializationLock =
            new(
                1,
                1);

    private readonly JsonSerializerOptions
        _jsonOptions;

    private readonly string
        _connectionString;

    private bool
        _initialized;

    public string DatabasePath =>
        _branchStore.DatabasePath;

    public SegaBranchWorkStore(
        SegaBranchStore branchStore)
    {
        _branchStore =
            branchStore
            ?? throw new ArgumentNullException(
                nameof(branchStore));

        _connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _branchStore.DatabasePath,

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

        _jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive =
                    true
            };

        _jsonOptions.Converters.Add(
            new JsonStringEnumConverter());
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

            // The branch-work queue deliberately shares the executive
            // database with goals/branches so ownership can be enforced
            // with real foreign keys.
            await _branchStore.InitializeAsync(
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

    public async Task<IReadOnlyList<SegaBranchWorkItem>>
        ReadAllAsync(
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
                branch_id,
                goal_id,
                kind,
                status,
                capability_request_json,
                dynamic_tool_invocation_json,
                reason,
                result_summary,
                result_evidence,
                created_at_utc,
                started_at_utc,
                finished_at_utc,
                result_notified_at_utc
            FROM sega_branch_work_items
            ORDER BY created_at_utc ASC;
            """;

        List<SegaBranchWorkItem> result =
            new();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                ReadItem(
                    reader));
        }

        return result;
    }

    public async Task UpsertAsync(
        SegaBranchWorkItem work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            work);

        await InitializeAsync(
            cancellationToken);

        SegaBranchWorkItem normalized =
            work.Normalize();

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
            INSERT INTO sega_branch_work_items (
                id,
                branch_id,
                goal_id,
                kind,
                status,
                capability_request_json,
                dynamic_tool_invocation_json,
                reason,
                result_summary,
                result_evidence,
                created_at_utc,
                started_at_utc,
                finished_at_utc,
                result_notified_at_utc
            )
            VALUES (
                $id,
                $branch_id,
                $goal_id,
                $kind,
                $status,
                $capability_request_json,
                $dynamic_tool_invocation_json,
                $reason,
                $result_summary,
                $result_evidence,
                $created_at_utc,
                $started_at_utc,
                $finished_at_utc,
                $result_notified_at_utc
            )
            ON CONFLICT(id) DO UPDATE SET
                branch_id = excluded.branch_id,
                goal_id = excluded.goal_id,
                kind = excluded.kind,
                status = excluded.status,
                capability_request_json = excluded.capability_request_json,
                dynamic_tool_invocation_json = excluded.dynamic_tool_invocation_json,
                reason = excluded.reason,
                result_summary = excluded.result_summary,
                result_evidence = excluded.result_evidence,
                created_at_utc = excluded.created_at_utc,
                started_at_utc = excluded.started_at_utc,
                finished_at_utc = excluded.finished_at_utc,
                result_notified_at_utc = excluded.result_notified_at_utc;
            """;

        command.Parameters.AddWithValue(
            "$id",
            normalized.Id.ToString("D"));

        command.Parameters.AddWithValue(
            "$branch_id",
            normalized.BranchId.ToString("D"));

        command.Parameters.AddWithValue(
            "$goal_id",
            normalized.GoalId.ToString("D"));

        command.Parameters.AddWithValue(
            "$kind",
            (int)normalized.Kind);

        command.Parameters.AddWithValue(
            "$status",
            (int)normalized.Status);

        command.Parameters.AddWithValue(
            "$capability_request_json",
            normalized.CapabilityRequest == null
                ? DBNull.Value
                : JsonSerializer.Serialize(
                    normalized.CapabilityRequest,
                    _jsonOptions));

        command.Parameters.AddWithValue(
            "$dynamic_tool_invocation_json",
            normalized.DynamicToolInvocation == null
                ? DBNull.Value
                : JsonSerializer.Serialize(
                    normalized.DynamicToolInvocation,
                    _jsonOptions));

        command.Parameters.AddWithValue(
            "$reason",
            normalized.Reason);

        command.Parameters.AddWithValue(
            "$result_summary",
            (object?)normalized.ResultSummary
            ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$result_evidence",
            (object?)normalized.ResultEvidence
            ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$created_at_utc",
            normalized.CreatedAtUtc.ToUnixTimeMilliseconds());

        command.Parameters.AddWithValue(
            "$started_at_utc",
            normalized.StartedAtUtc.HasValue
                ? (object)normalized.StartedAtUtc.Value.ToUnixTimeMilliseconds()
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$finished_at_utc",
            normalized.FinishedAtUtc.HasValue
                ? (object)normalized.FinishedAtUtc.Value.ToUnixTimeMilliseconds()
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$result_notified_at_utc",
            normalized.ResultNotifiedAtUtc.HasValue
                ? (object)normalized.ResultNotifiedAtUtc.Value.ToUnixTimeMilliseconds()
                : DBNull.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private SegaBranchWorkItem ReadItem(
        SqliteDataReader reader)
    {
        string? capabilityJson =
            reader.IsDBNull(
                5)
                ? null
                : reader.GetString(
                    5);

        string? dynamicToolJson =
            reader.IsDBNull(
                6)
                ? null
                : reader.GetString(
                    6);

        SegaAgent.Capabilities.SegaCapabilityRequest? capabilityRequest =
            string.IsNullOrWhiteSpace(
                capabilityJson)
                ? null
                : JsonSerializer.Deserialize<SegaAgent.Capabilities.SegaCapabilityRequest>(
                    capabilityJson,
                    _jsonOptions);

        SegaAgent.Tools.SegaDynamicToolInvocation? dynamicToolInvocation =
            string.IsNullOrWhiteSpace(
                dynamicToolJson)
                ? null
                : JsonSerializer.Deserialize<SegaAgent.Tools.SegaDynamicToolInvocation>(
                    dynamicToolJson,
                    _jsonOptions);

        return new SegaBranchWorkItem
        {
            Id =
                Guid.Parse(
                    reader.GetString(
                        0)),

            BranchId =
                Guid.Parse(
                    reader.GetString(
                        1)),

            GoalId =
                Guid.Parse(
                    reader.GetString(
                        2)),

            Kind =
                (SegaBranchWorkKind)reader.GetInt32(
                    3),

            Status =
                (SegaBranchWorkStatus)reader.GetInt32(
                    4),

            CapabilityRequest =
                capabilityRequest,

            DynamicToolInvocation =
                dynamicToolInvocation,

            Reason =
                reader.GetString(
                    7),

            ResultSummary =
                reader.IsDBNull(
                    8)
                    ? null
                    : reader.GetString(
                        8),

            ResultEvidence =
                reader.IsDBNull(
                    9)
                    ? null
                    : reader.GetString(
                        9),

            CreatedAtUtc =
                DateTimeOffset.FromUnixTimeMilliseconds(
                    reader.GetInt64(
                        10)),

            StartedAtUtc =
                reader.IsDBNull(
                    11)
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(
                        reader.GetInt64(
                            11)),

            FinishedAtUtc =
                reader.IsDBNull(
                    12)
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(
                        reader.GetInt64(
                            12)),

            ResultNotifiedAtUtc =
                reader.IsDBNull(
                    13)
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(
                        reader.GetInt64(
                            13))
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
            CREATE TABLE IF NOT EXISTS sega_branch_work_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO sega_branch_work_meta (key, value)
            VALUES ('schema_version', '{SchemaVersion}');

            CREATE TABLE IF NOT EXISTS sega_branch_work_items (
                id TEXT PRIMARY KEY,
                branch_id TEXT NOT NULL,
                goal_id TEXT NOT NULL,
                kind INTEGER NOT NULL,
                status INTEGER NOT NULL,
                capability_request_json TEXT NULL,
                dynamic_tool_invocation_json TEXT NULL,
                reason TEXT NOT NULL,
                result_summary TEXT NULL,
                result_evidence TEXT NULL,
                created_at_utc INTEGER NOT NULL,
                started_at_utc INTEGER NULL,
                finished_at_utc INTEGER NULL,
                result_notified_at_utc INTEGER NULL,
                FOREIGN KEY(branch_id) REFERENCES sega_branches(id),
                FOREIGN KEY(goal_id) REFERENCES sega_goals(id)
            );

            CREATE INDEX IF NOT EXISTS idx_sega_branch_work_branch
                ON sega_branch_work_items(branch_id, status, created_at_utc);

            CREATE INDEX IF NOT EXISTS idx_sega_branch_work_status
                ON sega_branch_work_items(status, created_at_utc);

            CREATE INDEX IF NOT EXISTS idx_sega_branch_work_unnotified
                ON sega_branch_work_items(result_notified_at_utc, status, finished_at_utc);
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);

        await using SqliteCommand versionCommand =
            connection.CreateCommand();

        versionCommand.CommandText =
            "SELECT value FROM sega_branch_work_meta WHERE key = 'schema_version';";

        object? value =
            await versionCommand.ExecuteScalarAsync(
                cancellationToken);

        if (!int.TryParse(
                Convert.ToString(
                    value),
                out int version)
            ||
            version != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported Sega branch-work schema. Expected {SchemaVersion}, found '{value}'.");
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
