using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Nira.Infrastructure;

public sealed partial class SqliteConversationStore
{
    private const int ProcedureLimit = 200;
    private static void InitializeProcedureSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS skill_preferences(singleton INTEGER PRIMARY KEY CHECK(singleton=1), enabled INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS learned_procedures(id TEXT NOT NULL, family TEXT NOT NULL, version INTEGER NOT NULL, payload TEXT NOT NULL,
                created_utc TEXT NOT NULL, matches INTEGER NOT NULL DEFAULT 0, deviations INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(id,version));
            CREATE INDEX IF NOT EXISTS idx_procedure_family ON learned_procedures(family,version);
            CREATE TABLE IF NOT EXISTS skill_reviewed_tasks(task_id TEXT PRIMARY KEY NOT NULL);
            CREATE TABLE IF NOT EXISTS skill_uses(id TEXT NOT NULL, version INTEGER NOT NULL, task_id TEXT NOT NULL,
                PRIMARY KEY(id,version,task_id));
            CREATE TRIGGER IF NOT EXISTS skill_uses_remove AFTER DELETE ON learned_procedures
                BEGIN DELETE FROM skill_uses WHERE id=OLD.id AND version=OLD.version; END;
            """;
        cmd.ExecuteNonQuery();
    }
    private static bool SkillEnabled(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT enabled FROM skill_preferences WHERE singleton=1";
        return cmd.ExecuteScalar() is long enabled && enabled == 1;
    }
    public Task<bool> GetSkillLearningAsync(CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); using var conn = Open(); return Task.FromResult(SkillEnabled(conn)); }
    public async Task SetSkillLearningAsync(bool enabled, CancellationToken ct)
    {
        await using var conn = Open(); await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO skill_preferences VALUES(1,$enabled) ON CONFLICT(singleton) DO UPDATE SET enabled=excluded.enabled";
        cmd.Parameters.AddWithValue("$enabled", enabled ? 1 : 0); await cmd.ExecuteNonQueryAsync(ct);
    }
    private static LearnedProcedure ReadProcedure(System.Data.Common.DbDataReader r) =>
        (JsonSerializer.Deserialize<LearnedProcedure>(WindowsDpapiProtector.Open(r.GetString(0))) ?? throw new InvalidDataException("Empty procedure"))
            with { Version = r.GetInt32(1), Matches = r.GetInt32(2), Deviations = r.GetInt32(3) };
    public async Task<IReadOnlyList<LearnedProcedure>> ListProceduresAsync(CancellationToken ct)
    {
        await using var conn = Open(); await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT payload,version,matches,deviations FROM learned_procedures p
            WHERE version=(SELECT MAX(version) FROM learned_procedures WHERE id=p.id)
            ORDER BY created_utc DESC,id LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", ProcedureLimit);
        await using var reader = await cmd.ExecuteReaderAsync(ct); var result = new List<LearnedProcedure>();
        while (await reader.ReadAsync(ct)) result.Add(ReadProcedure(reader));
        return result;
    }
    public async Task<bool> ClaimProcedureReviewAsync(Guid taskId, CancellationToken ct)
    {
        await using var conn = Open(); using var tx = conn.BeginTransaction();
        if (!SkillEnabled(conn, tx)) return false;
        await using var cmd = conn.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO skill_reviewed_tasks VALUES($task)";
        cmd.Parameters.AddWithValue("$task", taskId.ToString("D")); bool inserted = await cmd.ExecuteNonQueryAsync(ct) == 1;
        tx.Commit(); return inserted;
    }
    public async Task<bool> SaveProcedureAsync(LearnedProcedure procedure, CancellationToken ct)
    {
        await using var conn = Open(); using var tx = conn.BeginTransaction();
        if (!SkillEnabled(conn, tx)) return false;
        await using var cmd = conn.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT payload,version,matches,deviations FROM learned_procedures WHERE family=$family ORDER BY version DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$family", procedure.Family);
        LearnedProcedure? prior;
        await using (var reader = await cmd.ExecuteReaderAsync(ct)) prior = await reader.ReadAsync(ct) ? ReadProcedure(reader) : null;
        if (prior is not null && prior.Steps.Select(s => s.Contract).SequenceEqual(procedure.Steps.Select(s => s.Contract))) return false;
        cmd.CommandText = "SELECT COUNT(DISTINCT id) FROM learned_procedures";
        cmd.Parameters.Clear();
        if (prior is null && Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) >= ProcedureLimit) return false;
        var next = procedure with { Id = prior?.Id ?? procedure.Id, Version = (prior?.Version ?? 0) + 1, Matches = 0, Deviations = 0 };
        cmd.CommandText = "INSERT INTO learned_procedures(id,family,version,payload,created_utc) VALUES($id,$family,$version,$payload,$utc)";
        cmd.Parameters.AddWithValue("$id", next.Id); cmd.Parameters.AddWithValue("$family", next.Family); cmd.Parameters.AddWithValue("$version", next.Version);
        cmd.Parameters.AddWithValue("$payload", WindowsDpapiProtector.Seal(JsonSerializer.Serialize(next)));
        cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct); tx.Commit(); return true;
    }
    public async Task<bool> RecordProcedureUseAsync(LearnedProcedure procedure, Guid task, bool matched, CancellationToken ct)
    {
        await using var conn = Open(); using var tx = conn.BeginTransaction();
        if (!SkillEnabled(conn, tx)) return false;
        await using var cmd = conn.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO skill_uses SELECT id,version,$task FROM learned_procedures WHERE id=$id AND version=$version";
        cmd.Parameters.AddWithValue("$id", procedure.Id); cmd.Parameters.AddWithValue("$version", procedure.Version);
        cmd.Parameters.AddWithValue("$task", task.ToString("D"));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) return false;
        cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("$id", procedure.Id); cmd.Parameters.AddWithValue("$version", procedure.Version);
        cmd.CommandText = matched ? "UPDATE learned_procedures SET matches=matches+1 WHERE id=$id AND version=$version" :
            "UPDATE learned_procedures SET deviations=deviations+1 WHERE id=$id AND version=$version";
        await cmd.ExecuteNonQueryAsync(ct); tx.Commit(); return true;
    }
    public async Task DeleteProcedureAsync(string id, CancellationToken ct)
    {
        await using var conn = Open(); await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM learned_procedures WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
