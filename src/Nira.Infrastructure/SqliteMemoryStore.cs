using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Nira.Core;

namespace Nira.Infrastructure;

public sealed record MemoryDetails(string Kind = "note", string Topic = "", string[]? Tags = null,
    string? SourceExcerpt = null, string? UpdatedUtc = null, Guid? SourceConversationId = null);
public sealed record SavedMemoryNote(string Id, string Content, string Source, string CreatedUtc = "", MemoryDetails? Metadata = null)
{
    public string Kind => Metadata?.Kind ?? "note";
    public string Topic => Metadata?.Topic ?? "";
    public string UpdatedUtc => Metadata?.UpdatedUtc ?? CreatedUtc;
    public string[] Tags => Metadata?.Tags ?? [];
}
public sealed record MemoryWriteResult(string Id, bool Changed, bool RelatedReviewRequired = false);
public sealed record HistoricalUserMemory(Guid ConversationId, string UpdatedUtc, string Content);

/// <summary>Single durable note store, sharing the existing SQLite database and DPAPI boundary.</summary>
public sealed partial class SqliteConversationStore
{
    public const int MemorySearchLimit = 2000;
    private const int HistoryTaskLimit = 100;
    private const int RecallCharacterBudget = 6000;

    private static void InitializeMemorySchema(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "PRAGMA table_info(memories)";
        bool hasMetadata = false;
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) if (reader.GetString(1) == "metadata") hasMetadata = true;
        if (!hasMetadata)
        {
            cmd.CommandText = "ALTER TABLE memories ADD COLUMN metadata TEXT";
            cmd.ExecuteNonQuery();
        }
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS memory_preferences (
                singleton INTEGER PRIMARY KEY CHECK(singleton=1),
                recall_enabled INTEGER NOT NULL,
                suggest_learning INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS memory_reviewed_tasks (task_id TEXT PRIMARY KEY NOT NULL);
            CREATE TABLE IF NOT EXISTS memory_candidates (
                id TEXT PRIMARY KEY NOT NULL, task_id TEXT NOT NULL, payload TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public async Task<MemoryRecallSettings> GetMemorySettingsAsync(CancellationToken ct)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT recall_enabled, suggest_learning FROM memory_preferences WHERE singleton=1";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new MemoryRecallSettings(reader.GetInt64(0) != 0, reader.GetInt64(1) != 0) : new();
    }

    public async Task SetMemorySettingsAsync(MemoryRecallSettings settings, CancellationToken ct)
    {
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memory_preferences(singleton,recall_enabled,suggest_learning) VALUES(1,$recall,$learn)
            ON CONFLICT(singleton) DO UPDATE SET recall_enabled=excluded.recall_enabled, suggest_learning=excluded.suggest_learning
            """;
        cmd.Parameters.AddWithValue("$recall", settings.RecallEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$learn", settings.SuggestLearning ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static SavedMemoryNote ReadNote(System.Data.Common.DbDataReader reader) => new(reader.GetString(0),
        WindowsDpapiProtector.Open(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<MemoryDetails>(WindowsDpapiProtector.Open(reader.GetString(4))));

    private async Task<IReadOnlyList<SavedMemoryNote>> ReadNotesAsync(int limit, CancellationToken ct)
    {
        var notes = new List<SavedMemoryNote>();
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,content,source,created_utc,metadata FROM memories ORDER BY created_utc DESC,id LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            notes.Add(ReadNote(reader));
        }
        return notes;
    }

    public Task<IReadOnlyList<SavedMemoryNote>> ListMemoryNotesAsync(CancellationToken ct) => ReadNotesAsync(MemorySearchLimit, ct);

    private static string ValidateContent(string content)
    {
        string text = content.Trim();
        if (text.Length is < 1 or > 3000 || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new ArgumentException("Use a note of 1–3000 characters, without binary or control data.");
        return text;
    }

    private static MemoryDetails ValidateDetails(MemoryDetails? details)
    {
        var value = details ?? new MemoryDetails();
        string topic = value.Topic.Trim();
        if (value.Kind is not ("note" or "preference" or "project" or "fact" or "lesson") || topic.Length > 160 || topic.Any(char.IsControl) ||
            value.Tags is { Length: > 8 } || (value.Tags ?? []).Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 60 || t.Any(char.IsControl)) ||
            value.SourceExcerpt is { Length: > 500 }) throw new ArgumentException("Invalid memory classification or evidence length.");
        return value with { Topic = topic, Tags = (value.Tags ?? []).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("O") };
    }

    public async Task<string> StoreMemoryAsync(string content, string source, CancellationToken ct) =>
        (await WriteMemoryAsync(content, source, null, ct)).Id;

    public async Task<MemoryWriteResult> WriteMemoryAsync(string content, string source, MemoryDetails? details, CancellationToken ct, IReadOnlyCollection<string>? reviewedRelatedIds = null)
    {
        string text = ValidateContent(content);
        MemoryDetails metadata = ValidateDetails(details);
        await using var conn = Open();
        using var tx = conn.BeginTransaction();
        MemoryWriteResult result = await WriteNoteInTransactionAsync(conn, tx, text, source, metadata, ct, reviewedRelatedIds);
        tx.Commit();
        return result;
    }

    private static async Task<MemoryWriteResult> WriteNoteInTransactionAsync(SqliteConnection conn, SqliteTransaction tx,
        string text, string source, MemoryDetails metadata, CancellationToken ct, IReadOnlyCollection<string>? reviewedRelatedIds = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Deduplicate exact normalized note text inside the same transaction, without a plaintext index.
        cmd.CommandText = "SELECT id,content,metadata FROM memories";
        string? existing = null;
        bool unreviewedRelated = false;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(WindowsDpapiProtector.Open(reader.GetString(1)).Trim(), text, StringComparison.Ordinal))
                { existing = reader.GetString(0); break; }
                // Only agent-proposed new notes use this review gate. Direct manual
                // creation and explicitly accepted learning suggestions keep their existing behavior.
                if (reviewedRelatedIds is not null && !reader.IsDBNull(2) &&
                    JsonSerializer.Deserialize<MemoryDetails>(WindowsDpapiProtector.Open(reader.GetString(2))) is { } prior &&
                    MemoryRanking.RelatedTopic(metadata.Topic, prior.Topic) && !reviewedRelatedIds.Contains(reader.GetString(0)))
                    unreviewedRelated = true;
            }
        if (existing is not null) return new(existing, false);
        if (unreviewedRelated) return new("", false, RelatedReviewRequired: true);
        string id = Guid.NewGuid().ToString("N");
        cmd.CommandText = "INSERT INTO memories(id,content,source,created_utc,metadata) VALUES($id,$content,$source,$created,$metadata)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$content", WindowsDpapiProtector.Seal(text));
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$created", metadata.UpdatedUtc!);
        cmd.Parameters.AddWithValue("$metadata", WindowsDpapiProtector.Seal(JsonSerializer.Serialize(metadata)));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new AgentException("Memory write was not recorded.");
        return new(id, true);
    }

    public async Task<bool> RemoveMemoryNoteAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return false;
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM memories WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    // Exact expected content is a compare-and-swap guard for user-approved changes.
    // Deleting a note does not delete original chats or their audit records.
    public async Task<bool> ChangeMemoryNoteAsync(string id, string expected, string? replacement,
        string source, MemoryDetails? details, CancellationToken ct, bool preserveClassification = false,
        bool kindSpecified = true, bool topicSpecified = true, bool tagsSpecified = true)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return false;
        string? text = replacement is null ? null : ValidateContent(replacement);
        MemoryDetails? metadata = text is null ? null : ValidateDetails(details);
        await using var conn = Open();
        using var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT content,metadata FROM memories WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        await using (var current = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await current.ReadAsync(ct) ||
                !string.Equals(WindowsDpapiProtector.Open(current.GetString(0)), expected, StringComparison.Ordinal)) return false;
            if (text is not null && preserveClassification && metadata is not null && !current.IsDBNull(1))
            {
                var prior = JsonSerializer.Deserialize<MemoryDetails>(WindowsDpapiProtector.Open(current.GetString(1)));
                if (prior is not null)
                    metadata = ValidateDetails(metadata with {
                        Kind = kindSpecified ? metadata.Kind : prior.Kind,
                        Topic = topicSpecified ? metadata.Topic : prior.Topic,
                        Tags = tagsSpecified ? metadata.Tags : prior.Tags
                    });
            }
        }
        if (text is not null && !string.Equals(text, expected, StringComparison.Ordinal))
        {
            // Do not silently merge independently approved records; ask the user to remove duplicates explicitly.
            cmd.CommandText = "SELECT content FROM memories WHERE id<>$id";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (string.Equals(WindowsDpapiProtector.Open(reader.GetString(0)).Trim(), text, StringComparison.Ordinal)) return false;
        }
        if (text is null) cmd.CommandText = "DELETE FROM memories WHERE id=$id";
        else
        {
            cmd.CommandText = "UPDATE memories SET content=$content,source=$source,metadata=$metadata WHERE id=$id";
            cmd.Parameters.AddWithValue("$content", WindowsDpapiProtector.Seal(text));
            cmd.Parameters.AddWithValue("$source", source);
            cmd.Parameters.AddWithValue("$metadata", WindowsDpapiProtector.Seal(JsonSerializer.Serialize(metadata)));
        }
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) return false;
        tx.Commit();
        return true;
    }

    private static string Searchable(SavedMemoryNote note) => note.Content + " " + note.Topic + " " + string.Join(" ", note.Tags);

    public async Task<IReadOnlyList<SavedMemoryNote>> FindMemoryAsync(string query, IReadOnlyList<string> alternatives,
        int limit, CancellationToken ct, bool automatic = false)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var notes = await ReadNotesAsync(MemorySearchLimit, ct);
        return MemoryRanking.Rank(notes.OrderByDescending(n => n.UpdatedUtc, StringComparer.Ordinal),
            Searchable, query, alternatives, limit, automatic, n => n.Topic);
    }

    public async Task<IReadOnlyList<(string Id, string Content)>> SearchMemoryAsync(string query, int limit, CancellationToken ct) =>
        (await FindMemoryAsync(query, [], limit, ct)).Select(n => (n.Id, n.Content)).ToArray();

    public async Task<MemoryRecallContext> RecallAsync(string query, CancellationToken ct)
    {
        try
        {
            var settings = await GetMemorySettingsAsync(ct);
            if (!settings.RecallEnabled || string.IsNullOrWhiteSpace(query)) return new(settings, []);
            var notes = await FindMemoryAsync(query[..Math.Min(query.Length, 1500)], [], 6, ct, automatic: true);
            var result = new List<MemoryRecallItem>();
            int budget = RecallCharacterBudget;
            foreach (var note in notes)
            {
                // Never truncate a fact into an incomplete claim. Skip records exceeding remaining budget.
                var item = new MemoryRecallItem(note.Content, note.Kind, note.Topic, note.Source, note.UpdatedUtc);
                int cost = JsonSerializer.Serialize(item).Length + 2;
                if (cost > budget) continue;
                budget -= cost;
                result.Add(item);
            }
            return new(settings, result);
        }
        catch (Exception ex) when (ex is SqliteException or CryptographicException or JsonException or FormatException or ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // Optional recall must not prevent normal tasks or leak decrypted content through error logs.
            return new(new MemoryRecallSettings(), []);
        }
    }

    public async Task<IReadOnlyList<HistoricalUserMemory>> SearchUserHistoryAsync(string query, IReadOnlyList<string> alternatives, CancellationToken ct, string order = "relevance")
    {
        var archived = new HashSet<Guid>();
        var candidates = new List<HistoricalUserMemory>();
        await using var conn = Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT thread_id FROM chat_preferences WHERE archived<>0";
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) if (Guid.TryParse(reader.GetString(0), out var id)) archived.Add(id);
        cmd.CommandText = "SELECT payload FROM conversations ORDER BY updated_utc DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", HistoryTaskLimit);
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                var state = JsonSerializer.Deserialize<ConversationState>(WindowsDpapiProtector.Open(reader.GetString(0)));
                if (state is null || state.Status != ConversationStatus.Completed || archived.Contains(state.ChatThreadId ?? state.Id)) continue;
                foreach (var message in state.Messages.Where(m => m.Role == "user" && m.AttachmentPaths is not { Count: > 0 }).TakeLast(10))
                {
                    string content = message.UserDisplayText ?? message.Content ?? "";
                    // Exclude oversized messages rather than misrepresenting a truncated excerpt.
                    if (content.Length is > 0 and <= 1500) candidates.Add(new(state.Id, state.UpdatedUtc.ToString("O"), content));
                }
            }
        // The window remains the most recent 100 task records. Chronological
        // order uses task timestamps, not an invented per-message timestamp.
        IEnumerable<HistoricalUserMemory> ordered = order switch
        {
            "oldest" => candidates.OrderBy(item => item.UpdatedUtc, StringComparer.Ordinal),
            "newest" => candidates.OrderByDescending(item => item.UpdatedUtc, StringComparer.Ordinal),
            _ => candidates
        };
        return MemoryRanking.Rank(ordered.DistinctBy(item => item.Content), item => item.Content,
            query, alternatives, 5, preserveInputOrder: order is "oldest" or "newest");
    }
}
