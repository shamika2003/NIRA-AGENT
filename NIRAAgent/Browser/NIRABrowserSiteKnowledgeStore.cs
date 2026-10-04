using System.Diagnostics;
using System.Text.Json;

using Microsoft.Data.Sqlite;

using NIRAAgent.Semantic;

namespace NIRAAgent.Browser;

// Persistent, non-secret knowledge of what observed routes actually contained.
//
// This is NOT a website workflow, not an auth store and not a cache of page text.
// It stores only:
//   * origin
//   * route path (query values are never persisted)
//   * stable document fingerprint
//   * local MiniLM content vectors
//   * observation count / timestamp
//
// A later objective can compare semantically against those vectors. Therefore a
// route learned as irrelevant to one task can still be highly relevant to a
// different task without any hard-coded page/menu names.
internal sealed record NIRABrowserRouteKnowledge(
    string RouteKey,
    double ContentSimilarity,
    int Observations,
    DateTimeOffset LastSeenUtc);

internal sealed class NIRABrowserSiteKnowledgeStore
{
    private const int MaximumRoutesPerOrigin =
        160;

    private const int MaximumVectorsPerRoute =
        10;

    private readonly object _sync =
        new();

    private readonly string _connectionString =
        string.Empty;

    private bool _available;

    public NIRABrowserSiteKnowledgeStore()
    {
        try
        {
            string local =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(
                    local))
            {
                local =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile),
                        ".NIRA");
            }

            string root =
                Path.Combine(
                    local,
                    "NIRAAgent",
                    "browser",
                    "knowledge");

            Directory.CreateDirectory(
                root);

            string path =
                Path.Combine(
                    root,
                    "site-route-knowledge.db");

            _connectionString =
                new SqliteConnectionStringBuilder
                {
                    DataSource =
                        path,
                    Mode =
                        SqliteOpenMode.ReadWriteCreate,
                    Pooling =
                        true,
                    DefaultTimeout =
                        5
                }
                .ToString();

            using SqliteConnection connection =
                Open();

            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;

                CREATE TABLE IF NOT EXISTS browser_route_knowledge (
                    origin TEXT NOT NULL,
                    route_key TEXT NOT NULL,
                    document_hash TEXT NOT NULL,
                    embeddings_json TEXT NOT NULL,
                    observations INTEGER NOT NULL,
                    last_seen_utc TEXT NOT NULL,
                    PRIMARY KEY(origin, route_key)
                );

                CREATE INDEX IF NOT EXISTS ix_browser_route_knowledge_origin_seen
                    ON browser_route_knowledge(origin, last_seen_utc DESC);
                """;

            command.ExecuteNonQuery();

            _available =
                true;
        }
        catch (Exception ex)
        {
            _available =
                false;

            Debug.WriteLine(
                $"[BrowserKnowledge] DISABLED | Type={ex.GetType().Name} | {ex.Message}");
        }
    }

    public IReadOnlyDictionary<string, NIRABrowserRouteKnowledge> Recall(
        string origin,
        SemanticEmbedding objectiveEmbedding)
    {
        Dictionary<string, NIRABrowserRouteKnowledge> result =
            new(
                StringComparer.Ordinal);

        if (!_available ||
            string.IsNullOrWhiteSpace(
                origin))
        {
            return result;
        }

        try
        {
            lock (_sync)
            {
                using SqliteConnection connection =
                    Open();

                using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    SELECT route_key,
                           embeddings_json,
                           observations,
                           last_seen_utc
                    FROM browser_route_knowledge
                    WHERE origin = $origin
                    ORDER BY last_seen_utc DESC
                    LIMIT $limit;
                    """;

                command.Parameters.AddWithValue(
                    "$origin",
                    NormalizeOrigin(
                        origin));

                command.Parameters.AddWithValue(
                    "$limit",
                    MaximumRoutesPerOrigin);

                using SqliteDataReader reader =
                    command.ExecuteReader();

                while (reader.Read())
                {
                    string routeKey =
                        reader.GetString(
                            0);

                    string embeddingsJson =
                        reader.GetString(
                            1);

                    int observations =
                        Math.Max(
                            1,
                            reader.GetInt32(
                                2));

                    DateTimeOffset lastSeen =
                        DateTimeOffset.TryParse(
                            reader.GetString(
                                3),
                            out DateTimeOffset parsed)
                            ? parsed
                            : DateTimeOffset.MinValue;

                    float[][] vectors =
                        JsonSerializer.Deserialize<float[][]>(
                            embeddingsJson)
                        ?? Array.Empty<float[]>();

                    double best =
                        0.0;

                    foreach (float[] vector in
                             vectors.Take(
                                 MaximumVectorsPerRoute))
                    {
                        if (vector.Length !=
                            objectiveEmbedding.Dimension)
                        {
                            continue;
                        }

                        SemanticEmbedding candidate =
                            new(
                                vector);

                        best =
                            Math.Max(
                                best,
                                Math.Clamp(
                                    NIRASemanticSimilarity.Cosine(
                                        objectiveEmbedding,
                                        candidate),
                                    0.0,
                                    1.0));
                    }

                    result[routeKey] =
                        new NIRABrowserRouteKnowledge(
                            routeKey,
                            best,
                            observations,
                            lastSeen);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[BrowserKnowledge] RECALL FAILED | Type={ex.GetType().Name} | {ex.Message}");
        }

        return result;
    }

    public void Remember(
        string origin,
        string rawUrl,
        string documentHash,
        IReadOnlyList<SemanticEmbedding> contentEmbeddings)
    {
        if (!_available ||
            string.IsNullOrWhiteSpace(
                origin) ||
            contentEmbeddings.Count ==
                0)
        {
            return;
        }

        string routeKey =
            RouteKey(
                rawUrl);

        if (string.IsNullOrWhiteSpace(
                routeKey))
        {
            return;
        }

        float[][] vectors =
            contentEmbeddings
                .Take(
                    MaximumVectorsPerRoute)
                .Select(embedding =>
                    embedding.Values
                        .ToArray())
                .ToArray();

        if (vectors.Length ==
            0)
        {
            return;
        }

        string serialized =
            JsonSerializer.Serialize(
                vectors);

        try
        {
            lock (_sync)
            {
                using SqliteConnection connection =
                    Open();

                using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    INSERT INTO browser_route_knowledge (
                        origin,
                        route_key,
                        document_hash,
                        embeddings_json,
                        observations,
                        last_seen_utc)
                    VALUES (
                        $origin,
                        $route,
                        $hash,
                        $embeddings,
                        1,
                        $seen)
                    ON CONFLICT(origin, route_key) DO UPDATE SET
                        document_hash = excluded.document_hash,
                        embeddings_json = excluded.embeddings_json,
                        observations = browser_route_knowledge.observations + 1,
                        last_seen_utc = excluded.last_seen_utc;
                    """;

                command.Parameters.AddWithValue(
                    "$origin",
                    NormalizeOrigin(
                        origin));

                command.Parameters.AddWithValue(
                    "$route",
                    routeKey);

                command.Parameters.AddWithValue(
                    "$hash",
                    documentHash?.Trim()
                    ?? string.Empty);

                command.Parameters.AddWithValue(
                    "$embeddings",
                    serialized);

                command.Parameters.AddWithValue(
                    "$seen",
                    DateTimeOffset.UtcNow.ToString(
                        "O"));

                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[BrowserKnowledge] REMEMBER FAILED | Type={ex.GetType().Name} | {ex.Message}");
        }
    }

    public static string RouteKey(
        string rawUrl)
    {
        if (!Uri.TryCreate(
                rawUrl,
                UriKind.Absolute,
                out Uri? uri)
            ||
            uri.Scheme is not ("http" or "https"))
        {
            return string.Empty;
        }

        // Query VALUES may contain account IDs, filters or other user-specific
        // data. Route learning intentionally persists only the path. This also
        // prevents harmless query variants of one rendered document from being
        // learned as separate conceptual pages.
        string path =
            uri.AbsolutePath;

        if (string.IsNullOrWhiteSpace(
                path))
        {
            return "/";
        }

        return path.Length >
               1
            ? path.TrimEnd('/')
            : path;
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection =
            new(
                _connectionString);

        connection.Open();

        using SqliteCommand foreignKeys =
            connection.CreateCommand();

        foreignKeys.CommandText =
            "PRAGMA foreign_keys=ON;";

        foreignKeys.ExecuteNonQuery();

        return connection;
    }

    private static string NormalizeOrigin(
        string origin)
    {
        if (!Uri.TryCreate(
                origin,
                UriKind.Absolute,
                out Uri? uri)
            ||
            uri.Scheme is not ("http" or "https"))
        {
            return origin.Trim();
        }

        return uri
            .GetLeftPart(
                UriPartial.Authority)
            .ToLowerInvariant();
    }
}
