/*
 * filename: SegaMemoryAssociativeIndexStore.cs
 */

using Microsoft.Data.Sqlite;

using SegaAgent.Semantic;

namespace SegaAgent.Memory.LongTerm;

// =============================================================
// ASSOCIATIVE INDEX STORE
//
// This store is deliberately separate from the authoritative
// memory row/evidence lifecycle.
//
// If associative indexing ever fails, the durable memory itself
// remains valid and can be re-indexed later.
// =============================================================

public sealed class SegaMemoryAssociativeIndexStore
{
    private const int SchemaVersion =
        1;


    private readonly SegaLongTermMemoryStore
        _memoryStore;


    private readonly string
        _connectionString;


    private readonly SemaphoreSlim
        _initializationLock =
            new(
                1,
                1);


    private bool
        _initialized;


    public SegaMemoryAssociativeIndexStore(
        SegaLongTermMemoryStore memoryStore)
    {
        _memoryStore =
            memoryStore
            ?? throw new ArgumentNullException(
                nameof(memoryStore));


        _connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _memoryStore.DatabasePath,

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


            await _memoryStore.InitializeAsync(
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
                $$"""
                CREATE TABLE IF NOT EXISTS sega_memory_association_meta
                (
                    key TEXT PRIMARY KEY NOT NULL,
                    value TEXT NOT NULL
                );

                INSERT OR IGNORE INTO sega_memory_association_meta
                (
                    key,
                    value
                )
                VALUES
                (
                    'schema_version',
                    '{{SchemaVersion}}'
                );

                CREATE TABLE IF NOT EXISTS memory_associative_profiles
                (
                    memory_id TEXT PRIMARY KEY NOT NULL,
                    retrieval_description TEXT NULL,
                    retrieval_embedding_dimension INTEGER NOT NULL,
                    retrieval_embedding BLOB NOT NULL,
                    indexed_at_utc INTEGER NOT NULL,

                    FOREIGN KEY (memory_id)
                        REFERENCES memories(id)
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS memory_associative_terms
                (
                    memory_id TEXT NOT NULL,
                    term_kind INTEGER NOT NULL,
                    value TEXT NOT NULL,
                    normalized_value TEXT NOT NULL,
                    ordinal INTEGER NOT NULL,

                    PRIMARY KEY
                    (
                        memory_id,
                        term_kind,
                        normalized_value
                    ),

                    FOREIGN KEY (memory_id)
                        REFERENCES memories(id)
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS memory_associative_links
                (
                    source_memory_id TEXT NOT NULL,
                    target_memory_id TEXT NOT NULL,
                    relation_kind INTEGER NOT NULL,
                    shared_value TEXT NOT NULL,
                    strength REAL NOT NULL,
                    updated_at_utc INTEGER NOT NULL,

                    PRIMARY KEY
                    (
                        source_memory_id,
                        target_memory_id,
                        relation_kind,
                        shared_value
                    ),

                    FOREIGN KEY (source_memory_id)
                        REFERENCES memories(id)
                        ON DELETE CASCADE,

                    FOREIGN KEY (target_memory_id)
                        REFERENCES memories(id)
                        ON DELETE CASCADE,

                    CHECK (source_memory_id <> target_memory_id)
                );

                CREATE INDEX IF NOT EXISTS
                    idx_memory_associative_terms_value
                    ON memory_associative_terms
                    (
                        term_kind,
                        normalized_value
                    );

                CREATE INDEX IF NOT EXISTS
                    idx_memory_associative_links_source
                    ON memory_associative_links
                    (
                        source_memory_id,
                        strength DESC
                    );

                CREATE INDEX IF NOT EXISTS
                    idx_memory_associative_links_target
                    ON memory_associative_links
                    (
                        target_memory_id,
                        strength DESC
                    );
                """;


            await command.ExecuteNonQueryAsync(
                cancellationToken);


            await ValidateVersionAsync(
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


    internal async Task<SegaStoredMemoryAssociationProfile?>
        ReadProfileAsync(
            Guid memoryId,
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


        return await ReadProfileAsync(
            connection,
            memoryId,
            cancellationToken);
    }


    internal async Task<IReadOnlyDictionary<Guid, SegaStoredMemoryAssociationProfile>>
        ReadActiveProfilesAsync(
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


        Dictionary<Guid, ProfileBuilder> builders =
            new();


        await using (
            SqliteCommand profiles =
                connection.CreateCommand())
        {
            profiles.CommandText =
                """
                SELECT
                    p.memory_id,
                    p.retrieval_description,
                    p.retrieval_embedding_dimension,
                    p.retrieval_embedding
                FROM memory_associative_profiles p
                INNER JOIN memories m
                    ON m.id = p.memory_id
                WHERE m.status = $activeStatus;
                """;


            AddParameter(
                profiles,
                "$activeStatus",
                (int)SegaMemoryStatus.Active);


            await using SqliteDataReader reader =
                await profiles.ExecuteReaderAsync(
                    cancellationToken);


            while (await reader.ReadAsync(
                       cancellationToken))
            {
                Guid memoryId =
                    Guid.Parse(
                        reader.GetString(
                            0));


                string? description =
                    reader.IsDBNull(
                        1)
                        ? null
                        : reader.GetString(
                            1);


                int dimension =
                    reader.GetInt32(
                        2);


                byte[] bytes =
                    (byte[])reader.GetValue(
                        3);


                builders[memoryId] =
                    new ProfileBuilder
                    {
                        MemoryId =
                            memoryId,

                        RetrievalDescription =
                            description,

                        RetrievalEmbedding =
                            DeserializeEmbedding(
                                bytes,
                                dimension)
                    };
            }
        }


        if (builders.Count ==
            0)
        {
            return new Dictionary<Guid, SegaStoredMemoryAssociationProfile>();
        }


        await using (
            SqliteCommand terms =
                connection.CreateCommand())
        {
            terms.CommandText =
                """
                SELECT
                    t.memory_id,
                    t.term_kind,
                    t.value,
                    t.ordinal
                FROM memory_associative_terms t
                INNER JOIN memories m
                    ON m.id = t.memory_id
                WHERE m.status = $activeStatus
                ORDER BY
                    t.memory_id,
                    t.term_kind,
                    t.ordinal;
                """;


            AddParameter(
                terms,
                "$activeStatus",
                (int)SegaMemoryStatus.Active);


            await using SqliteDataReader reader =
                await terms.ExecuteReaderAsync(
                    cancellationToken);


            while (await reader.ReadAsync(
                       cancellationToken))
            {
                Guid memoryId =
                    Guid.Parse(
                        reader.GetString(
                            0));


                if (!builders.TryGetValue(
                        memoryId,
                        out ProfileBuilder? builder))
                {
                    continue;
                }


                SegaMemoryAssociationTermKind kind =
                    (SegaMemoryAssociationTermKind)
                    reader.GetInt32(
                        1);


                string value =
                    reader.GetString(
                        2);


                builder.Add(
                    kind,
                    value);
            }
        }


        return builders.ToDictionary(
            pair =>
                pair.Key,
            pair =>
                pair.Value.Build());
    }


    internal async Task UpsertProfileAsync(
        Guid memoryId,
        SegaMemoryAssociationProfile profile,
        SemanticEmbedding retrievalEmbedding,
        CancellationToken cancellationToken = default)
    {
        if (memoryId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Memory ID cannot be empty.",
                nameof(memoryId));
        }


        ArgumentNullException.ThrowIfNull(
            profile);


        ArgumentNullException.ThrowIfNull(
            retrievalEmbedding);


        await InitializeAsync(
            cancellationToken);


        SegaMemoryAssociationProfile normalized =
            profile.Normalize();


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
            SqliteCommand profileCommand =
                connection.CreateCommand())
        {
            profileCommand.Transaction =
                transaction;


            profileCommand.CommandText =
                """
                INSERT INTO memory_associative_profiles
                (
                    memory_id,
                    retrieval_description,
                    retrieval_embedding_dimension,
                    retrieval_embedding,
                    indexed_at_utc
                )
                VALUES
                (
                    $memoryId,
                    $description,
                    $dimension,
                    $embedding,
                    $indexedAtUtc
                )
                ON CONFLICT(memory_id)
                DO UPDATE SET
                    retrieval_description = excluded.retrieval_description,
                    retrieval_embedding_dimension = excluded.retrieval_embedding_dimension,
                    retrieval_embedding = excluded.retrieval_embedding,
                    indexed_at_utc = excluded.indexed_at_utc;
                """;


            AddParameter(
                profileCommand,
                "$memoryId",
                memoryId.ToString(
                    "D"));


            AddParameter(
                profileCommand,
                "$description",
                normalized.RetrievalDescription);


            AddParameter(
                profileCommand,
                "$dimension",
                retrievalEmbedding.Dimension);


            SqliteParameter embeddingParameter =
                profileCommand.Parameters.Add(
                    "$embedding",
                    SqliteType.Blob);


            embeddingParameter.Value =
                SerializeEmbedding(
                    retrievalEmbedding);


            AddParameter(
                profileCommand,
                "$indexedAtUtc",
                ToUnixMilliseconds(
                    DateTimeOffset.UtcNow));


            await profileCommand.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await using (
            SqliteCommand deleteTerms =
                connection.CreateCommand())
        {
            deleteTerms.Transaction =
                transaction;


            deleteTerms.CommandText =
                """
                DELETE FROM memory_associative_terms
                WHERE memory_id = $memoryId;
                """;


            AddParameter(
                deleteTerms,
                "$memoryId",
                memoryId.ToString(
                    "D"));


            await deleteTerms.ExecuteNonQueryAsync(
                cancellationToken);
        }


        await InsertTermsAsync(
            connection,
            transaction,
            memoryId,
            SegaMemoryAssociationTermKind.RetrievalCue,
            normalized.RetrievalCues,
            cancellationToken);


        await InsertTermsAsync(
            connection,
            transaction,
            memoryId,
            SegaMemoryAssociationTermKind.Concept,
            normalized.Concepts,
            cancellationToken);


        await InsertTermsAsync(
            connection,
            transaction,
            memoryId,
            SegaMemoryAssociationTermKind.Entity,
            normalized.Entities,
            cancellationToken);


        await transaction.CommitAsync(
            cancellationToken);
    }


    internal async Task ReplaceLinksForMemoryAsync(
        Guid memoryId,
        IReadOnlyList<SegaMemoryAssociationLink> links,
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


        await using SqliteTransaction transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);


        await DeleteLinksForMemoryAsync(
            connection,
            transaction,
            memoryId,
            cancellationToken);


        foreach (
            SegaMemoryAssociationLink link
            in links)
        {
            if (
                link.SourceMemoryId ==
                    Guid.Empty
                ||
                link.TargetMemoryId ==
                    Guid.Empty
                ||
                link.SourceMemoryId ==
                    link.TargetMemoryId
                ||
                string.IsNullOrWhiteSpace(
                    link.SharedValue))
            {
                continue;
            }


            await InsertLinkAsync(
                connection,
                transaction,
                link,
                cancellationToken);
        }


        await transaction.CommitAsync(
            cancellationToken);
    }


    // =========================================================
    // PRUNE INACTIVE DERIVED STATE
    //
    // Superseded/archived memories remain in authoritative
    // history, but their retrieval profiles/terms/links should not
    // remain live index state.
    // =========================================================

    internal async Task<int> PruneInactiveAsync(
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


        await using SqliteTransaction transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);


        int changed =
            0;


        string inactiveSubquery =
            "SELECT id FROM memories WHERE status <> $activeStatus";


        await using (
            SqliteCommand links =
                connection.CreateCommand())
        {
            links.Transaction =
                transaction;


            links.CommandText =
                $"""
                DELETE FROM memory_associative_links
                WHERE source_memory_id IN ({inactiveSubquery})
                   OR target_memory_id IN ({inactiveSubquery});
                """;


            AddParameter(
                links,
                "$activeStatus",
                (int)SegaMemoryStatus.Active);


            changed +=
                await links.ExecuteNonQueryAsync(
                    cancellationToken);
        }


        await using (
            SqliteCommand terms =
                connection.CreateCommand())
        {
            terms.Transaction =
                transaction;


            terms.CommandText =
                $"""
                DELETE FROM memory_associative_terms
                WHERE memory_id IN ({inactiveSubquery});
                """;


            AddParameter(
                terms,
                "$activeStatus",
                (int)SegaMemoryStatus.Active);


            changed +=
                await terms.ExecuteNonQueryAsync(
                    cancellationToken);
        }


        await using (
            SqliteCommand profiles =
                connection.CreateCommand())
        {
            profiles.Transaction =
                transaction;


            profiles.CommandText =
                $"""
                DELETE FROM memory_associative_profiles
                WHERE memory_id IN ({inactiveSubquery});
                """;


            AddParameter(
                profiles,
                "$activeStatus",
                (int)SegaMemoryStatus.Active);


            changed +=
                await profiles.ExecuteNonQueryAsync(
                    cancellationToken);
        }


        await transaction.CommitAsync(
            cancellationToken);


        return changed;
    }


    internal async Task<IReadOnlyList<SegaMemoryAssociationLink>>
        ReadRelatedAsync(
            IReadOnlyCollection<Guid> seedMemoryIds,
            int maximumResults,
            CancellationToken cancellationToken = default)
    {
        if (
            seedMemoryIds ==
                null
            ||
            seedMemoryIds.Count ==
                0)
        {
            return Array.Empty<SegaMemoryAssociationLink>();
        }


        maximumResults =
            Math.Clamp(
                maximumResults,
                1,
                60);


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


        List<string> parameterNames =
            new();


        int parameterIndex =
            0;


        foreach (Guid memoryId
                 in seedMemoryIds
                     .Where(
                         id =>
                             id != Guid.Empty)
                     .Distinct())
        {
            string parameterName =
                $"$seed{parameterIndex++}";


            parameterNames.Add(
                parameterName);


            AddParameter(
                command,
                parameterName,
                memoryId.ToString(
                    "D"));
        }


        if (parameterNames.Count ==
            0)
        {
            return Array.Empty<SegaMemoryAssociationLink>();
        }


        command.CommandText =
            $"""
            SELECT
                l.source_memory_id,
                l.target_memory_id,
                l.relation_kind,
                l.shared_value,
                l.strength
            FROM memory_associative_links l
            INNER JOIN memories target
                ON target.id = l.target_memory_id
            WHERE l.source_memory_id IN
                ({string.Join(", ", parameterNames)})
              AND target.status = $activeStatus
            ORDER BY
                l.strength DESC,
                target.updated_at_utc DESC
            LIMIT $maximumResults;
            """;


        AddParameter(
            command,
            "$activeStatus",
            (int)SegaMemoryStatus.Active);


        AddParameter(
            command,
            "$maximumResults",
            maximumResults);


        List<SegaMemoryAssociationLink> result =
            new();


        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(
                cancellationToken);


        while (await reader.ReadAsync(
                   cancellationToken))
        {
            result.Add(
                new SegaMemoryAssociationLink
                {
                    SourceMemoryId =
                        Guid.Parse(
                            reader.GetString(
                                0)),

                    TargetMemoryId =
                        Guid.Parse(
                            reader.GetString(
                                1)),

                    RelationKind =
                        (SegaMemoryRelationKind)
                        reader.GetInt32(
                            2),

                    SharedValue =
                        reader.GetString(
                            3),

                    Strength =
                        Math.Clamp(
                            reader.GetDouble(
                                4),
                            0.0,
                            1.0)
                });
        }


        return result;
    }


    internal async Task DeleteLinksForMemoryAsync(
        Guid memoryId,
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


        await using SqliteTransaction transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);


        await DeleteLinksForMemoryAsync(
            connection,
            transaction,
            memoryId,
            cancellationToken);


        await transaction.CommitAsync(
            cancellationToken);
    }


    private async Task<SegaStoredMemoryAssociationProfile?>
        ReadProfileAsync(
            SqliteConnection connection,
            Guid memoryId,
            CancellationToken cancellationToken)
    {
        await using SqliteCommand profileCommand =
            connection.CreateCommand();


        profileCommand.CommandText =
            """
            SELECT
                retrieval_description,
                retrieval_embedding_dimension,
                retrieval_embedding
            FROM memory_associative_profiles
            WHERE memory_id = $memoryId
            LIMIT 1;
            """;


        AddParameter(
            profileCommand,
            "$memoryId",
            memoryId.ToString(
                "D"));


        string? description;
        int dimension;
        byte[] bytes;


        await using (
            SqliteDataReader reader =
                await profileCommand.ExecuteReaderAsync(
                    cancellationToken))
        {
            if (!await reader.ReadAsync(
                    cancellationToken))
            {
                return null;
            }


            description =
                reader.IsDBNull(
                    0)
                    ? null
                    : reader.GetString(
                        0);


            dimension =
                reader.GetInt32(
                    1);


            bytes =
                (byte[])reader.GetValue(
                    2);
        }


        List<string> cues =
            new();


        List<string> concepts =
            new();


        List<string> entities =
            new();


        await using SqliteCommand terms =
            connection.CreateCommand();


        terms.CommandText =
            """
            SELECT
                term_kind,
                value
            FROM memory_associative_terms
            WHERE memory_id = $memoryId
            ORDER BY
                term_kind,
                ordinal;
            """;


        AddParameter(
            terms,
            "$memoryId",
            memoryId.ToString(
                "D"));


        await using SqliteDataReader termReader =
            await terms.ExecuteReaderAsync(
                cancellationToken);


        while (await termReader.ReadAsync(
                   cancellationToken))
        {
            SegaMemoryAssociationTermKind kind =
                (SegaMemoryAssociationTermKind)
                termReader.GetInt32(
                    0);


            string value =
                termReader.GetString(
                    1);


            switch (kind)
            {
                case SegaMemoryAssociationTermKind.RetrievalCue:
                    cues.Add(
                        value);
                    break;

                case SegaMemoryAssociationTermKind.Concept:
                    concepts.Add(
                        value);
                    break;

                case SegaMemoryAssociationTermKind.Entity:
                    entities.Add(
                        value);
                    break;
            }
        }


        return new SegaStoredMemoryAssociationProfile(
            memoryId,
            new SegaMemoryAssociationProfile
            {
                RetrievalDescription =
                    description,

                RetrievalCues =
                    cues,

                Concepts =
                    concepts,

                Entities =
                    entities
            }
            .Normalize(),
            DeserializeEmbedding(
                bytes,
                dimension));
    }


    private static async Task InsertTermsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid memoryId,
        SegaMemoryAssociationTermKind kind,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        for (
            int index = 0;
            index < values.Count;
            index++)
        {
            string value =
                values[index];


            await using SqliteCommand command =
                connection.CreateCommand();


            command.Transaction =
                transaction;


            command.CommandText =
                """
                INSERT OR REPLACE INTO memory_associative_terms
                (
                    memory_id,
                    term_kind,
                    value,
                    normalized_value,
                    ordinal
                )
                VALUES
                (
                    $memoryId,
                    $termKind,
                    $value,
                    $normalizedValue,
                    $ordinal
                );
                """;


            AddParameter(
                command,
                "$memoryId",
                memoryId.ToString(
                    "D"));


            AddParameter(
                command,
                "$termKind",
                (int)kind);


            AddParameter(
                command,
                "$value",
                value);


            AddParameter(
                command,
                "$normalizedValue",
                NormalizeTerm(
                    value));


            AddParameter(
                command,
                "$ordinal",
                index);


            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }


    private static async Task InsertLinkAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SegaMemoryAssociationLink link,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.Transaction =
            transaction;


        command.CommandText =
            """
            INSERT OR REPLACE INTO memory_associative_links
            (
                source_memory_id,
                target_memory_id,
                relation_kind,
                shared_value,
                strength,
                updated_at_utc
            )
            VALUES
            (
                $source,
                $target,
                $relation,
                $sharedValue,
                $strength,
                $updatedAtUtc
            );
            """;


        AddParameter(
            command,
            "$source",
            link.SourceMemoryId.ToString(
                "D"));


        AddParameter(
            command,
            "$target",
            link.TargetMemoryId.ToString(
                "D"));


        AddParameter(
            command,
            "$relation",
            (int)link.RelationKind);


        AddParameter(
            command,
            "$sharedValue",
            link.SharedValue.Trim());


        AddParameter(
            command,
            "$strength",
            Math.Clamp(
                link.Strength,
                0.0,
                1.0));


        AddParameter(
            command,
            "$updatedAtUtc",
            ToUnixMilliseconds(
                DateTimeOffset.UtcNow));


        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    private static async Task DeleteLinksForMemoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.Transaction =
            transaction;


        command.CommandText =
            """
            DELETE FROM memory_associative_links
            WHERE source_memory_id = $memoryId
               OR target_memory_id = $memoryId;
            """;


        AddParameter(
            command,
            "$memoryId",
            memoryId.ToString(
                "D"));


        await command.ExecuteNonQueryAsync(
            cancellationToken);
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


    private static async Task ValidateVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command =
            connection.CreateCommand();


        command.CommandText =
            """
            SELECT value
            FROM sega_memory_association_meta
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
                "Unsupported Sega associative-memory index schema.");
        }
    }


    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(
            _connectionString);
    }


    private static string NormalizeTerm(
        string value)
    {
        return string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();
    }


    private static byte[] SerializeEmbedding(
        SemanticEmbedding embedding)
    {
        float[] values =
            embedding.Values.ToArray();


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
        if (
            dimension <=
                0
            ||
            bytes.Length !=
                dimension *
                sizeof(float))
        {
            throw new InvalidOperationException(
                "Stored associative-memory embedding is invalid.");
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


    private static long ToUnixMilliseconds(
        DateTimeOffset value)
    {
        return value.ToUnixTimeMilliseconds();
    }


    private static void AddParameter(
        SqliteCommand command,
        string name,
        object? value)
    {
        SqliteParameter parameter =
            command.Parameters.AddWithValue(
                name,
                value ?? DBNull.Value);


        if (value ==
            null)
        {
            parameter.Value =
                DBNull.Value;
        }
    }


    private sealed class ProfileBuilder
    {
        public Guid MemoryId
        {
            get;
            init;
        }


        public string? RetrievalDescription
        {
            get;
            init;
        }


        public SemanticEmbedding RetrievalEmbedding
        {
            get;
            init;
        } =
            null!;


        private readonly List<string> _cues =
            new();


        private readonly List<string> _concepts =
            new();


        private readonly List<string> _entities =
            new();


        public void Add(
            SegaMemoryAssociationTermKind kind,
            string value)
        {
            switch (kind)
            {
                case SegaMemoryAssociationTermKind.RetrievalCue:
                    _cues.Add(
                        value);
                    break;

                case SegaMemoryAssociationTermKind.Concept:
                    _concepts.Add(
                        value);
                    break;

                case SegaMemoryAssociationTermKind.Entity:
                    _entities.Add(
                        value);
                    break;
            }
        }


        public SegaStoredMemoryAssociationProfile Build()
        {
            return new SegaStoredMemoryAssociationProfile(
                MemoryId,
                new SegaMemoryAssociationProfile
                {
                    RetrievalDescription =
                        RetrievalDescription,

                    RetrievalCues =
                        _cues,

                    Concepts =
                        _concepts,

                    Entities =
                        _entities
                }
                .Normalize(),
                RetrievalEmbedding);
        }
    }
}