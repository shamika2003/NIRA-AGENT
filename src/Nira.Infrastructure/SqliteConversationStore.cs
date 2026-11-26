using System.Text.Json;
using Microsoft.Data.Sqlite;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// Atomic, version-checked persistence. The entire tool journal is stored before
/// dispatch and after observation, preventing silent operation replay on restart.
/// </summary>
public sealed partial class SqliteConversationStore : IConversationStore, IMemoryContextProvider
{
    private readonly string _connectionString;
    // Encrypted local JSON, not HTML. Avoid redundant escaping of nested tool receipts
    // and generated documents. Existing rows use the same JSON format and remain readable.
    private static readonly JsonSerializerOptions ConversationJson = new()
    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public SqliteConversationStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var builder = new SqliteConnectionStringBuilder {
            DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true, DefaultTimeout = 30 };
        _connectionString = builder.ToString();
        using var conn = Open();
        using var command = conn.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY NOT NULL,
                revision INTEGER NOT NULL,
                payload TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS memories (
                id TEXT PRIMARY KEY NOT NULL,
                content TEXT NOT NULL,
                source TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_memories_created ON memories(created_utc);
            CREATE TABLE IF NOT EXISTS chat_preferences (
                thread_id TEXT PRIMARY KEY NOT NULL,
                title TEXT,
                archived INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
        InitializeMemorySchema(conn);
        InitializeProcedureSchema(conn);
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<ConversationState?> LoadAsync(Guid id, CancellationToken ct)
    {
        await using var conn = Open();
        await using var command = conn.CreateCommand();
        command.CommandText = "SELECT payload, revision FROM conversations WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        ConversationState state = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)))
            ?? throw new InvalidDataException("Stored conversation was empty.");
        state.Revision = reader.GetInt64(1);
        return state;
    }

    // Read-only desktop history. Decrypt using the same current-user DPAPI store.
    public async Task<IReadOnlyList<SavedConversationSummary>> ListRecentAsync(CancellationToken ct, bool includeArchived = false)
    {
        var results = new List<SavedConversationSummary>();
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload FROM conversations ORDER BY updated_utc DESC LIMIT 500";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            ConversationState? state = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)));
            if (state is not null && !string.IsNullOrWhiteSpace(state.Objective))
                results.Add(new SavedConversationSummary(state.Id, state.Objective, state.Status, state.UpdatedUtc,
                    state.ChatThreadId ?? state.Id, state.ChatThreadTitle ?? state.Objective));
        }
        // Rows are newest-first. One history card per logical chat; opening it
        // restores the linked task sequence from the encrypted store.
        await reader.DisposeAsync();
        var preferences = new Dictionary<Guid, (string? Title, bool Archived)>();
        await using var prefs = conn.CreateCommand();
        prefs.CommandText = "SELECT thread_id, title, archived FROM chat_preferences";
        await using var prefReader = await prefs.ExecuteReaderAsync(ct);
        while (await prefReader.ReadAsync(ct))
        {
            if (!Guid.TryParse(prefReader.GetString(0), out Guid id)) continue;
            preferences[id] = (prefReader.IsDBNull(1) ? null : WindowsDpapiProtector.Open(prefReader.GetString(1)),
                prefReader.GetInt64(2) != 0);
        }
        return results.GroupBy(item => item.ThreadId).Select(group => group.First()).Select(item =>
            preferences.TryGetValue(item.ThreadId, out var pref)
                ? item with { ThreadTitle = pref.Title ?? item.ThreadTitle, Archived = pref.Archived } : item)
            .Where(item => includeArchived || !item.Archived).Take(80).ToArray();
    }

    // Gallery reuses the encrypted task journal; no new database or remote access.
    public async Task<IReadOnlyList<ImageGalleryEntry>> ListGalleryAsync(CancellationToken ct)
    {
        var entries = new List<ImageGalleryEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload FROM conversations ORDER BY updated_utc DESC LIMIT 500";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            var state = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)));
            if (state is null) continue;
            foreach (var entry in ImageGalleryEntry.FromTask(state).OrderByDescending(e => e.RetrievedUtc))
                if (seen.Add(entry.Sha256 + "|" + entry.Path)) entries.Add(entry);
        }
        return entries.OrderByDescending(e => e.RetrievedUtc).ToArray();
    }

    // Explicit UI operations only; not exposed to the model as execution tools.
    // Archiving is reversible and does not alter task status or any operation receipt.
    public async Task SetChatPreferencesAsync(Guid threadId, string? title, bool archived, CancellationToken ct)
    {
        if (threadId == Guid.Empty) throw new ArgumentException("Invalid chat identity.");
        if (title is not null)
        {
            title = title.Trim();
            if (title.Length is < 1 or > 160 || title.Any(char.IsControl))
                throw new ArgumentException("Use a single-line title of 1–160 characters.");
        }
        // The root must actually exist; no ghost entries and no rewriting historical tasks.
        var root = await LoadAsync(threadId, ct);
        if (root is null || (root.ChatThreadId ?? root.Id) != threadId)
            throw new AgentException("Chat thread not found.");
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat_preferences(thread_id,title,archived) VALUES($id,$title,$archived)
            ON CONFLICT(thread_id) DO UPDATE SET title=COALESCE(excluded.title,chat_preferences.title), archived=excluded.archived
            """;
        cmd.Parameters.AddWithValue("$id", threadId.ToString("D"));
        cmd.Parameters.AddWithValue("$title", title is null ? (object)DBNull.Value : WindowsDpapiProtector.Seal(title));
        cmd.Parameters.AddWithValue("$archived", archived ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ConversationState>> LoadThreadAsync(Guid latest, CancellationToken ct)
    {
        var chain = new List<ConversationState>();
        var visited = new HashSet<Guid>();
        Guid? next = latest;
        Guid? root = null;
        while (next is Guid id && chain.Count < 100 && visited.Add(id))
        {
            var state = await LoadAsync(id, ct);
            if (state is null) break;
            root ??= state.ChatThreadId ?? state.Id;
            if ((state.ChatThreadId ?? state.Id) != root) break;
            chain.Add(state);
            next = state.PreviousTaskId;
        }
        chain.Reverse();
        return chain;
    }

    public async Task<Guid> CreateFollowupAsync(Guid previous, CancellationToken ct, bool startNewAfterPause = false)
    {
        var chain = await LoadThreadAsync(previous, ct);
        if (chain.Count == 0 || (chain[^1].Status != ConversationStatus.Completed &&
            !(startNewAfterPause && chain[^1].Status is (ConversationStatus.Paused or ConversationStatus.Running or
                ConversationStatus.Failed or ConversationStatus.NeedsReconciliation or ConversationStatus.AwaitingApproval)) &&
            !(chain[^1].Status == ConversationStatus.Paused && ToolRecoveryPolicy.HasUnverifiedEffect(chain[^1]))))
            throw new AgentException("Only a completed task or explicitly isolated interrupted task can start a new linked chat turn.");
        var parent = chain[^1];
        var excerpts = new List<string>();
        int remaining = ConversationState.PreviousChatContextLimit;
        foreach (var state in chain.TakeLast(6).Reverse())
        {
            // Preserve whole recent exchanges before adding older history. A prior
            // task never supplies current authorization or operation receipts.
            var turn = new List<string>();
            if (state.Status != ConversationStatus.Completed)
            {
                if (!string.IsNullOrWhiteSpace(state.LastAnswer))
                    turn.Add("Previous unfinished task outcome (not a request): " + state.LastAnswer);
            }
            else
            {
                foreach (var user in state.Messages.Where(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content)))
                    turn.Add("User: " + user.Content);
                if (!string.IsNullOrWhiteSpace(state.LastAnswer)) turn.Add("NIRA: " + state.LastAnswer);
            }
            string exchange = string.Join("\n\n", turn);
            if (exchange.Length == 0) continue;
            if (exchange.Length > remaining)
            {
                if (excerpts.Count == 0)
                {
                    // Explicitly mark exceptionally long answers as incomplete; keep
                    // both the initial request/answer opening and its conclusion.
                    const string omitted = "\n\n[Middle of previous exchange omitted by context limit; not a complete transcript.]\n\n";
                    int head = (remaining - omitted.Length) / 2;
                    int tail = remaining - omitted.Length - head;
                    if (char.IsHighSurrogate(exchange[head - 1])) head--;
                    if (char.IsLowSurrogate(exchange[^tail])) tail--;
                    excerpts.Add(exchange[..head] + omitted + exchange[^tail..]);
                }
                break;
            }
            excerpts.Add(exchange);
            remaining -= exchange.Length + 2;
            if (remaining <= 0) break;
        }
        excerpts.Reverse();
        string context = string.Join("\n\n", excerpts);
        var child = new ConversationState {
            ChatThreadId = parent.ChatThreadId ?? parent.Id, PreviousTaskId = parent.Id,
            ChatThreadTitle = parent.ChatThreadTitle ?? parent.Objective, PreviousChatContext = context
        };
        await SaveAsync(child, ct);
        return child.Id;
    }

    public async Task<Guid?> FindRecentUnfinishedAsync(CancellationToken ct)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload FROM conversations ORDER BY updated_utc DESC LIMIT 40";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ConversationState? candidate = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)));
            if (candidate is not null && !string.IsNullOrWhiteSpace(candidate.Objective) && candidate.Status is not
                (ConversationStatus.Completed or ConversationStatus.Failed))
                return candidate.Id;
        }
        return null;
    }

    public async Task SaveAsync(ConversationState state, CancellationToken ct)
    {
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        await using var conn = Open();
        using var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        long next = checked(state.Revision + 1);
        // Use an optimistic compare-and-swap to prevent two hosts overwriting each other.
        if (state.Revision == 0)
        {
            cmd.CommandText = """
                INSERT OR IGNORE INTO conversations(id, revision, payload, updated_utc)
                VALUES ($id, $next, $payload, $updated)
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE conversations SET revision=$next, payload=$payload, updated_utc=$updated
                WHERE id=$id AND revision=$current
                """;
            cmd.Parameters.AddWithValue("$current", state.Revision);
        }
        cmd.Parameters.AddWithValue("$id", state.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$next", next);
        cmd.Parameters.AddWithValue("$payload", WindowsDpapiProtector.Seal(JsonSerializer.Serialize(state, ConversationJson)));
        cmd.Parameters.AddWithValue("$updated", state.UpdatedUtc.ToString("O"));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new AgentException("Conversation version conflict. Reload before continuing.");
        tx.Commit();
        state.Revision = next;
    }


}

public sealed record SavedConversationSummary(Guid Id, string Objective, ConversationStatus Status, DateTimeOffset UpdatedUtc, Guid ThreadId, string ThreadTitle, bool Archived = false);
