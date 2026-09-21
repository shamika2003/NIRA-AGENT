/*
 * filename: NIRASelfPreferenceStore.cs
 */

using Microsoft.Data.Sqlite;

namespace NIRAAgent.Self.Preferences;

public sealed class NIRASelfPreferenceStore
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


    public string DatabasePath =>
        _databasePath;


    public NIRASelfPreferenceStore()
        : this(
            BuildDefaultDatabasePath())
    {
    }


    // =========================================================
    // EXPLICIT DATABASE PATH
    //
    // Used by acceptance/integration tests and future isolated
    // NIRA profiles without touching the normal application DB.
    // The desktop application continues using the parameterless
    // constructor above.
    // =========================================================

    public NIRASelfPreferenceStore(
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
                "NIRA self-preference database path must include a directory.",
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
    }


    private static string BuildDefaultDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "NIRAAgent",
            "self",
            "NIRA-self.db");
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
    // READ ALL CURRENT PREFERENCES
    // =========================================================

    public async Task<IReadOnlyList<NIRASelfPreferenceState>>
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
                preference_key,
                subject,
                topic_key,
                affinity,
                confidence,
                observation_count,
                contradiction_count,
                status,
                created_at_utc,
                updated_at_utc
            FROM NIRA_self_preferences
            ORDER BY updated_at_utc DESC;
            """;


        List<NIRASelfPreferenceState> result =
            new();


        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);


        while (
            await reader.ReadAsync(
                cancellationToken))
        {
            result.Add(
                new NIRASelfPreferenceState
                {
                    Key =
                        reader.GetString(
                            0),

                    Subject =
                        reader.GetString(
                            1),

                    TopicKey =
                        reader.IsDBNull(
                                2)
                            ? null
                            : reader.GetString(
                                2),

                    Affinity =
                        reader.GetDouble(
                            3),

                    Confidence =
                        reader.GetDouble(
                            4),

                    ObservationCount =
                        reader.GetInt32(
                            5),

                    ContradictionCount =
                        reader.GetInt32(
                            6),

                    Status =
                        (NIRASelfPreferenceStatus)
                            reader.GetInt32(
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
                                    9))
                }
                .Normalize());
        }


        return result;
    }


    // =========================================================
    // READ TEMPORARY OPINIONS
    // =========================================================

    public async Task<IReadOnlyList<NIRATemporaryOpinionState>>
        ReadTemporaryOpinionsAsync(
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
                preference_key,
                subject,
                topic_key,
                base_affinity,
                confidence,
                half_life_hours,
                experience_count,
                created_at_utc,
                last_experienced_at_utc
            FROM NIRA_self_temporary_opinions
            ORDER BY last_experienced_at_utc DESC;
            """;


        List<NIRATemporaryOpinionState> result =
            new();


        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);


        while (
            await reader.ReadAsync(
                cancellationToken))
        {
            result.Add(
                new NIRATemporaryOpinionState
                {
                    Key =
                        reader.GetString(
                            0),

                    Subject =
                        reader.GetString(
                            1),

                    TopicKey =
                        reader.IsDBNull(
                                2)
                            ? null
                            : reader.GetString(
                                2),

                    BaseAffinity =
                        reader.GetDouble(
                            3),

                    Confidence =
                        reader.GetDouble(
                            4),

                    HalfLifeHours =
                        reader.GetDouble(
                            5),

                    ExperienceCount =
                        reader.GetInt32(
                            6),

                    CreatedAt =
                        DateTimeOffset
                            .FromUnixTimeMilliseconds(
                                reader.GetInt64(
                                    7)),

                    LastExperiencedAt =
                        DateTimeOffset
                            .FromUnixTimeMilliseconds(
                                reader.GetInt64(
                                    8))
                }
                .Normalize());
        }


        return result;
    }


    // =========================================================
    // EVIDENCE DE-DUPLICATION
    //
    // One completed NIRAMindEvent may contribute at most one
    // observation to one preference key.
    // =========================================================

    public async Task<bool> ContainsEvidenceAsync(
        string preferenceKey,
        Guid sourceEventId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(
            cancellationToken);


        string normalizedKey =
            NIRASelfPreferenceState
                .NormalizeRequiredKey(
                    preferenceKey);


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
            FROM NIRA_self_preference_evidence
            WHERE preference_key = $key
              AND source_event_id = $eventId
            LIMIT 1;
            """;


        command.Parameters.AddWithValue(
            "$key",
            normalizedKey);


        command.Parameters.AddWithValue(
            "$eventId",
            sourceEventId.ToString(
                "D"));


        object? value =
            await command.ExecuteScalarAsync(
                cancellationToken);


        return value !=
            null;
    }


    // =========================================================
    // UPSERT STATE + APPEND EVIDENCE
    // =========================================================

    internal async Task UpsertAsync(
        NIRASelfPreferenceState state,
        NIRATemporaryOpinionState temporaryOpinion,
        NIRASelfPreferenceObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            state);


        ArgumentNullException.ThrowIfNull(
            temporaryOpinion);


        ArgumentNullException.ThrowIfNull(
            observation);


        await InitializeAsync(
            cancellationToken);


        NIRASelfPreferenceState normalizedState =
            state.Normalize();


        NIRATemporaryOpinionState normalizedTemporary =
            temporaryOpinion.Normalize();


        NIRASelfPreferenceObservation normalizedObservation =
            observation.Normalize();


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
                INSERT INTO NIRA_self_preferences
                (
                    preference_key,
                    subject,
                    topic_key,
                    affinity,
                    confidence,
                    observation_count,
                    contradiction_count,
                    status,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES
                (
                    $key,
                    $subject,
                    $topic,
                    $affinity,
                    $confidence,
                    $observations,
                    $contradictions,
                    $status,
                    $created,
                    $updated
                )
                ON CONFLICT(preference_key)
                DO UPDATE SET
                    subject = excluded.subject,
                    topic_key = excluded.topic_key,
                    affinity = excluded.affinity,
                    confidence = excluded.confidence,
                    observation_count = excluded.observation_count,
                    contradiction_count = excluded.contradiction_count,
                    status = excluded.status,
                    updated_at_utc = excluded.updated_at_utc;
                """;


            command.Parameters.AddWithValue(
                "$key",
                normalizedState.Key);


            command.Parameters.AddWithValue(
                "$subject",
                normalizedState.Subject);


            command.Parameters.AddWithValue(
                "$topic",
                (object?)normalizedState.TopicKey
                ?? DBNull.Value);


            command.Parameters.AddWithValue(
                "$affinity",
                normalizedState.Affinity);


            command.Parameters.AddWithValue(
                "$confidence",
                normalizedState.Confidence);


            command.Parameters.AddWithValue(
                "$observations",
                normalizedState.ObservationCount);


            command.Parameters.AddWithValue(
                "$contradictions",
                normalizedState.ContradictionCount);


            command.Parameters.AddWithValue(
                "$status",
                (int)normalizedState.Status);


            command.Parameters.AddWithValue(
                "$created",
                normalizedState.CreatedAt
                    .ToUnixTimeMilliseconds());


            command.Parameters.AddWithValue(
                "$updated",
                normalizedState.UpdatedAt
                    .ToUnixTimeMilliseconds());


            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await using (
            SqliteCommand temporary =
                connection.CreateCommand())
        {
            temporary.Transaction =
                transaction;


            temporary.CommandText =
                """
                INSERT INTO NIRA_self_temporary_opinions
                (
                    preference_key,
                    subject,
                    topic_key,
                    base_affinity,
                    confidence,
                    half_life_hours,
                    experience_count,
                    created_at_utc,
                    last_experienced_at_utc
                )
                VALUES
                (
                    $key,
                    $subject,
                    $topic,
                    $affinity,
                    $confidence,
                    $halfLife,
                    $experiences,
                    $created,
                    $lastExperienced
                )
                ON CONFLICT(preference_key)
                DO UPDATE SET
                    subject = excluded.subject,
                    topic_key = excluded.topic_key,
                    base_affinity = excluded.base_affinity,
                    confidence = excluded.confidence,
                    half_life_hours = excluded.half_life_hours,
                    experience_count = excluded.experience_count,
                    last_experienced_at_utc = excluded.last_experienced_at_utc;
                """;


            temporary.Parameters.AddWithValue(
                "$key",
                normalizedTemporary.Key);


            temporary.Parameters.AddWithValue(
                "$subject",
                normalizedTemporary.Subject);


            temporary.Parameters.AddWithValue(
                "$topic",
                (object?)normalizedTemporary.TopicKey
                ?? DBNull.Value);


            temporary.Parameters.AddWithValue(
                "$affinity",
                normalizedTemporary.BaseAffinity);


            temporary.Parameters.AddWithValue(
                "$confidence",
                normalizedTemporary.Confidence);


            temporary.Parameters.AddWithValue(
                "$halfLife",
                normalizedTemporary.HalfLifeHours);


            temporary.Parameters.AddWithValue(
                "$experiences",
                normalizedTemporary.ExperienceCount);


            temporary.Parameters.AddWithValue(
                "$created",
                normalizedTemporary.CreatedAt
                    .ToUnixTimeMilliseconds());


            temporary.Parameters.AddWithValue(
                "$lastExperienced",
                normalizedTemporary.LastExperiencedAt
                    .ToUnixTimeMilliseconds());


            await temporary.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await using (
            SqliteCommand evidence =
                connection.CreateCommand())
        {
            evidence.Transaction =
                transaction;


            evidence.CommandText =
                """
                INSERT INTO NIRA_self_preference_evidence
                (
                    id,
                    preference_key,
                    affinity,
                    evidence_strength,
                    confidence,
                    source_event_id,
                    source_event_sequence,
                    source_timestamp_utc,
                    evidence_summary,
                    recorded_at_utc
                )
                VALUES
                (
                    $id,
                    $key,
                    $affinity,
                    $strength,
                    $confidence,
                    $eventId,
                    $eventSequence,
                    $sourceTimestamp,
                    $summary,
                    $recorded
                );
                """;


            evidence.Parameters.AddWithValue(
                "$id",
                Guid.NewGuid()
                    .ToString(
                        "D"));


            evidence.Parameters.AddWithValue(
                "$key",
                normalizedObservation.Key);


            evidence.Parameters.AddWithValue(
                "$affinity",
                normalizedObservation.Affinity);


            evidence.Parameters.AddWithValue(
                "$strength",
                normalizedObservation.EvidenceStrength);


            evidence.Parameters.AddWithValue(
                "$confidence",
                normalizedObservation.Confidence);


            evidence.Parameters.AddWithValue(
                "$eventId",
                normalizedObservation.SourceEventId.HasValue
                    ? normalizedObservation.SourceEventId
                        .Value
                        .ToString(
                            "D")
                    : DBNull.Value);


            evidence.Parameters.AddWithValue(
                "$eventSequence",
                normalizedObservation.SourceEventSequence.HasValue
                    ? normalizedObservation.SourceEventSequence.Value
                    : DBNull.Value);


            evidence.Parameters.AddWithValue(
                "$sourceTimestamp",
                normalizedObservation.SourceTimestamp.HasValue
                    ? normalizedObservation.SourceTimestamp
                        .Value
                        .ToUnixTimeMilliseconds()
                    : DBNull.Value);


            evidence.Parameters.AddWithValue(
                "$summary",
                (object?)normalizedObservation.EvidenceSummary
                ?? DBNull.Value);


            evidence.Parameters.AddWithValue(
                "$recorded",
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds());


            await evidence.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await transaction.CommitAsync(
            cancellationToken);
    }


    // =========================================================
    // SCHEMA
    // =========================================================

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await CreateBaseMetaAsync(
            connection,
            cancellationToken);


        int currentVersion =
            await ReadSchemaVersionAsync(
                connection,
                cancellationToken);


        if (currentVersion ==
            0)
        {
            await CreateVersion2SchemaAsync(
                connection,
                cancellationToken);


            await WriteSchemaVersionAsync(
                connection,
                SchemaVersion,
                cancellationToken);


            return;
        }


        if (currentVersion ==
            1)
        {
            await MigrateVersion1ToVersion2Async(
                connection,
                cancellationToken);


            currentVersion =
                2;
        }


        if (currentVersion !=
            SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported NIRA self-state database schema. " +
                $"Found={currentVersion}, Expected={SchemaVersion}.");
        }


        // Idempotently ensure all current objects exist even if a
        // previous process was interrupted during initialization.
        await CreateVersion2SchemaAsync(
            connection,
            cancellationToken);
    }


    private static async Task CreateBaseMetaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS NIRA_self_meta
            (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            SELECT value
            FROM NIRA_self_meta
            WHERE key = 'schema_version';
            """;


        object? value =
            await command.ExecuteScalarAsync(
                cancellationToken);


        if (value ==
            null)
        {
            return 0;
        }


        if (!int.TryParse(
                Convert.ToString(
                    value),
                out int version))
        {
            throw new InvalidOperationException(
                "NIRA self-state database contains an invalid schema version.");
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
            INSERT INTO NIRA_self_meta
            (
                key,
                value
            )
            VALUES
            (
                'schema_version',
                $version
            )
            ON CONFLICT(key)
            DO UPDATE SET
                value = excluded.value;
            """;


        command.Parameters.AddWithValue(
            "$version",
            version.ToString());


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    private static async Task MigrateVersion1ToVersion2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);


        try
        {
            await using SqliteCommand command =
                connection.CreateCommand();


            command.Transaction =
                transaction;


            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS NIRA_self_temporary_opinions
                (
                    preference_key TEXT PRIMARY KEY,
                    subject TEXT NOT NULL,
                    topic_key TEXT NULL,
                    base_affinity REAL NOT NULL,
                    confidence REAL NOT NULL,
                    half_life_hours REAL NOT NULL,
                    experience_count INTEGER NOT NULL,
                    created_at_utc INTEGER NOT NULL,
                    last_experienced_at_utc INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS
                    idx_NIRA_self_temporary_opinions_experienced
                    ON NIRA_self_temporary_opinions(last_experienced_at_utc DESC);

                INSERT INTO NIRA_self_meta
                (
                    key,
                    value
                )
                VALUES
                (
                    'schema_version',
                    '2'
                )
                ON CONFLICT(key)
                DO UPDATE SET
                    value = excluded.value;
                """;


            await command.ExecuteNonQueryAsync(
                cancellationToken);


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


    private static async Task CreateVersion2SchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS NIRA_self_preferences
            (
                preference_key TEXT PRIMARY KEY,
                subject TEXT NOT NULL,
                topic_key TEXT NULL,
                affinity REAL NOT NULL,
                confidence REAL NOT NULL,
                observation_count INTEGER NOT NULL,
                contradiction_count INTEGER NOT NULL,
                status INTEGER NOT NULL,
                created_at_utc INTEGER NOT NULL,
                updated_at_utc INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS NIRA_self_preference_evidence
            (
                id TEXT PRIMARY KEY,
                preference_key TEXT NOT NULL,
                affinity REAL NOT NULL,
                evidence_strength REAL NOT NULL,
                confidence REAL NOT NULL,
                source_event_id TEXT NULL,
                source_event_sequence INTEGER NULL,
                source_timestamp_utc INTEGER NULL,
                evidence_summary TEXT NULL,
                recorded_at_utc INTEGER NOT NULL,

                FOREIGN KEY(preference_key)
                    REFERENCES NIRA_self_preferences(preference_key)
            );

            CREATE TABLE IF NOT EXISTS NIRA_self_temporary_opinions
            (
                preference_key TEXT PRIMARY KEY,
                subject TEXT NOT NULL,
                topic_key TEXT NULL,
                base_affinity REAL NOT NULL,
                confidence REAL NOT NULL,
                half_life_hours REAL NOT NULL,
                experience_count INTEGER NOT NULL,
                created_at_utc INTEGER NOT NULL,
                last_experienced_at_utc INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS
                idx_NIRA_self_preferences_status
                ON NIRA_self_preferences(status);

            CREATE INDEX IF NOT EXISTS
                idx_NIRA_self_preferences_updated
                ON NIRA_self_preferences(updated_at_utc DESC);

            CREATE INDEX IF NOT EXISTS
                idx_NIRA_self_preference_evidence_key
                ON NIRA_self_preference_evidence(preference_key);

            CREATE INDEX IF NOT EXISTS
                idx_NIRA_self_preference_evidence_recorded
                ON NIRA_self_preference_evidence(recorded_at_utc DESC);

            CREATE UNIQUE INDEX IF NOT EXISTS
                idx_NIRA_self_preference_evidence_unique_event
                ON NIRA_self_preference_evidence
                (
                    preference_key,
                    source_event_id
                )
                WHERE source_event_id IS NOT NULL;

            CREATE INDEX IF NOT EXISTS
                idx_NIRA_self_temporary_opinions_experienced
                ON NIRA_self_temporary_opinions(last_experienced_at_utc DESC);
            """;


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

