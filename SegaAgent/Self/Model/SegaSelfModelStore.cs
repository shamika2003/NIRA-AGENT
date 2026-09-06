/*
 * filename: SegaSelfModelStore.cs
 */

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Data.Sqlite;

using SegaAgent.Temporal;

namespace SegaAgent.Self.Model;


public sealed class SegaSelfModelStore
{
    private const int SchemaVersion =
        2;


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


    private readonly JsonSerializerOptions
        _jsonOptions;


    public string DatabasePath =>
        _databasePath;


    public SegaSelfModelStore()
        : this(
            BuildDefaultDatabasePath())
    {
    }


    public SegaSelfModelStore(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);


        _databasePath =
            Path.GetFullPath(
                databasePath.Trim());


        string? directory =
            Path.GetDirectoryName(
                _databasePath);


        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new ArgumentException(
                "Sega self-model database path must include a directory.",
                nameof(databasePath));
        }


        Directory.CreateDirectory(
            directory);


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


        _jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive =
                    true
            };


        _jsonOptions.Converters.Add(
            new JsonStringEnumConverter());
    }


    private static string BuildDefaultDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "SegaAgent",
            "self",
            "sega-self-model.db");
    }


    // =========================================================
    // INITIALIZE
    // =========================================================

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


    // =========================================================
    // SELF FACTS
    // =========================================================

    public async Task<IReadOnlyList<SegaSelfFactState>>
        ReadFactsAsync(
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
                fact_key,
                category,
                statement,
                authority,
                source_reference,
                status,
                created_at_utc,
                updated_at_utc,
                retired_at_utc
            FROM sega_self_facts
            ORDER BY category ASC, fact_key ASC;
            """;


        List<SegaSelfFactState> result =
            new();


        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);


        while (
            await reader.ReadAsync(
                cancellationToken))
        {
            result.Add(
                ReadFact(
                    reader));
        }


        return result;
    }


    public async Task UpsertFactAsync(
        SegaSelfFactState fact,
        CancellationToken cancellationToken = default)
    {
        SegaSelfFactState normalized =
            fact.Normalize();


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
            INSERT INTO sega_self_facts (
                fact_key,
                category,
                statement,
                authority,
                source_reference,
                status,
                created_at_utc,
                updated_at_utc,
                retired_at_utc
            )
            VALUES (
                $fact_key,
                $category,
                $statement,
                $authority,
                $source_reference,
                $status,
                $created_at_utc,
                $updated_at_utc,
                NULL
            )
            ON CONFLICT(fact_key) DO UPDATE SET
                category = excluded.category,
                statement = excluded.statement,
                authority = excluded.authority,
                source_reference = excluded.source_reference,
                status = excluded.status,
                updated_at_utc = excluded.updated_at_utc,
                retired_at_utc = NULL;
            """;


        command.Parameters.AddWithValue(
            "$fact_key",
            normalized.Key);


        command.Parameters.AddWithValue(
            "$category",
            (int)normalized.Category);


        command.Parameters.AddWithValue(
            "$statement",
            normalized.Statement);


        command.Parameters.AddWithValue(
            "$authority",
            (int)normalized.Authority);


        command.Parameters.AddWithValue(
            "$source_reference",
            (object?)normalized.SourceReference
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$status",
            (int)SegaSelfFactStatus.Active);


        command.Parameters.AddWithValue(
            "$created_at_utc",
            normalized.CreatedAt
                .ToUnixTimeMilliseconds());


        command.Parameters.AddWithValue(
            "$updated_at_utc",
            normalized.UpdatedAt
                .ToUnixTimeMilliseconds());


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    public async Task RetireFactAsync(
        string factKey,
        DateTimeOffset retiredAt,
        CancellationToken cancellationToken = default)
    {
        factKey =
            SegaSelfFactState.NormalizeKey(
                factKey);


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
            UPDATE sega_self_facts
            SET
                status = $status,
                updated_at_utc = $updated_at_utc,
                retired_at_utc = $retired_at_utc
            WHERE fact_key = $fact_key;
            """;


        command.Parameters.AddWithValue(
            "$status",
            (int)SegaSelfFactStatus.Retired);


        command.Parameters.AddWithValue(
            "$updated_at_utc",
            retiredAt.ToUnixTimeMilliseconds());


        command.Parameters.AddWithValue(
            "$retired_at_utc",
            retiredAt.ToUnixTimeMilliseconds());


        command.Parameters.AddWithValue(
            "$fact_key",
            factKey);


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // =========================================================
    // COMMITMENTS
    // =========================================================

    public async Task<IReadOnlyList<SegaCommitmentState>>
        ReadCommitmentsAsync(
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
                commitment_id,
                fingerprint,
                summary,
                status,
                source_event_id,
                source_event_name,
                last_evidence_quote,
                last_reason,
                created_at_utc,
                updated_at_utc,
                resolved_at_utc,
                temporal_json
            FROM sega_commitments
            ORDER BY updated_at_utc DESC;
            """;


        List<SegaCommitmentState> result =
            new();


        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);


        while (
            await reader.ReadAsync(
                cancellationToken))
        {
            result.Add(
                ReadCommitment(
                    reader));
        }


        return result;
    }


    public async Task InsertCommitmentAsync(
        SegaCommitmentState commitment,
        SegaCommitmentEvidenceSource evidenceSource,
        CancellationToken cancellationToken = default)
    {
        SegaCommitmentState normalized =
            commitment.Normalize();


        await InitializeAsync(
            cancellationToken);


        await using SqliteConnection connection =
            CreateConnection();


        await connection.OpenAsync(
            cancellationToken);


        await ConfigureConnectionAsync(
            connection,
            cancellationToken);


        using SqliteTransaction transaction =
            connection.BeginTransaction();


        await using (
            SqliteCommand command =
                connection.CreateCommand())
        {
            command.Transaction =
                transaction;


            command.CommandText =
                """
                INSERT INTO sega_commitments (
                    commitment_id,
                    fingerprint,
                    summary,
                    status,
                    source_event_id,
                    source_event_name,
                    last_evidence_quote,
                    last_reason,
                    created_at_utc,
                    updated_at_utc,
                    resolved_at_utc,
                    temporal_json
                )
                VALUES (
                    $commitment_id,
                    $fingerprint,
                    $summary,
                    $status,
                    $source_event_id,
                    $source_event_name,
                    $last_evidence_quote,
                    $last_reason,
                    $created_at_utc,
                    $updated_at_utc,
                    $resolved_at_utc,
                    $temporal_json
                );
                """;


            AddCommitmentParameters(
                command,
                normalized);


            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await InsertCommitmentHistoryAsync(
            connection,
            transaction,
            normalized.Id,
            fromStatus: null,
            toStatus:
                normalized.Status,
            evidenceSource,
            normalized.LastEvidenceQuote,
            normalized.LastReason,
            normalized.SourceEventId,
            normalized.CreatedAt,
            cancellationToken);


        if (normalized.Temporal != null)
        {
            await InsertCommitmentTemporalHistoryAsync(
                connection,
                transaction,
                normalized.Id,
                normalized.Temporal,
                "Create",
                normalized.LastReason,
                normalized.SourceEventId,
                normalized.CreatedAt,
                cancellationToken);
        }


        transaction.Commit();
    }


    public async Task UpdateCommitmentStatusAsync(
        SegaCommitmentState previous,
        SegaCommitmentState updated,
        SegaCommitmentEvidenceSource evidenceSource,
        Guid? sourceEventId,
        CancellationToken cancellationToken = default)
    {
        SegaCommitmentState normalizedPrevious =
            previous.Normalize();


        SegaCommitmentState normalizedUpdated =
            updated.Normalize();


        if (normalizedPrevious.Id !=
            normalizedUpdated.Id)
        {
            throw new InvalidOperationException(
                "Cannot transition between different commitment IDs.");
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


        using SqliteTransaction transaction =
            connection.BeginTransaction();


        await using (
            SqliteCommand command =
                connection.CreateCommand())
        {
            command.Transaction =
                transaction;


            command.CommandText =
                """
                UPDATE sega_commitments
                SET
                    status = $status,
                    last_evidence_quote = $last_evidence_quote,
                    last_reason = $last_reason,
                    updated_at_utc = $updated_at_utc,
                    resolved_at_utc = $resolved_at_utc
                WHERE commitment_id = $commitment_id;
                """;


            command.Parameters.AddWithValue(
                "$status",
                (int)normalizedUpdated.Status);


            command.Parameters.AddWithValue(
                "$last_evidence_quote",
                (object?)normalizedUpdated.LastEvidenceQuote
                ?? DBNull.Value);


            command.Parameters.AddWithValue(
                "$last_reason",
                (object?)normalizedUpdated.LastReason
                ?? DBNull.Value);


            command.Parameters.AddWithValue(
                "$updated_at_utc",
                normalizedUpdated.UpdatedAt
                    .ToUnixTimeMilliseconds());


            command.Parameters.AddWithValue(
                "$resolved_at_utc",
                normalizedUpdated.ResolvedAt.HasValue
                    ? normalizedUpdated.ResolvedAt.Value
                        .ToUnixTimeMilliseconds()
                    : DBNull.Value);


            command.Parameters.AddWithValue(
                "$commitment_id",
                normalizedUpdated.Id.ToString(
                    "D"));


            int changed =
                await command.ExecuteNonQueryAsync(
                    cancellationToken);


            if (changed !=
                1)
            {
                throw new InvalidOperationException(
                    "Commitment transition did not update exactly one row.");
            }
        }


        await InsertCommitmentHistoryAsync(
            connection,
            transaction,
            normalizedUpdated.Id,
            normalizedPrevious.Status,
            normalizedUpdated.Status,
            evidenceSource,
            normalizedUpdated.LastEvidenceQuote,
            normalizedUpdated.LastReason,
            sourceEventId,
            normalizedUpdated.UpdatedAt,
            cancellationToken);


        transaction.Commit();
    }


    // =========================================================
    // COMMITMENT TEMPORAL / COMPOSITE UPDATE
    //
    // Used for user-grounded rescheduling and scheduler-owned wake
    // bookkeeping. The caller decides whether temporal/lifecycle history
    // should be recorded; low-level wake leases do not pollute audit history.
    // =========================================================

    public async Task UpdateCommitmentTemporalAsync(
        SegaCommitmentState previous,
        SegaCommitmentState updated,
        SegaCommitmentEvidenceSource? evidenceSource,
        Guid? sourceEventId,
        string changeKind,
        bool recordTemporalHistory,
        CancellationToken cancellationToken = default)
    {
        SegaCommitmentState normalizedPrevious = previous.Normalize();
        SegaCommitmentState normalizedUpdated = updated.Normalize();

        if (normalizedPrevious.Id != normalizedUpdated.Id)
        {
            throw new InvalidOperationException(
                "Cannot update temporal state across different commitment IDs.");
        }

        if (string.IsNullOrWhiteSpace(changeKind))
        {
            changeKind = "TemporalUpdate";
        }

        await InitializeAsync(cancellationToken);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);

        using SqliteTransaction transaction = connection.BeginTransaction();

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE sega_commitments
                SET
                    status = $status,
                    temporal_json = $temporal_json,
                    last_evidence_quote = $last_evidence_quote,
                    last_reason = $last_reason,
                    updated_at_utc = $updated_at_utc,
                    resolved_at_utc = $resolved_at_utc
                WHERE commitment_id = $commitment_id;
                """;

            command.Parameters.AddWithValue(
                "$status",
                (int)normalizedUpdated.Status);

            command.Parameters.AddWithValue(
                "$temporal_json",
                SerializeTemporal(normalizedUpdated.Temporal));

            command.Parameters.AddWithValue(
                "$last_evidence_quote",
                (object?)normalizedUpdated.LastEvidenceQuote ?? DBNull.Value);

            command.Parameters.AddWithValue(
                "$last_reason",
                (object?)normalizedUpdated.LastReason ?? DBNull.Value);

            command.Parameters.AddWithValue(
                "$updated_at_utc",
                normalizedUpdated.UpdatedAt.ToUnixTimeMilliseconds());

            command.Parameters.AddWithValue(
                "$resolved_at_utc",
                normalizedUpdated.ResolvedAt.HasValue
                    ? normalizedUpdated.ResolvedAt.Value.ToUnixTimeMilliseconds()
                    : DBNull.Value);

            command.Parameters.AddWithValue(
                "$commitment_id",
                normalizedUpdated.Id.ToString("D"));

            int changed = await command.ExecuteNonQueryAsync(cancellationToken);

            if (changed != 1)
            {
                throw new InvalidOperationException(
                    "Temporal commitment update did not update exactly one row.");
            }
        }

        if (normalizedPrevious.Status != normalizedUpdated.Status &&
            evidenceSource.HasValue)
        {
            await InsertCommitmentHistoryAsync(
                connection,
                transaction,
                normalizedUpdated.Id,
                normalizedPrevious.Status,
                normalizedUpdated.Status,
                evidenceSource.Value,
                normalizedUpdated.LastEvidenceQuote,
                normalizedUpdated.LastReason,
                sourceEventId,
                normalizedUpdated.UpdatedAt,
                cancellationToken);
        }

        if (recordTemporalHistory && normalizedUpdated.Temporal != null)
        {
            await InsertCommitmentTemporalHistoryAsync(
                connection,
                transaction,
                normalizedUpdated.Id,
                normalizedUpdated.Temporal,
                changeKind.Trim(),
                normalizedUpdated.LastReason,
                sourceEventId,
                normalizedUpdated.UpdatedAt,
                cancellationToken);
        }

        transaction.Commit();
    }


    // =========================================================
    // TEMPORAL RECONCILIATION REVIEW MARKERS
    // =========================================================

    public async Task<bool> HasTemporalReviewAsync(
        Guid commitmentId,
        int reviewVersion,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 1
            FROM sega_commitment_temporal_reviews
            WHERE commitment_id = $commitment_id
              AND review_version = $review_version
              AND outcome <> 'Error'
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$commitment_id",
            commitmentId.ToString("D"));

        command.Parameters.AddWithValue(
            "$review_version",
            Math.Max(1, reviewVersion));

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }


    public async Task RecordTemporalReviewAsync(
        Guid commitmentId,
        int reviewVersion,
        string outcome,
        string? reason,
        DateTimeOffset reviewedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sega_commitment_temporal_reviews (
                commitment_id,
                review_version,
                reviewed_at_utc,
                outcome,
                reason
            )
            VALUES (
                $commitment_id,
                $review_version,
                $reviewed_at_utc,
                $outcome,
                $reason
            )
            ON CONFLICT(commitment_id, review_version) DO UPDATE SET
                reviewed_at_utc = excluded.reviewed_at_utc,
                outcome = excluded.outcome,
                reason = excluded.reason;
            """;

        command.Parameters.AddWithValue(
            "$commitment_id",
            commitmentId.ToString("D"));

        command.Parameters.AddWithValue(
            "$review_version",
            Math.Max(1, reviewVersion));

        command.Parameters.AddWithValue(
            "$reviewed_at_utc",
            reviewedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds());

        command.Parameters.AddWithValue(
            "$outcome",
            string.IsNullOrWhiteSpace(outcome) ? "Unknown" : outcome.Trim());

        command.Parameters.AddWithValue(
            "$reason",
            string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    // =========================================================
    // SCHEMA
    // =========================================================

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sega_self_model_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO sega_self_model_meta (
                key,
                value
            )
            VALUES (
                'schema_version',
                '2'
            );

            CREATE TABLE IF NOT EXISTS sega_self_facts (
                fact_key TEXT PRIMARY KEY,
                category INTEGER NOT NULL,
                statement TEXT NOT NULL,
                authority INTEGER NOT NULL,
                source_reference TEXT NULL,
                status INTEGER NOT NULL,
                created_at_utc INTEGER NOT NULL,
                updated_at_utc INTEGER NOT NULL,
                retired_at_utc INTEGER NULL
            );

            CREATE INDEX IF NOT EXISTS ix_sega_self_facts_status_category
            ON sega_self_facts(status, category);

            CREATE TABLE IF NOT EXISTS sega_commitments (
                commitment_id TEXT PRIMARY KEY,
                fingerprint TEXT NOT NULL,
                summary TEXT NOT NULL,
                status INTEGER NOT NULL,
                source_event_id TEXT NULL,
                source_event_name TEXT NULL,
                last_evidence_quote TEXT NULL,
                last_reason TEXT NULL,
                created_at_utc INTEGER NOT NULL,
                updated_at_utc INTEGER NOT NULL,
                resolved_at_utc INTEGER NULL,
                temporal_json TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_sega_commitments_status_updated
            ON sega_commitments(status, updated_at_utc DESC);

            CREATE INDEX IF NOT EXISTS ix_sega_commitments_fingerprint
            ON sega_commitments(fingerprint);

            CREATE TABLE IF NOT EXISTS sega_commitment_history (
                history_id INTEGER PRIMARY KEY AUTOINCREMENT,
                commitment_id TEXT NOT NULL,
                from_status INTEGER NULL,
                to_status INTEGER NOT NULL,
                evidence_source INTEGER NOT NULL,
                evidence_quote TEXT NULL,
                reason TEXT NULL,
                source_event_id TEXT NULL,
                occurred_at_utc INTEGER NOT NULL,
                FOREIGN KEY(commitment_id)
                    REFERENCES sega_commitments(commitment_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_sega_commitment_history_commitment
            ON sega_commitment_history(commitment_id, occurred_at_utc DESC);

            CREATE TABLE IF NOT EXISTS sega_commitment_temporal_history (
                history_id INTEGER PRIMARY KEY AUTOINCREMENT,
                commitment_id TEXT NOT NULL,
                temporal_json TEXT NOT NULL,
                change_kind TEXT NOT NULL,
                reason TEXT NULL,
                source_event_id TEXT NULL,
                occurred_at_utc INTEGER NOT NULL,
                FOREIGN KEY(commitment_id)
                    REFERENCES sega_commitments(commitment_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_sega_commitment_temporal_history_commitment
            ON sega_commitment_temporal_history(commitment_id, occurred_at_utc DESC);

            CREATE TABLE IF NOT EXISTS sega_commitment_temporal_reviews (
                commitment_id TEXT NOT NULL,
                review_version INTEGER NOT NULL,
                reviewed_at_utc INTEGER NOT NULL,
                outcome TEXT NOT NULL,
                reason TEXT NULL,
                PRIMARY KEY(commitment_id, review_version),
                FOREIGN KEY(commitment_id)
                    REFERENCES sega_commitments(commitment_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_sega_commitment_temporal_reviews_time
            ON sega_commitment_temporal_reviews(reviewed_at_utc DESC);
            """;


        await command.ExecuteNonQueryAsync(
            cancellationToken);


        await MigrateSchemaAsync(
            connection,
            cancellationToken);


        await ValidateSchemaAsync(
            connection,
            cancellationToken);
    }


    private static async Task MigrateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand read = connection.CreateCommand();
        read.CommandText =
            """
            SELECT value
            FROM sega_self_model_meta
            WHERE key = 'schema_version';
            """;

        object? raw = await read.ExecuteScalarAsync(cancellationToken);

        if (raw == null ||
            !int.TryParse(Convert.ToString(raw), out int version))
        {
            throw new InvalidOperationException(
                "Sega self-model database has no readable schema version.");
        }

        if (version == SchemaVersion)
        {
            return;
        }

        if (version != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported Sega self-model database schema version {version}.");
        }

        bool hasTemporalColumn =
            await HasColumnAsync(
                connection,
                "sega_commitments",
                "temporal_json",
                cancellationToken);

        using SqliteTransaction transaction = connection.BeginTransaction();

        if (!hasTemporalColumn)
        {
            await using SqliteCommand addColumn = connection.CreateCommand();
            addColumn.Transaction = transaction;
            addColumn.CommandText =
                """
                ALTER TABLE sega_commitments
                ADD COLUMN temporal_json TEXT NULL;
                """;

            await addColumn.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand migrate = connection.CreateCommand())
        {
            migrate.Transaction = transaction;
            migrate.CommandText =
                """
                CREATE TABLE IF NOT EXISTS sega_commitment_temporal_history (
                    history_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    commitment_id TEXT NOT NULL,
                    temporal_json TEXT NOT NULL,
                    change_kind TEXT NOT NULL,
                    reason TEXT NULL,
                    source_event_id TEXT NULL,
                    occurred_at_utc INTEGER NOT NULL,
                    FOREIGN KEY(commitment_id)
                        REFERENCES sega_commitments(commitment_id)
                        ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ix_sega_commitment_temporal_history_commitment
                ON sega_commitment_temporal_history(commitment_id, occurred_at_utc DESC);

                CREATE TABLE IF NOT EXISTS sega_commitment_temporal_reviews (
                    commitment_id TEXT NOT NULL,
                    review_version INTEGER NOT NULL,
                    reviewed_at_utc INTEGER NOT NULL,
                    outcome TEXT NOT NULL,
                    reason TEXT NULL,
                    PRIMARY KEY(commitment_id, review_version),
                    FOREIGN KEY(commitment_id)
                        REFERENCES sega_commitments(commitment_id)
                        ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ix_sega_commitment_temporal_reviews_time
                ON sega_commitment_temporal_reviews(reviewed_at_utc DESC);

                UPDATE sega_self_model_meta
                SET value = '2'
                WHERE key = 'schema_version';
                """;

            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }


    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(
                    reader.GetString(1),
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }


    private static async Task ValidateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            SELECT value
            FROM sega_self_model_meta
            WHERE key = 'schema_version';
            """;


        object? value =
            await command.ExecuteScalarAsync(
                cancellationToken);


        if (
            value ==
                null
            ||
            !int.TryParse(
                Convert.ToString(
                    value),
                out int version)
            ||
            version !=
                SchemaVersion)
        {
            throw new InvalidOperationException(
                "Unsupported Sega self-model database schema.");
        }


        if (!await HasColumnAsync(
                connection,
                "sega_commitments",
                "temporal_json",
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Sega self-model temporal schema is incomplete.");
        }
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private object SerializeTemporal(
        SegaTemporalScheduleState? temporal)
    {
        if (temporal == null)
        {
            return DBNull.Value;
        }

        return JsonSerializer.Serialize(
            temporal.Normalize(),
            _jsonOptions);
    }


    private SegaTemporalScheduleState? DeserializeTemporal(
        string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            SegaTemporalScheduleState? value =
                JsonSerializer.Deserialize<SegaTemporalScheduleState>(
                    json,
                    _jsonOptions);

            return value?.Normalize();
        }
        catch
        {
            // A damaged/obsolete temporal payload must not prevent Sega's
            // entire self-model from loading. The commitment remains active
            // and the temporal reconciliation service can review it again.
            return null;
        }
    }


    private static SegaSelfFactState ReadFact(
        SqliteDataReader reader)
    {
        return new SegaSelfFactState
        {
            Key =
                reader.GetString(
                    0),

            Category =
                (SegaSelfFactCategory)
                    reader.GetInt32(
                        1),

            Statement =
                reader.GetString(
                    2),

            Authority =
                (SegaSelfFactAuthority)
                    reader.GetInt32(
                        3),

            SourceReference =
                reader.IsDBNull(
                        4)
                    ? null
                    : reader.GetString(
                        4),

            Status =
                (SegaSelfFactStatus)
                    reader.GetInt32(
                        5),

            CreatedAt =
                DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        reader.GetInt64(
                            6)),

            UpdatedAt =
                DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        reader.GetInt64(
                            7)),

            RetiredAt =
                reader.IsDBNull(
                        8)
                    ? null
                    : DateTimeOffset
                        .FromUnixTimeMilliseconds(
                            reader.GetInt64(
                                8))
        }
        .Normalize();
    }


    private SegaCommitmentState ReadCommitment(
        SqliteDataReader reader)
    {
        Guid? sourceEventId =
            reader.IsDBNull(
                    4)
                ? null
                : Guid.TryParse(
                    reader.GetString(
                        4),
                    out Guid parsedSource)
                    ? parsedSource
                    : null;


        return new SegaCommitmentState
        {
            Id =
                Guid.Parse(
                    reader.GetString(
                        0)),

            Fingerprint =
                reader.GetString(
                    1),

            Summary =
                reader.GetString(
                    2),

            Status =
                (SegaCommitmentStatus)
                    reader.GetInt32(
                        3),

            SourceEventId =
                sourceEventId,

            SourceEventName =
                reader.IsDBNull(
                        5)
                    ? null
                    : reader.GetString(
                        5),

            LastEvidenceQuote =
                reader.IsDBNull(
                        6)
                    ? null
                    : reader.GetString(
                        6),

            LastReason =
                reader.IsDBNull(
                        7)
                    ? null
                    : reader.GetString(
                        7),

            CreatedAt =
                DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        reader.GetInt64(
                            8)),

            UpdatedAt =
                DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        reader.GetInt64(
                            9)),

            ResolvedAt =
                reader.IsDBNull(
                        10)
                    ? null
                    : DateTimeOffset
                        .FromUnixTimeMilliseconds(
                            reader.GetInt64(
                                10)),

            Temporal =
                reader.IsDBNull(
                        11)
                    ? null
                    : DeserializeTemporal(
                        reader.GetString(
                            11))
        }
        .Normalize();
    }


    private void AddCommitmentParameters(
        SqliteCommand command,
        SegaCommitmentState commitment)
    {
        command.Parameters.AddWithValue(
            "$commitment_id",
            commitment.Id.ToString(
                "D"));


        command.Parameters.AddWithValue(
            "$fingerprint",
            commitment.Fingerprint);


        command.Parameters.AddWithValue(
            "$summary",
            commitment.Summary);


        command.Parameters.AddWithValue(
            "$status",
            (int)commitment.Status);


        command.Parameters.AddWithValue(
            "$source_event_id",
            commitment.SourceEventId.HasValue
                ? commitment.SourceEventId.Value
                    .ToString(
                        "D")
                : DBNull.Value);


        command.Parameters.AddWithValue(
            "$source_event_name",
            (object?)commitment.SourceEventName
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$last_evidence_quote",
            (object?)commitment.LastEvidenceQuote
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$last_reason",
            (object?)commitment.LastReason
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$created_at_utc",
            commitment.CreatedAt
                .ToUnixTimeMilliseconds());


        command.Parameters.AddWithValue(
            "$updated_at_utc",
            commitment.UpdatedAt
                .ToUnixTimeMilliseconds());


        command.Parameters.AddWithValue(
            "$resolved_at_utc",
            commitment.ResolvedAt.HasValue
                ? commitment.ResolvedAt.Value
                    .ToUnixTimeMilliseconds()
                : DBNull.Value);


        command.Parameters.AddWithValue(
            "$temporal_json",
            SerializeTemporal(
                commitment.Temporal));
    }


    private async Task InsertCommitmentTemporalHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid commitmentId,
        SegaTemporalScheduleState temporal,
        string changeKind,
        string? reason,
        Guid? sourceEventId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO sega_commitment_temporal_history (
                commitment_id,
                temporal_json,
                change_kind,
                reason,
                source_event_id,
                occurred_at_utc
            )
            VALUES (
                $commitment_id,
                $temporal_json,
                $change_kind,
                $reason,
                $source_event_id,
                $occurred_at_utc
            );
            """;

        command.Parameters.AddWithValue(
            "$commitment_id",
            commitmentId.ToString("D"));

        command.Parameters.AddWithValue(
            "$temporal_json",
            JsonSerializer.Serialize(temporal.Normalize(), _jsonOptions));

        command.Parameters.AddWithValue(
            "$change_kind",
            changeKind);

        command.Parameters.AddWithValue(
            "$reason",
            (object?)reason ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$source_event_id",
            sourceEventId.HasValue
                ? sourceEventId.Value.ToString("D")
                : DBNull.Value);

        command.Parameters.AddWithValue(
            "$occurred_at_utc",
            occurredAt.ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    private static async Task InsertCommitmentHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid commitmentId,
        SegaCommitmentStatus? fromStatus,
        SegaCommitmentStatus toStatus,
        SegaCommitmentEvidenceSource evidenceSource,
        string? evidenceQuote,
        string? reason,
        Guid? sourceEventId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.Transaction =
            transaction;


        command.CommandText =
            """
            INSERT INTO sega_commitment_history (
                commitment_id,
                from_status,
                to_status,
                evidence_source,
                evidence_quote,
                reason,
                source_event_id,
                occurred_at_utc
            )
            VALUES (
                $commitment_id,
                $from_status,
                $to_status,
                $evidence_source,
                $evidence_quote,
                $reason,
                $source_event_id,
                $occurred_at_utc
            );
            """;


        command.Parameters.AddWithValue(
            "$commitment_id",
            commitmentId.ToString(
                "D"));


        command.Parameters.AddWithValue(
            "$from_status",
            fromStatus.HasValue
                ? (int)fromStatus.Value
                : DBNull.Value);


        command.Parameters.AddWithValue(
            "$to_status",
            (int)toStatus);


        command.Parameters.AddWithValue(
            "$evidence_source",
            (int)evidenceSource);


        command.Parameters.AddWithValue(
            "$evidence_quote",
            (object?)evidenceQuote
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$reason",
            (object?)reason
            ?? DBNull.Value);


        command.Parameters.AddWithValue(
            "$source_event_id",
            sourceEventId.HasValue
                ? sourceEventId.Value
                    .ToString(
                        "D")
                : DBNull.Value);


        command.Parameters.AddWithValue(
            "$occurred_at_utc",
            occurredAt.ToUnixTimeMilliseconds());


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // =========================================================
    // CONNECTION
    // =========================================================

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