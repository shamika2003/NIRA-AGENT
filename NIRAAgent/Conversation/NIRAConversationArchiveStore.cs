using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using NIRAAgent.Character.Appraisal;
using NIRAAgent.Semantic;
using NIRAAgent.Presentation;

namespace NIRAAgent.Conversation;

// Separate from durable facts and character state. One SQLite write per utterance,
// with no LLM call, and on-demand hybrid (semantic + FTS5) search across sessions.
public sealed class NIRAConversationArchiveStore
{
    private const int SemanticCandidateLimit = 3000;
    private const int LexicalCandidateLimit = 1500;
    private readonly object _gate = new();
    private readonly INIRASemanticEncoder _encoder;
    private readonly string _connectionString;
    private readonly Guid _sessionId = Guid.NewGuid();
    private bool _initialized;
    public bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable("NIRA_CONVERSATION_ARCHIVE_ENABLED"),
            "0", StringComparison.Ordinal);
    public Guid CurrentSessionId => _sessionId;
    public string DatabasePath { get; }

    public NIRAConversationArchiveStore(INIRASemanticEncoder encoder)
    {
        _encoder = encoder;
        DatabasePath = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "NIRAAgent", "conversation", "NIRA-conversation.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString();
    }

    private SqliteConnection Open()
    {
        if (!Enabled) throw new InvalidOperationException("Conversation archive is disabled.");
        if (!_initialized)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var setup = new SqliteConnection(_connectionString);
            setup.Open();
            using var schema = setup.CreateCommand();
            schema.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA busy_timeout=5000;
                CREATE TABLE IF NOT EXISTS sessions(
                  id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, ended_utc TEXT);
                CREATE TABLE IF NOT EXISTS messages(
                  id TEXT PRIMARY KEY, session_id TEXT NOT NULL,
                  source_event_id TEXT, role TEXT NOT NULL,
                  content TEXT NOT NULL, occurred_utc TEXT NOT NULL,
                  embedding BLOB,
                  FOREIGN KEY(session_id) REFERENCES sessions(id));
                CREATE INDEX IF NOT EXISTS ix_messages_time ON messages(occurred_utc);
                CREATE INDEX IF NOT EXISTS ix_messages_event ON messages(source_event_id);
                CREATE INDEX IF NOT EXISTS ix_messages_session ON messages(session_id,occurred_utc);
                CREATE VIRTUAL TABLE IF NOT EXISTS message_fts USING fts5(message_id UNINDEXED, content);
                CREATE TABLE IF NOT EXISTS episodes(
                  id TEXT PRIMARY KEY, source_event_id TEXT NOT NULL UNIQUE,
                  source_message_id TEXT NOT NULL,
                  appraisal_json TEXT NOT NULL,
                  significance REAL NOT NULL,
                  occurred_utc TEXT NOT NULL,
                  FOREIGN KEY(source_message_id) REFERENCES messages(id));
                CREATE INDEX IF NOT EXISTS ix_episodes_message ON episodes(source_message_id);
                CREATE TABLE IF NOT EXISTS message_presentations(
                  message_id TEXT PRIMARY KEY, presentation_json TEXT NOT NULL,
                  FOREIGN KEY(message_id) REFERENCES messages(id));
                CREATE TABLE IF NOT EXISTS message_ui_state(
                  message_id TEXT NOT NULL, block_index INTEGER NOT NULL,
                  state_json TEXT NOT NULL,
                  PRIMARY KEY(message_id, block_index),
                  FOREIGN KEY(message_id) REFERENCES messages(id));
                """;
            schema.ExecuteNonQuery();
            // Existing archive databases have sessions(id, started_utc) only.
            using (var columns = setup.CreateCommand())
            {
                columns.CommandText = "PRAGMA table_info(sessions);";
                using var reader = columns.ExecuteReader();
                bool hasEnded = false;
                while (reader.Read())
                    hasEnded |= string.Equals(reader.GetString(1), "ended_utc", StringComparison.OrdinalIgnoreCase);
                reader.Close();
                if (!hasEnded)
                {
                    using var migration = setup.CreateCommand();
                    migration.CommandText = "ALTER TABLE sessions ADD COLUMN ended_utc TEXT;";
                    migration.ExecuteNonQuery();
                }
            }
            // An uncleanly terminated process cannot close its session itself.
            using (var recover = setup.CreateCommand())
            {
                recover.CommandText = """
                    UPDATE sessions SET ended_utc=COALESCE(
                        (SELECT MAX(occurred_utc) FROM messages WHERE session_id=sessions.id),
                        started_utc)
                    WHERE ended_utc IS NULL AND id<>$current;
                    """;
                recover.Parameters.AddWithValue("$current", _sessionId.ToString("D"));
                recover.ExecuteNonQuery();
            }
            using var session = setup.CreateCommand();
            session.CommandText = "INSERT OR IGNORE INTO sessions(id,started_utc) VALUES($id,$at);";
            session.Parameters.AddWithValue("$id", _sessionId.ToString("D"));
            session.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            session.ExecuteNonQuery();
            _initialized = true;
            Debug.WriteLine($"[ConversationArchive] READY | Session={_sessionId:D} | Database='{DatabasePath}'");
        }
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public IReadOnlyList<NIRAArchivedChatSession> ListPastSessions(int maximum = 30)
    {
        if (!Enabled) return Array.Empty<NIRAArchivedChatSession>();
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                SELECT s.id, s.started_utc, s.ended_utc,
                  COUNT(m.id), COALESCE((SELECT content FROM messages
                    WHERE session_id=s.id AND role='user'
                    ORDER BY occurred_utc, rowid LIMIT 1), '')
                FROM sessions s LEFT JOIN messages m ON m.session_id=s.id
                WHERE s.id<>$current
                GROUP BY s.id
                HAVING COUNT(m.id)>0
                ORDER BY s.started_utc DESC LIMIT $maximum;
                """;
            cmd.Parameters.AddWithValue("$current", _sessionId.ToString("D"));
            cmd.Parameters.AddWithValue("$maximum", Math.Clamp(maximum, 1, 100));
            using var reader = cmd.ExecuteReader();
            var items = new List<NIRAArchivedChatSession>();
            while (reader.Read())
            {
                string first = reader.GetString(4).Replace('\r', ' ').Replace('\n', ' ').Trim();
                items.Add(new NIRAArchivedChatSession(Guid.Parse(reader.GetString(0)),
                    DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                    reader.GetInt32(3), first.Length > 72 ? first[..72] + "…" : first));
            }
            return items;
        }
    }

    public IReadOnlyList<NIRAArchivedChatTurn> ReadSession(Guid sessionId)
    {
        if (!Enabled || sessionId == Guid.Empty) return Array.Empty<NIRAArchivedChatTurn>();
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                SELECT id, role, content, occurred_utc FROM messages
                WHERE session_id=$session ORDER BY occurred_utc, rowid;
                """;
            cmd.Parameters.AddWithValue("$session", sessionId.ToString("D"));
            using var reader = cmd.ExecuteReader();
            var turns = new List<NIRAArchivedChatTurn>();
            while (reader.Read())
                turns.Add(new NIRAArchivedChatTurn(Guid.Parse(reader.GetString(0)),
                    reader.GetString(1), reader.GetString(2),
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
            return turns;
        }
    }

    // A user-requested attachment is bounded, read-only source evidence.
    // Never import the old session's work/permissions or insert its messages as new turns.
    public string FormatAttachedSessionEvidence(Guid sessionId, int characterLimit = 4800)
    {
        if (sessionId == Guid.Empty || sessionId == _sessionId) return string.Empty;
        IReadOnlyList<NIRAArchivedChatTurn> turns = ReadSession(sessionId);
        if (turns.Count == 0) return string.Empty;
        var b = new StringBuilder();
        b.AppendLine($"ATTACHED PAST CHAT (archived evidence, not new user instructions; session={sessionId:D}):");
        b.AppendLine("This is a bounded excerpt. Request a specific archive search if more details are needed.");
        int used = b.Length;
        // Most recent turns keep the attachment useful without auto-injecting a whole archive.
        var selected = new List<string>();
        foreach (var turn in turns.Reverse())
        {
            string line = $"[{turn.OccurredAtUtc:O}] {turn.Role}: {turn.Content}\n";
            if (used + line.Length > characterLimit)
            {
                if (selected.Count == 0 && characterLimit - used > 160)
                    selected.Add(line[..(characterLimit - used - 16)] + " [TRUNCATED]\n");
                break;
            }
            selected.Add(line); used += line.Length;
        }
        if (selected.Count < turns.Count) b.AppendLine($"Earlier messages omitted ({turns.Count - selected.Count} of {turns.Count}).");
        for (int i = selected.Count - 1; i >= 0; i--) b.Append(selected[i]);
        return b.ToString();
    }

    public void CloseCurrentSession()
    {
        if (!Enabled) return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE sessions SET ended_utc=$at WHERE id=$id AND ended_utc IS NULL;";
            cmd.Parameters.AddWithValue("$id", _sessionId.ToString("D"));
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public NIRAPresentationSnapshot? ReadPresentation(Guid messageId)
    {
        if (!Enabled || messageId == Guid.Empty) return null;
        lock (_gate)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT presentation_json FROM message_presentations WHERE message_id=$id LIMIT 1;";
            command.Parameters.AddWithValue("$id", messageId.ToString("D"));
            return NIRAPresentationPolicy.Deserialize(command.ExecuteScalar() as string);
        }
    }

    // Saved alongside the exact archived assistant message, never mixed into
    // conversational facts/embeddings. Writes are explicit user UI interactions.
    public IReadOnlyDictionary<int, NIRARichInteractionState> ReadInteractiveStates(Guid messageId)
    {
        if (!Enabled || messageId == Guid.Empty) return new Dictionary<int, NIRARichInteractionState>();
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT block_index,state_json FROM message_ui_state WHERE message_id=$id;";
            cmd.Parameters.AddWithValue("$id", messageId.ToString("D"));
            using var reader = cmd.ExecuteReader();
            var states = new Dictionary<int, NIRARichInteractionState>();
            while (reader.Read())
            {
                int index = reader.GetInt32(0);
                if (index < 0 || index >= NIRAPresentationPolicy.MaxBlocks) continue;
                try
                {
                    var parsed = JsonSerializer.Deserialize<NIRARichInteractionState>(reader.GetString(1));
                    if (parsed != null) states[index] = parsed;
                }
                catch (JsonException) { /* Bad UI state never destroys the conversation. */ }
            }
            return states;
        }
    }

    public void SaveInteractiveState(Guid messageId, int blockIndex, NIRARichInteractionState state)
    {
        if (!Enabled || messageId == Guid.Empty || blockIndex < 0 ||
            blockIndex >= NIRAPresentationPolicy.MaxBlocks) return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO message_ui_state(message_id,block_index,state_json)
                SELECT id,$index,$state FROM messages WHERE id=$id AND role='assistant'
                ON CONFLICT(message_id,block_index) DO UPDATE SET state_json=excluded.state_json;
                """;
            cmd.Parameters.AddWithValue("$id", messageId.ToString("D"));
            cmd.Parameters.AddWithValue("$index", blockIndex);
            cmd.Parameters.AddWithValue("$state", JsonSerializer.Serialize(state));
            cmd.ExecuteNonQuery();
        }
    }

    public Guid? Append(string role, string content, Guid? sourceEventId = null,
        NIRAPresentationSnapshot? presentation = null)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(content)) return null;
        if (role is not ("user" or "assistant"))
            throw new ArgumentException("Only user and assistant chat turns belong in the archive.", nameof(role));
        lock (_gate)
        {
            using var db = Open();
            // Semantic encoding failure must not lose the message: FTS still indexes it.
            byte[]? embedding = null;
            try
            {
                var values = _encoder.Encode(content).Values.ToArray();
                embedding = new byte[values.Length * sizeof(float)];
                Buffer.BlockCopy(values, 0, embedding, 0, embedding.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConversationArchive] EMBEDDING_UNAVAILABLE | {ex.GetType().Name}");
            }
            Guid id = Guid.NewGuid();
            using var tx = db.BeginTransaction();
            using (var write = db.CreateCommand())
            {
                write.Transaction = tx;
                write.CommandText = """
                    INSERT INTO messages(id,session_id,source_event_id,role,content,occurred_utc,embedding)
                    VALUES($id,$session,$event,$role,$content,$at,$embedding);
                    """;
                write.Parameters.AddWithValue("$id", id.ToString("D"));
                write.Parameters.AddWithValue("$session", _sessionId.ToString("D"));
                write.Parameters.AddWithValue("$event", sourceEventId is Guid e ? (object)e.ToString("D") : DBNull.Value);
                write.Parameters.AddWithValue("$role", role);
                write.Parameters.AddWithValue("$content", content);
                write.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
                write.Parameters.AddWithValue("$embedding", (object?)embedding ?? DBNull.Value);
                write.ExecuteNonQuery();
            }
            using (var fts = db.CreateCommand())
            {
                fts.Transaction = tx;
                fts.CommandText = "INSERT INTO message_fts(message_id,content) VALUES($id,$content);";
                fts.Parameters.AddWithValue("$id", id.ToString("D"));
                fts.Parameters.AddWithValue("$content", content);
                fts.ExecuteNonQuery();
            }
            if (role == "assistant" && presentation != null)
            {
                using var display = db.CreateCommand();
                display.Transaction = tx;
                display.CommandText = "INSERT INTO message_presentations(message_id,presentation_json) VALUES($id,$json);";
                display.Parameters.AddWithValue("$id", id.ToString("D"));
                display.Parameters.AddWithValue("$json", NIRAPresentationPolicy.Serialize(presentation));
                display.ExecuteNonQuery();
            }
            tx.Commit();
            Debug.WriteLine($"[ConversationArchive] SAVED | Session={_sessionId:D} | Role={role} | Message={id:D}");
            return id;
        }
    }

    // The appraisal is evidence of NIRA's interpretation, NOT an additional
    // authoritative relationship or mood state. No second LLM call.
    public void RecordEpisode(Guid sourceEventId, NIRAInteractionAppraisal appraisal)
    {
        if (!Enabled || sourceEventId == Guid.Empty) return;
        NIRAInteractionAppraisal normalized = appraisal.Normalize();
        var m = normalized.Meaning;
        double significance = Math.Max(normalized.SituationIntensity,
            new[] { Math.Abs(m.Respect), Math.Abs(m.Warmth), Math.Abs(m.Trust),
                m.Appreciation, m.Affection, m.Playfulness, m.Hostility,
                m.Dismissal, m.Repair, m.Concern, m.Pressure }.Max());
        // Only substantial appraisals are episode-indexed; all original messages
        // remain in the archive independently of this threshold.
        if (significance * normalized.Confidence < 0.40) return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO episodes(id,source_event_id,source_message_id,
                    appraisal_json,significance,occurred_utc)
                SELECT $id,$event,id,$appraisal,$significance,$at FROM messages
                WHERE source_event_id=$event AND role='user'
                ORDER BY occurred_utc DESC LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            cmd.Parameters.AddWithValue("$event", sourceEventId.ToString("D"));
            cmd.Parameters.AddWithValue("$appraisal", JsonSerializer.Serialize(normalized));
            cmd.Parameters.AddWithValue("$significance", significance);
            cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            if (cmd.ExecuteNonQuery() > 0)
                Debug.WriteLine($"[SocialEpisode] SAVED | SourceEvent={sourceEventId:D} | Significance={significance:F2}");
        }
    }

    public IReadOnlyList<NIRAArchivedConversationHit> Search(
        NIRAConversationSearchRequest raw, Guid? excludeSourceEventId = null)
    {
        if (!Enabled) return Array.Empty<NIRAArchivedConversationHit>();
        var request = raw.Normalize();
        if (string.IsNullOrWhiteSpace(request.Query)) return Array.Empty<NIRAArchivedConversationHit>();
        lock (_gate)
        {
            using var db = Open();
            float[]? queryEmbedding = null;
            try { queryEmbedding = _encoder.Encode(request.Query).Values.ToArray(); }
            catch (Exception ex) { Debug.WriteLine($"[ConversationArchive] SEARCH_EMBEDDING_UNAVAILABLE | {ex.GetType().Name}"); }
            var candidates = new Dictionary<Guid, Candidate>();
            string where = " WHERE ($from='' OR m.occurred_utc >= $from) AND ($to='' OR m.occurred_utc <= $to) AND ($session='' OR m.session_id=$session)";
            void Collect(string sql, bool lexical)
            {
                using var query = db.CreateCommand();
                query.CommandText = sql;
                query.Parameters.AddWithValue("$from", request.FromUtc ?? "");
                query.Parameters.AddWithValue("$to", request.ToUtc ?? "");
                query.Parameters.AddWithValue("$session", request.SessionId ?? (request.CurrentSessionOnly ? _sessionId.ToString("D") : ""));
                if (lexical) query.Parameters.AddWithValue("$match", BuildFtsQuery(request.Query));
                using var reader = query.ExecuteReader();
                while (reader.Read())
                {
                    var id = Guid.Parse(reader.GetString(0));
                    if (candidates.ContainsKey(id)) continue;
                    candidates[id] = new Candidate(id, Guid.Parse(reader.GetString(1)),
                        reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                        reader.GetString(3), reader.GetString(4),
                        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                        reader.IsDBNull(6) ? null : (byte[])reader.GetValue(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7));
                }
            }
            const string projection = "SELECT m.id,m.session_id,m.source_event_id,m.role,m.content,m.occurred_utc,m.embedding,e.appraisal_json FROM messages m LEFT JOIN episodes e ON e.source_message_id=m.id";
            Collect(projection + where + " ORDER BY m.occurred_utc DESC LIMIT " + SemanticCandidateLimit, false);
            string fts = BuildFtsQuery(request.Query);
            if (!string.IsNullOrEmpty(fts))
            {
                try
                {
                    Collect(projection + " JOIN message_fts f ON f.message_id=m.id" + where +
                        " AND message_fts MATCH $match ORDER BY bm25(message_fts) LIMIT " + LexicalCandidateLimit, true);
                }
                catch (SqliteException ex)
                {
                    Debug.WriteLine($"[ConversationArchive] FTS_FALLBACK | Error={ex.SqliteErrorCode}");
                }
            }
            var now = DateTimeOffset.UtcNow;
            string? FindReply(Guid? eventId, Guid sourceId)
            {
                if (eventId is not Guid valid) return null;
                using var reply = db.CreateCommand();
                reply.CommandText = """
                    SELECT content FROM messages WHERE source_event_id=$event
                    AND role='assistant' AND id<>$source
                    ORDER BY occurred_utc ASC LIMIT 1;
                    """;
                reply.Parameters.AddWithValue("$event", valid.ToString("D"));
                reply.Parameters.AddWithValue("$source", sourceId.ToString("D"));
                return reply.ExecuteScalar() as string;
            }
            var ranked = candidates.Values
                .Where(c => excludeSourceEventId == null || c.Event != excludeSourceEventId)
                .Select(c =>
            {
                double semantic = Cosine(queryEmbedding, c.Embedding);
                double lexical = LexicalOverlap(request.Query, c.Content);
                double freshness = Math.Exp(-Math.Max(0, (now - c.When).TotalDays) / 45.0);
                double score = 0.73 * Math.Max(0, semantic) + 0.22 * lexical + 0.05 * freshness;
                if (request.IncludeEpisodes && c.Episode != null) score += 0.04;
                return (Candidate: c, Score: score);
            }).OrderByDescending(x => x.Score)
              .Take(request.MaximumResults)
              .Select(x => new NIRAArchivedConversationHit(x.Candidate.Id,
                  x.Candidate.Session, x.Candidate.Event, x.Candidate.Role,
                  x.Candidate.Content, x.Candidate.When, x.Score,
                  request.IncludeEpisodes ? x.Candidate.Episode : null,
                  FindReply(x.Candidate.Event, x.Candidate.Id))).ToArray();
            Debug.WriteLine($"[ConversationArchive] SEARCH | Candidates={candidates.Count} | Returned={ranked.Length} | SessionOnly={request.CurrentSessionOnly}");
            // Recovery for a model that accidentally scopes a past-chat question
            // to a freshly restarted session containing only the question itself.
            // Never broaden an explicit exact session-id scope.
            if (ranked.Length == 0 && request.CurrentSessionOnly && request.SessionId == null)
            {
                using var count = db.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM messages WHERE session_id=$id;";
                count.Parameters.AddWithValue("$id", _sessionId.ToString("D"));
                if (Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture) <= 1)
                {
                    Debug.WriteLine("[ConversationArchive] SESSION_SCOPE_RECOVERY | Empty fresh session; searching earlier sessions.");
                    return Search(request with { CurrentSessionOnly = false }, excludeSourceEventId);
                }
            }
            return ranked;
        }
    }

    // Explicit user-controlled removal surface for future Settings/UI integration.
    // Deletion does not mutate NIRA's authoritative character / other memory stores.
    public void DeleteAll()
    {
        if (!Enabled) return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM episodes; DELETE FROM message_fts; DELETE FROM messages; DELETE FROM sessions;";
            cmd.ExecuteNonQuery();
            using var restore = db.CreateCommand();
            restore.CommandText = "INSERT INTO sessions(id,started_utc) VALUES($id,$at);";
            restore.Parameters.AddWithValue("$id", _sessionId.ToString("D"));
            restore.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            restore.ExecuteNonQuery();
        }
    }

    private static string BuildFtsQuery(string input) => string.Join(" OR ",
        Regex.Matches(input, @"[\p{L}\p{Nd}]{3,}")
            .Select(m => m.Value.ToLowerInvariant()).Distinct().Take(12)
            .Select(t => "\"" + t.Replace("\"", "") + "\""));

    private static double LexicalOverlap(string query, string text)
    {
        var tokens = Regex.Matches(query, @"[\p{L}\p{Nd}]{3,}")
            .Select(m => m.Value.ToLowerInvariant()).Distinct().ToArray();
        if (tokens.Length == 0) return 0;
        return (double)tokens.Count(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)) / tokens.Length;
    }

    private static double Cosine(float[]? query, byte[]? stored)
    {
        if (query == null || stored == null || stored.Length != query.Length * 4) return 0;
        double dot = 0, aa = 0, bb = 0;
        for (int i = 0; i < query.Length; i++)
        {
            double a = query[i], b = BitConverter.ToSingle(stored, i * 4);
            dot += a * b; aa += a * a; bb += b * b;
        }
        return aa > 0 && bb > 0 ? dot / Math.Sqrt(aa * bb) : 0;
    }

    private sealed record Candidate(Guid Id, Guid Session, Guid? Event,
        string Role, string Content, DateTimeOffset When, byte[]? Embedding, string? Episode);
}

public sealed record NIRAArchivedChatSession(Guid SessionId, DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc, int MessageCount, string Title);
public sealed record NIRAArchivedChatTurn(Guid MessageId, string Role, string Content,
    DateTimeOffset OccurredAtUtc);


