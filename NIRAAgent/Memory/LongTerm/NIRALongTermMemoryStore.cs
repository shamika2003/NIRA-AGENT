/*
 * filename: NIRALongTermMemoryStore.cs
 */

using Microsoft.Data.Sqlite;

using NIRAAgent.Semantic;

namespace NIRAAgent.Memory.LongTerm;

public sealed class NIRALongTermMemoryStore
{
    // =========================================================
    // SCHEMA
    //
    // v1: memories
    // v2: immutable memory evidence + active canonical identity
    // =========================================================

    private const int SchemaVersion =
        2;


    // =========================================================
    // INITIALIZATION
    // =========================================================

    private readonly SemaphoreSlim
        _initializationLock =
            new(
                1,
                1);


    private bool
        _initialized;


    // =========================================================
    // STORAGE
    // =========================================================

    private readonly string
        _databasePath;


    private readonly string
        _connectionString;


    public string DatabasePath =>
        _databasePath;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public NIRALongTermMemoryStore()
        : this(
            BuildDefaultDatabasePath())
    {
    }


    // =========================================================
    // EXPLICIT DATABASE PATH
    //
    // Keeps acceptance/integration tests and future isolated
    // NIRA profiles away from the application's real memory DB.
    // Normal desktop DI still selects the parameterless
    // constructor above.
    // =========================================================

    public NIRALongTermMemoryStore(
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
                "NIRA long-term-memory database path must include a directory.",
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
                    SqliteOpenMode
                        .ReadWriteCreate,

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
                Environment
                    .SpecialFolder
                    .LocalApplicationData),
            "NIRAAgent",
            "memory",
            "NIRA-memory.db");
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


        await _initializationLock
            .WaitAsync(
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


            await CreateSchemaAsync(
                connection,
                cancellationToken);


            await MigrateSchemaAsync(
                connection,
                cancellationToken);


            await ValidateSchemaAsync(
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
    // DATABASE INTEGRITY CHECK
    //
    // quick_check validates SQLite page/index structure.
    // foreign_key_check catches lifecycle links/evidence rows that
    // no longer point at valid memory rows.
    //
    // Maintenance must not write when this result is unhealthy.
    // =========================================================

    internal async Task<NIRAMemoryDatabaseIntegrityResult>
        CheckIntegrityAsync(
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


        List<string> quickCheck =
            new();


        await using (
            SqliteCommand command =
                connection.CreateCommand())
        {
            command.CommandText =
                "PRAGMA quick_check;";


            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);


            while (await reader.ReadAsync(
                       cancellationToken))
            {
                if (!reader.IsDBNull(
                        0))
                {
                    quickCheck.Add(
                        reader.GetString(
                            0));
                }
            }
        }


        int foreignKeyViolations =
            0;


        await using (
            SqliteCommand command =
                connection.CreateCommand())
        {
            command.CommandText =
                "PRAGMA foreign_key_check;";


            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);


            while (await reader.ReadAsync(
                       cancellationToken))
            {
                foreignKeyViolations++;
            }
        }


        bool quickHealthy =
            quickCheck.Count ==
                1
            &&
            string.Equals(
                quickCheck[0],
                "ok",
                StringComparison.OrdinalIgnoreCase);


        return new NIRAMemoryDatabaseIntegrityResult
        {
            IsHealthy =
                quickHealthy
                &&
                foreignKeyViolations ==
                    0,

            QuickCheckResult =
                quickCheck.Count ==
                    0
                    ? "no-result"
                    : string.Join(
                        " | ",
                        quickCheck.Take(
                            4)),

            ForeignKeyViolationCount =
                foreignKeyViolations
        };
    }


    // =========================================================
    // INSERT
    // =========================================================

    internal async Task InsertAsync(
        NIRAMemoryRecord memory,
        SemanticEmbedding embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            memory);


        ArgumentNullException.ThrowIfNull(
            embedding);


        await InitializeAsync(
            cancellationToken);


        NIRAMemoryRecord normalized =
            memory.Normalize();


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


        await InsertMemoryAsync(
            connection,
            transaction,
            normalized,
            embedding,
            cancellationToken);


        await InsertEvidenceAsync(
            connection,
            transaction,
            normalized.Id,
            normalized.Provenance,
            normalized.CreatedAt,
            cancellationToken);


        await transaction.CommitAsync(
            cancellationToken);
    }


    // =========================================================
    // UPDATE ACTIVE MEMORY
    //
    // Used for reinforcement and safe enrichment.
    // Original row identity/provenance remain intact while new
    // evidence is appended to memory_evidence.
    // =========================================================

    internal async Task UpdateAsync(
        NIRAMemoryRecord memory,
        SemanticEmbedding embedding,
        NIRAMemoryProvenance evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            memory);


        ArgumentNullException.ThrowIfNull(
            embedding);


        ArgumentNullException.ThrowIfNull(
            evidence);


        await InitializeAsync(
            cancellationToken);


        NIRAMemoryRecord normalized =
            memory.Normalize();


        byte[] embeddingBytes =
            SerializeEmbedding(
                embedding);


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


        await using SqliteCommand command =
            connection.CreateCommand();


        command.Transaction =
            transaction;


        command.CommandText =
            """
            UPDATE memories
            SET
                kind = $kind,
                content = $content,
                canonical_key = $canonicalKey,
                topic_key = $topicKey,
                importance = $importance,
                confidence = $confidence,
                emotional_weight = $emotionalWeight,
                status = $status,
                superseded_by_memory_id = $supersededByMemoryId,
                updated_at_utc = $updatedAtUtc,
                last_recalled_at_utc = $lastRecalledAtUtc,
                reinforcement_count = $reinforcementCount,
                recall_count = $recallCount,
                embedding_dimension = $embeddingDimension,
                embedding = $embedding
            WHERE id = $id;
            """;


        AddParameter(
            command,
            "$kind",
            (int)normalized.Kind);


        AddParameter(
            command,
            "$content",
            normalized.Content);


        AddParameter(
            command,
            "$canonicalKey",
            normalized.CanonicalKey);


        AddParameter(
            command,
            "$topicKey",
            normalized.TopicKey);


        AddParameter(
            command,
            "$importance",
            normalized.Importance);


        AddParameter(
            command,
            "$confidence",
            normalized.Confidence);


        AddParameter(
            command,
            "$emotionalWeight",
            normalized.EmotionalWeight);


        AddParameter(
            command,
            "$status",
            (int)normalized.Status);


        AddParameter(
            command,
            "$supersededByMemoryId",
            normalized.SupersededByMemoryId?
                .ToString("D"));


        AddParameter(
            command,
            "$updatedAtUtc",
            ToUnixMilliseconds(
                normalized.UpdatedAt));


        AddParameter(
            command,
            "$lastRecalledAtUtc",
            normalized.LastRecalledAt.HasValue
                ? ToUnixMilliseconds(
                    normalized.LastRecalledAt.Value)
                : null);


        AddParameter(
            command,
            "$reinforcementCount",
            normalized.ReinforcementCount);


        AddParameter(
            command,
            "$recallCount",
            normalized.RecallCount);


        AddParameter(
            command,
            "$embeddingDimension",
            embedding.Dimension);


        SqliteParameter embeddingParameter =
            command.Parameters.Add(
                "$embedding",
                SqliteType.Blob);


        embeddingParameter.Value =
            embeddingBytes;


        AddParameter(
            command,
            "$id",
            normalized.Id.ToString("D"));


        int affected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);


        if (affected !=
            1)
        {
            throw new InvalidOperationException(
                $"Long-term memory update expected one row but changed {affected}.");
        }


        await InsertEvidenceAsync(
            connection,
            transaction,
            normalized.Id,
            evidence.Normalize(),
            DateTimeOffset.UtcNow,
            cancellationToken);


        await transaction.CommitAsync(
            cancellationToken);
    }


    // =========================================================
    // SUPERSEDE + INSERT
    //
    // Atomic lifecycle transition:
    //
    // old active -> superseded
    // new memory -> active
    // old.superseded_by -> new.id
    // new evidence -> appended
    // =========================================================

    internal async Task SupersedeAndInsertAsync(
        Guid previousMemoryId,
        NIRAMemoryRecord replacement,
        SemanticEmbedding replacementEmbedding,
        NIRAMemoryProvenance evidence,
        CancellationToken cancellationToken = default)
    {
        if (previousMemoryId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Previous memory ID cannot be empty.",
                nameof(previousMemoryId));
        }


        ArgumentNullException.ThrowIfNull(
            replacement);


        ArgumentNullException.ThrowIfNull(
            replacementEmbedding);


        ArgumentNullException.ThrowIfNull(
            evidence);


        await InitializeAsync(
            cancellationToken);


        NIRAMemoryRecord normalized =
            replacement.Normalize();


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


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


        // =====================================================
        // 1. Remove old row from the active canonical index.
        // Keep superseded_by null until replacement exists so the
        // foreign-key constraint remains valid.
        // =====================================================

        await using (
            SqliteCommand retire =
                connection.CreateCommand())
        {
            retire.Transaction =
                transaction;


            retire.CommandText =
                """
                UPDATE memories
                SET
                    status = $supersededStatus,
                    superseded_by_memory_id = NULL,
                    updated_at_utc = $updatedAtUtc
                WHERE id = $id
                  AND status = $activeStatus;
                """;


            AddParameter(
                retire,
                "$supersededStatus",
                (int)NIRAMemoryStatus.Superseded);


            AddParameter(
                retire,
                "$updatedAtUtc",
                ToUnixMilliseconds(
                    now));


            AddParameter(
                retire,
                "$id",
                previousMemoryId.ToString("D"));


            AddParameter(
                retire,
                "$activeStatus",
                (int)NIRAMemoryStatus.Active);


            int affected =
                await retire.ExecuteNonQueryAsync(
                    cancellationToken);


            if (affected !=
                1)
            {
                throw new InvalidOperationException(
                    "The memory being superseded is no longer active.");
            }
        }


        // =====================================================
        // 2. Insert replacement.
        // =====================================================

        await InsertMemoryAsync(
            connection,
            transaction,
            normalized,
            replacementEmbedding,
            cancellationToken);


        // =====================================================
        // 3. Connect history after the replacement exists.
        // =====================================================

        await using (
            SqliteCommand link =
                connection.CreateCommand())
        {
            link.Transaction =
                transaction;


            link.CommandText =
                """
                UPDATE memories
                SET superseded_by_memory_id = $replacementId
                WHERE id = $previousId;
                """;


            AddParameter(
                link,
                "$replacementId",
                normalized.Id.ToString("D"));


            AddParameter(
                link,
                "$previousId",
                previousMemoryId.ToString("D"));


            await link.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await InsertEvidenceAsync(
            connection,
            transaction,
            normalized.Id,
            evidence.Normalize(),
            now,
            cancellationToken);


        await transaction.CommitAsync(
            cancellationToken);
    }


    // =========================================================
    // READ ACTIVE
    // =========================================================

    internal async Task<
        IReadOnlyList<NIRAStoredMemory>>
        ReadActiveAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(
            cancellationToken);


        List<NIRAStoredMemory> memories =
            new();


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
            BuildSelectMemorySql(
                "WHERE status = $status " +
                "ORDER BY updated_at_utc DESC");


        AddParameter(
            command,
            "$status",
            (int)NIRAMemoryStatus.Active);


        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);


        while (await reader.ReadAsync(
                   cancellationToken))
        {
            memories.Add(
                ReadStoredMemory(
                    reader));
        }


        return memories;
    }


    // =========================================================
    // READ ACTIVE BY CANONICAL KEY
    // =========================================================

    internal async Task<NIRAStoredMemory?>
        ReadActiveByCanonicalKeyAsync(
            string canonicalKey,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                canonicalKey))
        {
            return null;
        }


        await InitializeAsync(
            cancellationToken);


        string normalizedKey =
            canonicalKey
                .Trim()
                .ToLowerInvariant();


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
            BuildSelectMemorySql(
                "WHERE status = $status " +
                "AND canonical_key = $canonicalKey " +
                "LIMIT 1");


        AddParameter(
            command,
            "$status",
            (int)NIRAMemoryStatus.Active);


        AddParameter(
            command,
            "$canonicalKey",
            normalizedKey);


        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);


        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }


        return ReadStoredMemory(
            reader);
    }


    // =========================================================
    // ARCHIVE ACTIVE BY CANONICAL KEY
    //
    // Used by authoritative subsystem mirrors when a previously
    // current proposition is no longer authoritative.
    //
    // Archiving preserves history while preventing stale state
    // from participating in normal active-memory recall.
    // =========================================================

    internal async Task<bool> ArchiveActiveByCanonicalKeyAsync(
        string canonicalKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                canonicalKey))
        {
            return false;
        }


        await InitializeAsync(
            cancellationToken);


        string normalizedKey =
            canonicalKey
                .Trim()
                .ToLowerInvariant();


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
            UPDATE memories
            SET
                status = $archivedStatus,
                updated_at_utc = $updatedAtUtc
            WHERE status = $activeStatus
              AND canonical_key = $canonicalKey;
            """;


        AddParameter(
            command,
            "$archivedStatus",
            (int)NIRAMemoryStatus.Archived);


        AddParameter(
            command,
            "$updatedAtUtc",
            ToUnixMilliseconds(
                DateTimeOffset.UtcNow));


        AddParameter(
            command,
            "$activeStatus",
            (int)NIRAMemoryStatus.Active);


        AddParameter(
            command,
            "$canonicalKey",
            normalizedKey);


        int affected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);


        return affected >
            0;
    }


    // =========================================================
    // ARCHIVE ACTIVE BY ID
    //
    // Used by conservative maintenance for records whose exact
    // identity is already known. History remains in SQLite.
    // =========================================================

    internal async Task<bool> ArchiveActiveByIdAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        if (memoryId ==
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
            UPDATE memories
            SET
                status = $archivedStatus,
                updated_at_utc = $updatedAtUtc
            WHERE id = $id
              AND status = $activeStatus;
            """;


        AddParameter(
            command,
            "$archivedStatus",
            (int)NIRAMemoryStatus.Archived);


        AddParameter(
            command,
            "$updatedAtUtc",
            ToUnixMilliseconds(
                DateTimeOffset.UtcNow));


        AddParameter(
            command,
            "$id",
            memoryId.ToString(
                "D"));


        AddParameter(
            command,
            "$activeStatus",
            (int)NIRAMemoryStatus.Active);


        int affected =
            await command.ExecuteNonQueryAsync(
                cancellationToken);


        return affected ==
            1;
    }


    // =========================================================
    // MERGE EXACT DUPLICATE
    //
    // This operation does not decide whether two memories are
    // equivalent. NIRAMemoryMaintenanceService only calls it
    // after exact normalized-content + structural compatibility
    // checks.
    //
    // Keeper receives the duplicate's evidence/use counters and
    // strongest weights. Duplicate becomes Archived, preserving
    // historical traceability.
    // =========================================================

    internal async Task<bool> MergeExactDuplicateAsync(
        Guid keeperMemoryId,
        Guid duplicateMemoryId,
        CancellationToken cancellationToken = default)
    {
        if (
            keeperMemoryId ==
                Guid.Empty
            ||
            duplicateMemoryId ==
                Guid.Empty
            ||
            keeperMemoryId ==
                duplicateMemoryId)
        {
            return false;
        }


        await InitializeAsync(
            cancellationToken);


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


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


        int keeperUpdated;


        await using (
            SqliteCommand update =
                connection.CreateCommand())
        {
            update.Transaction =
                transaction;


            update.CommandText =
                """
                UPDATE memories
                SET
                    importance = MAX
                    (
                        importance,
                        (
                            SELECT importance
                            FROM memories
                            WHERE id = $duplicateId
                              AND status = $activeStatus
                        )
                    ),
                    confidence = MAX
                    (
                        confidence,
                        (
                            SELECT confidence
                            FROM memories
                            WHERE id = $duplicateId
                              AND status = $activeStatus
                        )
                    ),
                    emotional_weight = MAX
                    (
                        emotional_weight,
                        (
                            SELECT emotional_weight
                            FROM memories
                            WHERE id = $duplicateId
                              AND status = $activeStatus
                        )
                    ),
                    reinforcement_count = reinforcement_count + MAX
                    (
                        1,
                        (
                            SELECT reinforcement_count
                            FROM memories
                            WHERE id = $duplicateId
                              AND status = $activeStatus
                        )
                    ),
                    recall_count = recall_count + COALESCE
                    (
                        (
                            SELECT recall_count
                            FROM memories
                            WHERE id = $duplicateId
                              AND status = $activeStatus
                        ),
                        0
                    ),
                    last_recalled_at_utc = CASE
                        WHEN last_recalled_at_utc IS NULL THEN
                            (
                                SELECT last_recalled_at_utc
                                FROM memories
                                WHERE id = $duplicateId
                                  AND status = $activeStatus
                            )
                        WHEN
                            (
                                SELECT last_recalled_at_utc
                                FROM memories
                                WHERE id = $duplicateId
                                  AND status = $activeStatus
                            ) IS NULL
                        THEN last_recalled_at_utc
                        ELSE MAX
                        (
                            last_recalled_at_utc,
                            (
                                SELECT last_recalled_at_utc
                                FROM memories
                                WHERE id = $duplicateId
                                  AND status = $activeStatus
                            )
                        )
                    END,
                    updated_at_utc = $updatedAtUtc
                WHERE id = $keeperId
                  AND status = $activeStatus
                  AND EXISTS
                  (
                      SELECT 1
                      FROM memories
                      WHERE id = $duplicateId
                        AND status = $activeStatus
                  );
                """;


            AddParameter(
                update,
                "$keeperId",
                keeperMemoryId.ToString(
                    "D"));


            AddParameter(
                update,
                "$duplicateId",
                duplicateMemoryId.ToString(
                    "D"));


            AddParameter(
                update,
                "$activeStatus",
                (int)NIRAMemoryStatus.Active);


            AddParameter(
                update,
                "$updatedAtUtc",
                ToUnixMilliseconds(
                    now));


            keeperUpdated =
                await update.ExecuteNonQueryAsync(
                    cancellationToken);
        }


        if (keeperUpdated !=
            1)
        {
            await transaction.RollbackAsync(
                cancellationToken);


            return false;
        }


        // Preserve distinct evidence. The existing unique event
        // index prevents the same source event from being copied
        // twice to the keeper.
        await using (
            SqliteCommand evidence =
                connection.CreateCommand())
        {
            evidence.Transaction =
                transaction;


            evidence.CommandText =
                """
                INSERT OR IGNORE INTO memory_evidence
                (
                    id,
                    memory_id,
                    source_type,
                    source_event_id,
                    source_event_sequence,
                    source_timestamp_utc,
                    source_excerpt,
                    recorded_at_utc
                )
                SELECT
                    lower(hex(randomblob(16))),
                    $keeperId,
                    source_type,
                    source_event_id,
                    source_event_sequence,
                    source_timestamp_utc,
                    source_excerpt,
                    recorded_at_utc
                FROM memory_evidence
                WHERE memory_id = $duplicateId;
                """;


            AddParameter(
                evidence,
                "$keeperId",
                keeperMemoryId.ToString(
                    "D"));


            AddParameter(
                evidence,
                "$duplicateId",
                duplicateMemoryId.ToString(
                    "D"));


            await evidence.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await using (
            SqliteCommand archive =
                connection.CreateCommand())
        {
            archive.Transaction =
                transaction;


            archive.CommandText =
                """
                UPDATE memories
                SET
                    status = $archivedStatus,
                    updated_at_utc = $updatedAtUtc
                WHERE id = $duplicateId
                  AND status = $activeStatus;
                """;


            AddParameter(
                archive,
                "$archivedStatus",
                (int)NIRAMemoryStatus.Archived);


            AddParameter(
                archive,
                "$updatedAtUtc",
                ToUnixMilliseconds(
                    now));


            AddParameter(
                archive,
                "$duplicateId",
                duplicateMemoryId.ToString(
                    "D"));


            AddParameter(
                archive,
                "$activeStatus",
                (int)NIRAMemoryStatus.Active);


            int archived =
                await archive.ExecuteNonQueryAsync(
                    cancellationToken);


            if (archived !=
                1)
            {
                await transaction.RollbackAsync(
                    cancellationToken);


                return false;
            }
        }


        await transaction.CommitAsync(
            cancellationToken);


        return true;
    }


    // =========================================================
    // RECORD RECALLS
    // =========================================================

    internal async Task RecordRecallsAsync(
        IReadOnlyCollection<Guid> memoryIds,
        DateTimeOffset recalledAt,
        CancellationToken cancellationToken = default)
    {
        if (memoryIds.Count ==
            0)
        {
            return;
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


        await using SqliteTransaction transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);


        foreach (Guid memoryId
                 in memoryIds)
        {
            await using SqliteCommand command =
                connection.CreateCommand();


            command.Transaction =
                transaction;


            command.CommandText =
                """
                UPDATE memories
                SET
                    recall_count = recall_count + 1,
                    last_recalled_at_utc = $recalledAtUtc
                WHERE id = $id
                  AND status = $status;
                """;


            AddParameter(
                command,
                "$recalledAtUtc",
                ToUnixMilliseconds(
                    recalledAt));


            AddParameter(
                command,
                "$id",
                memoryId.ToString("D"));


            AddParameter(
                command,
                "$status",
                (int)NIRAMemoryStatus.Active);


            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await transaction.CommitAsync(
            cancellationToken);
    }


    // =========================================================
    // COUNT
    // =========================================================

    public async Task<long> CountActiveAsync(
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
            SELECT COUNT(*)
            FROM memories
            WHERE status = $status;
            """;


        AddParameter(
            command,
            "$status",
            (int)NIRAMemoryStatus.Active);


        object? result =
            await command.ExecuteScalarAsync(
                cancellationToken);


        return Convert.ToInt64(
            result ?? 0L);
    }


    // =========================================================
    // INSERT MEMORY HELPER
    // =========================================================

    private static async Task InsertMemoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NIRAMemoryRecord memory,
        SemanticEmbedding embedding,
        CancellationToken cancellationToken)
    {
        byte[] embeddingBytes =
            SerializeEmbedding(
                embedding);


        await using SqliteCommand command =
            connection.CreateCommand();


        command.Transaction =
            transaction;


        command.CommandText =
            """
            INSERT INTO memories
            (
                id,
                kind,
                content,
                canonical_key,
                topic_key,
                importance,
                confidence,
                emotional_weight,
                status,
                superseded_by_memory_id,
                created_at_utc,
                updated_at_utc,
                last_recalled_at_utc,
                reinforcement_count,
                recall_count,
                source_type,
                source_event_id,
                source_event_sequence,
                source_timestamp_utc,
                source_excerpt,
                embedding_dimension,
                embedding
            )
            VALUES
            (
                $id,
                $kind,
                $content,
                $canonicalKey,
                $topicKey,
                $importance,
                $confidence,
                $emotionalWeight,
                $status,
                $supersededByMemoryId,
                $createdAtUtc,
                $updatedAtUtc,
                $lastRecalledAtUtc,
                $reinforcementCount,
                $recallCount,
                $sourceType,
                $sourceEventId,
                $sourceEventSequence,
                $sourceTimestampUtc,
                $sourceExcerpt,
                $embeddingDimension,
                $embedding
            );
            """;


        AddParameter(
            command,
            "$id",
            memory.Id.ToString("D"));


        AddParameter(
            command,
            "$kind",
            (int)memory.Kind);


        AddParameter(
            command,
            "$content",
            memory.Content);


        AddParameter(
            command,
            "$canonicalKey",
            memory.CanonicalKey);


        AddParameter(
            command,
            "$topicKey",
            memory.TopicKey);


        AddParameter(
            command,
            "$importance",
            memory.Importance);


        AddParameter(
            command,
            "$confidence",
            memory.Confidence);


        AddParameter(
            command,
            "$emotionalWeight",
            memory.EmotionalWeight);


        AddParameter(
            command,
            "$status",
            (int)memory.Status);


        AddParameter(
            command,
            "$supersededByMemoryId",
            memory.SupersededByMemoryId?
                .ToString("D"));


        AddParameter(
            command,
            "$createdAtUtc",
            ToUnixMilliseconds(
                memory.CreatedAt));


        AddParameter(
            command,
            "$updatedAtUtc",
            ToUnixMilliseconds(
                memory.UpdatedAt));


        AddParameter(
            command,
            "$lastRecalledAtUtc",
            memory.LastRecalledAt.HasValue
                ? ToUnixMilliseconds(
                    memory.LastRecalledAt.Value)
                : null);


        AddParameter(
            command,
            "$reinforcementCount",
            memory.ReinforcementCount);


        AddParameter(
            command,
            "$recallCount",
            memory.RecallCount);


        AddParameter(
            command,
            "$sourceType",
            (int)memory
                .Provenance
                .SourceType);


        AddParameter(
            command,
            "$sourceEventId",
            memory
                .Provenance
                .SourceEventId?
                .ToString("D"));


        AddParameter(
            command,
            "$sourceEventSequence",
            memory
                .Provenance
                .SourceEventSequence);


        AddParameter(
            command,
            "$sourceTimestampUtc",
            memory
                .Provenance
                .SourceTimestamp
                .HasValue
                    ? ToUnixMilliseconds(
                        memory
                            .Provenance
                            .SourceTimestamp!
                            .Value)
                    : null);


        AddParameter(
            command,
            "$sourceExcerpt",
            memory
                .Provenance
                .SourceExcerpt);


        AddParameter(
            command,
            "$embeddingDimension",
            embedding.Dimension);


        SqliteParameter embeddingParameter =
            command.Parameters.Add(
                "$embedding",
                SqliteType.Blob);


        embeddingParameter.Value =
            embeddingBytes;


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // =========================================================
    // INSERT EVIDENCE HELPER
    // =========================================================

    private static async Task InsertEvidenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid memoryId,
        NIRAMemoryProvenance provenance,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        NIRAMemoryProvenance normalized =
            provenance.Normalize();


        await using SqliteCommand command =
            connection.CreateCommand();


        command.Transaction =
            transaction;


        command.CommandText =
            """
            INSERT OR IGNORE INTO memory_evidence
            (
                id,
                memory_id,
                source_type,
                source_event_id,
                source_event_sequence,
                source_timestamp_utc,
                source_excerpt,
                recorded_at_utc
            )
            VALUES
            (
                $id,
                $memoryId,
                $sourceType,
                $sourceEventId,
                $sourceEventSequence,
                $sourceTimestampUtc,
                $sourceExcerpt,
                $recordedAtUtc
            );
            """;


        AddParameter(
            command,
            "$id",
            Guid.NewGuid()
                .ToString("D"));


        AddParameter(
            command,
            "$memoryId",
            memoryId.ToString("D"));


        AddParameter(
            command,
            "$sourceType",
            (int)normalized.SourceType);


        AddParameter(
            command,
            "$sourceEventId",
            normalized.SourceEventId?
                .ToString("D"));


        AddParameter(
            command,
            "$sourceEventSequence",
            normalized.SourceEventSequence);


        AddParameter(
            command,
            "$sourceTimestampUtc",
            normalized.SourceTimestamp.HasValue
                ? ToUnixMilliseconds(
                    normalized.SourceTimestamp.Value)
                : null);


        AddParameter(
            command,
            "$sourceExcerpt",
            normalized.SourceExcerpt);


        AddParameter(
            command,
            "$recordedAtUtc",
            ToUnixMilliseconds(
                recordedAt));


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
            PRAGMA busy_timeout = 5000;
            """;


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // =========================================================
    // SCHEMA
    // =========================================================

    private static async Task CreateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            $$"""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS NIRA_memory_meta
            (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO NIRA_memory_meta
            (
                key,
                value
            )
            VALUES
            (
                'schema_version',
                '{{SchemaVersion}}'
            );

            CREATE TABLE IF NOT EXISTS memories
            (
                id TEXT PRIMARY KEY NOT NULL,
                kind INTEGER NOT NULL,
                content TEXT NOT NULL,
                canonical_key TEXT NULL,
                topic_key TEXT NULL,
                importance REAL NOT NULL,
                confidence REAL NOT NULL,
                emotional_weight REAL NOT NULL,
                status INTEGER NOT NULL,
                superseded_by_memory_id TEXT NULL,
                created_at_utc INTEGER NOT NULL,
                updated_at_utc INTEGER NOT NULL,
                last_recalled_at_utc INTEGER NULL,
                reinforcement_count INTEGER NOT NULL,
                recall_count INTEGER NOT NULL,
                source_type INTEGER NOT NULL,
                source_event_id TEXT NULL,
                source_event_sequence INTEGER NULL,
                source_timestamp_utc INTEGER NULL,
                source_excerpt TEXT NULL,
                embedding_dimension INTEGER NOT NULL,
                embedding BLOB NOT NULL,

                FOREIGN KEY (superseded_by_memory_id)
                    REFERENCES memories(id)
            );

            CREATE TABLE IF NOT EXISTS memory_evidence
            (
                id TEXT PRIMARY KEY NOT NULL,
                memory_id TEXT NOT NULL,
                source_type INTEGER NOT NULL,
                source_event_id TEXT NULL,
                source_event_sequence INTEGER NULL,
                source_timestamp_utc INTEGER NULL,
                source_excerpt TEXT NULL,
                recorded_at_utc INTEGER NOT NULL,

                FOREIGN KEY (memory_id)
                    REFERENCES memories(id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS
                idx_memories_status
                ON memories(status);

            CREATE INDEX IF NOT EXISTS
                idx_memories_kind_status
                ON memories(kind, status);

            CREATE INDEX IF NOT EXISTS
                idx_memories_canonical_key
                ON memories(canonical_key);

            CREATE UNIQUE INDEX IF NOT EXISTS
                ux_memories_active_canonical_key
                ON memories(canonical_key)
                WHERE status = 0
                  AND canonical_key IS NOT NULL;

            CREATE INDEX IF NOT EXISTS
                idx_memories_topic_key
                ON memories(topic_key);

            CREATE INDEX IF NOT EXISTS
                idx_memories_updated_at
                ON memories(updated_at_utc DESC);

            CREATE INDEX IF NOT EXISTS
                idx_memory_evidence_memory
                ON memory_evidence(memory_id, recorded_at_utc);

            CREATE UNIQUE INDEX IF NOT EXISTS
                ux_memory_evidence_event
                ON memory_evidence(memory_id, source_event_id)
                WHERE source_event_id IS NOT NULL;
            """;


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // =========================================================
    // MIGRATION
    // =========================================================

    private static async Task MigrateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        int storedVersion =
            await ReadSchemaVersionAsync(
                connection,
                cancellationToken);


        if (storedVersion >
            SchemaVersion)
        {
            throw new InvalidOperationException(
                $"NIRA long-term memory schema version {storedVersion} " +
                $"is newer than supported version {SchemaVersion}.");
        }


        // =====================================================
        // SEQUENTIAL MIGRATION PIPELINE
        //
        // Future schema versions are added as one explicit
        // version-to-version migration. We never jump across
        // unknown versions or rebuild user memory from scratch.
        // =====================================================

        while (storedVersion <
               SchemaVersion)
        {
            storedVersion =
                storedVersion switch
                {
                    1 =>
                        await MigrateV1ToV2Async(
                            connection,
                            cancellationToken),

                    _ =>
                        throw new InvalidOperationException(
                            $"No NIRA long-term-memory migration path exists " +
                            $"from schema version {storedVersion}.")
                };
        }
    }


    private static async Task<int> MigrateV1ToV2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);


        // =====================================================
        // v1 -> v2
        //
        // v1 stored only the first provenance on the memory row.
        // Backfill that provenance as the first evidence entry.
        // =====================================================

        await using (
            SqliteCommand evidence =
                connection.CreateCommand())
        {
            evidence.Transaction =
                transaction;


            evidence.CommandText =
                """
                INSERT OR IGNORE INTO memory_evidence
                (
                    id,
                    memory_id,
                    source_type,
                    source_event_id,
                    source_event_sequence,
                    source_timestamp_utc,
                    source_excerpt,
                    recorded_at_utc
                )
                SELECT
                    lower(hex(randomblob(16))),
                    id,
                    source_type,
                    source_event_id,
                    source_event_sequence,
                    source_timestamp_utc,
                    source_excerpt,
                    created_at_utc
                FROM memories;
                """;


            await evidence.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await using (
            SqliteCommand version =
                connection.CreateCommand())
        {
            version.Transaction =
                transaction;


            version.CommandText =
                """
                UPDATE NIRA_memory_meta
                SET value = $version
                WHERE key = 'schema_version';
                """;


            AddParameter(
                version,
                "$version",
                "2");


            await version.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await transaction.CommitAsync(
            cancellationToken);


        return 2;
    }


    // =========================================================
    // VALIDATE SCHEMA
    // =========================================================

    private static async Task ValidateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        int storedVersion =
            await ReadSchemaVersionAsync(
                connection,
                cancellationToken);


        if (storedVersion !=
            SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported NIRA long-term memory schema " +
                $"version {storedVersion}. Expected {SchemaVersion}.");
        }
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
            FROM NIRA_memory_meta
            WHERE key = 'schema_version';
            """;


        object? result =
            await command.ExecuteScalarAsync(
                cancellationToken);


        if (
            result ==
                null
            ||
            !int.TryParse(
                Convert.ToString(
                    result),
                out int storedVersion))
        {
            throw new InvalidOperationException(
                "NIRA long-term memory database has no valid schema version.");
        }


        return storedVersion;
    }


    // =========================================================
    // SELECT SQL
    // =========================================================

    private static string BuildSelectMemorySql(
        string suffix)
    {
        return
            """
            SELECT
                id,
                kind,
                content,
                canonical_key,
                topic_key,
                importance,
                confidence,
                emotional_weight,
                status,
                superseded_by_memory_id,
                created_at_utc,
                updated_at_utc,
                last_recalled_at_utc,
                reinforcement_count,
                recall_count,
                source_type,
                source_event_id,
                source_event_sequence,
                source_timestamp_utc,
                source_excerpt,
                embedding_dimension,
                embedding
            FROM memories
            """
            +
            Environment.NewLine
            +
            suffix
            +
            ";";
    }


    // =========================================================
    // READ RECORD
    // =========================================================

    private static NIRAStoredMemory ReadStoredMemory(
        SqliteDataReader reader)
    {
        Guid id =
            Guid.Parse(
                reader.GetString(
                    0));


        NIRAMemoryKind kind =
            (NIRAMemoryKind)
                reader.GetInt32(
                    1);


        string content =
            reader.GetString(
                2);


        string? canonicalKey =
            ReadNullableString(
                reader,
                3);


        string? topicKey =
            ReadNullableString(
                reader,
                4);


        double importance =
            reader.GetDouble(
                5);


        double confidence =
            reader.GetDouble(
                6);


        double emotionalWeight =
            reader.GetDouble(
                7);


        NIRAMemoryStatus status =
            (NIRAMemoryStatus)
                reader.GetInt32(
                    8);


        Guid? supersededByMemoryId =
            ReadNullableGuid(
                reader,
                9);


        DateTimeOffset createdAt =
            FromUnixMilliseconds(
                reader.GetInt64(
                    10));


        DateTimeOffset updatedAt =
            FromUnixMilliseconds(
                reader.GetInt64(
                    11));


        DateTimeOffset? lastRecalledAt =
            ReadNullableDateTimeOffset(
                reader,
                12);


        int reinforcementCount =
            reader.GetInt32(
                13);


        int recallCount =
            reader.GetInt32(
                14);


        NIRAMemorySourceType sourceType =
            (NIRAMemorySourceType)
                reader.GetInt32(
                    15);


        Guid? sourceEventId =
            ReadNullableGuid(
                reader,
                16);


        long? sourceEventSequence =
            ReadNullableInt64(
                reader,
                17);


        DateTimeOffset? sourceTimestamp =
            ReadNullableDateTimeOffset(
                reader,
                18);


        string? sourceExcerpt =
            ReadNullableString(
                reader,
                19);


        int embeddingDimension =
            reader.GetInt32(
                20);


        byte[] embeddingBytes =
            (byte[])reader.GetValue(
                21);


        SemanticEmbedding embedding =
            DeserializeEmbedding(
                embeddingBytes,
                embeddingDimension);


        NIRAMemoryRecord memory =
            new NIRAMemoryRecord
            {
                Id =
                    id,

                Kind =
                    kind,

                Content =
                    content,

                CanonicalKey =
                    canonicalKey,

                TopicKey =
                    topicKey,

                Importance =
                    importance,

                Confidence =
                    confidence,

                EmotionalWeight =
                    emotionalWeight,

                Status =
                    status,

                SupersededByMemoryId =
                    supersededByMemoryId,

                CreatedAt =
                    createdAt,

                UpdatedAt =
                    updatedAt,

                LastRecalledAt =
                    lastRecalledAt,

                ReinforcementCount =
                    reinforcementCount,

                RecallCount =
                    recallCount,

                Provenance =
                    new NIRAMemoryProvenance
                    {
                        SourceType =
                            sourceType,

                        SourceEventId =
                            sourceEventId,

                        SourceEventSequence =
                            sourceEventSequence,

                        SourceTimestamp =
                            sourceTimestamp,

                        SourceExcerpt =
                            sourceExcerpt
                    }
            }
            .Normalize();


        return new NIRAStoredMemory(
            memory,
            embedding);
    }


    // =========================================================
    // EMBEDDING SERIALIZATION
    // =========================================================

    private static byte[] SerializeEmbedding(
        SemanticEmbedding embedding)
    {
        float[] values =
            embedding
                .Values
                .ToArray();


        byte[] bytes =
            new byte[
                values.Length *
                sizeof(float)];


        Buffer.BlockCopy(
            values,
            0,
            bytes,
            0,
            bytes.Length);


        return bytes;
    }


    private static SemanticEmbedding DeserializeEmbedding(
        byte[] bytes,
        int dimension)
    {
        if (dimension <=
            0)
        {
            throw new InvalidOperationException(
                "Stored semantic embedding dimension is invalid.");
        }


        int expectedLength =
            dimension *
            sizeof(float);


        if (bytes.Length !=
            expectedLength)
        {
            throw new InvalidOperationException(
                "Stored semantic embedding size does not match its dimension.");
        }


        float[] values =
            new float[
                dimension];


        Buffer.BlockCopy(
            bytes,
            0,
            values,
            0,
            bytes.Length);


        return new SemanticEmbedding(
            values);
    }


    // =========================================================
    // SQLITE HELPERS
    // =========================================================

    private static void AddParameter(
        SqliteCommand command,
        string name,
        object? value)
    {
        command.Parameters.AddWithValue(
            name,
            value ?? DBNull.Value);
    }


    private static string? ReadNullableString(
        SqliteDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(
                ordinal)
            ? null
            : reader.GetString(
                ordinal);
    }


    private static Guid? ReadNullableGuid(
        SqliteDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(
                ordinal))
        {
            return null;
        }


        string value =
            reader.GetString(
                ordinal);


        return Guid.TryParse(
                value,
                out Guid parsed)
            ? parsed
            : null;
    }


    private static long? ReadNullableInt64(
        SqliteDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(
                ordinal)
            ? null
            : reader.GetInt64(
                ordinal);
    }


    private static DateTimeOffset?
        ReadNullableDateTimeOffset(
            SqliteDataReader reader,
            int ordinal)
    {
        if (reader.IsDBNull(
                ordinal))
        {
            return null;
        }


        return FromUnixMilliseconds(
            reader.GetInt64(
                ordinal));
    }


    private static long ToUnixMilliseconds(
        DateTimeOffset value)
    {
        return value
            .ToUniversalTime()
            .ToUnixTimeMilliseconds();
    }


    private static DateTimeOffset FromUnixMilliseconds(
        long value)
    {
        return DateTimeOffset
            .FromUnixTimeMilliseconds(
                value);
    }
}


// =============================================================
// STORED MEMORY
//
// Internal retrieval/consolidation representation. Embeddings
// never leave long-term-memory infrastructure.
// =============================================================

internal sealed record NIRAStoredMemory(
    NIRAMemoryRecord Memory,
    SemanticEmbedding Embedding);
