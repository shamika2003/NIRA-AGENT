using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SegaAgent.Capabilities;

namespace SegaAgent.Authorization;

// Grants and their audit entries commit in one transaction. The audit never
// stores file contents, command bodies, HTTP headers, query strings or output.
public sealed class SegaAuthorityStore
{
    private readonly string _connectionString;
    public string DatabasePath { get; }
    public string ProtectedDirectory => Path.GetDirectoryName(DatabasePath)!;
    public event Action? Changed;

    public SegaAuthorityStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SegaAgent", "authority", "sega-authority.db")) { }

    public SegaAuthorityStore(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(ProtectedDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true, DefaultTimeout = 5
        }.ToString();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS authority_schema (version INTEGER NOT NULL);
            INSERT INTO authority_schema SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM authority_schema);
            CREATE TABLE IF NOT EXISTS authority_scopes (
                id TEXT PRIMARY KEY, json TEXT NOT NULL, revoked_utc TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS authority_audit (
                id TEXT PRIMARY KEY, time_utc TEXT NOT NULL, request_id TEXT NOT NULL,
                capability TEXT NOT NULL, fingerprint TEXT NOT NULL, target TEXT NOT NULL,
                status TEXT NOT NULL, scope_id TEXT NOT NULL, detail TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS authority_audit_time ON authority_audit(time_utc DESC);
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT version FROM authority_schema;";
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("Unsupported authority database version. Grants were not loaded.");
        Debug.WriteLine($"[Authorization] READY | PersistentScopes={ReadScopes().Count(x => x.Active)} | Audit=Enabled");
    }

    private SqliteConnection Open()
    {
        SqliteConnection connection = new(_connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    public IReadOnlyList<SegaAuthorityScope> ReadScopes()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT json, revoked_utc FROM authority_scopes ORDER BY rowid DESC;";
        List<SegaAuthorityScope> scopes = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            SegaAuthorityScope scope = JsonSerializer.Deserialize<SegaAuthorityScope>(reader.GetString(0))
                ?? throw new InvalidDataException("Invalid permission record. No automatic permission was granted.");
            if (!Enum.IsDefined(scope.Kind) || scope.CapabilityIds == null)
                throw new InvalidDataException("Invalid permission record.");
            scopes.Add(scope with
            {
                RevokedAtUtc = reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1))
            });
        }
        return scopes;
    }

    // Callers are the trusted Permissions UI and the scoped authorizer after
    // an explicit broker response. Never expose this method as a capability.
    public void Grant(SegaAuthorityScope scope)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO authority_scopes(id,json) VALUES($id,$json);";
        command.Parameters.AddWithValue("$id", scope.Id.ToString("D"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(scope));
        command.ExecuteNonQuery();
        InsertAudit(connection, transaction, Guid.NewGuid(), Guid.Empty, "authority.grant",
            string.Empty, scope.RootPath.Length > 0 ? scope.RootPath : scope.Origin,
            "Granted", scope.Id.ToString("D"), scope.Kind.ToString());
        transaction.Commit();
        Debug.WriteLine($"[Authorization] GRANTED | Scope={scope.Id:D} | Kind={scope.Kind}");
        RaiseChanged();
    }

    public void Revoke(Guid id)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE authority_scopes SET revoked_utc=$now WHERE id=$id AND revoked_utc IS NULL;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        int changed = command.ExecuteNonQuery();
        if (changed > 0)
            InsertAudit(connection, transaction, Guid.NewGuid(), Guid.Empty, "authority.revoke",
                string.Empty, string.Empty, "Revoked", id.ToString("D"), "User revoked this scope.");
        transaction.Commit();
        if (changed > 0) Debug.WriteLine($"[Authorization] REVOKED | Scope={id:D}");
        RaiseChanged();
    }

    public Guid BeginAttempt(SegaCapabilityRequest request, string target, string fingerprint)
    {
        Guid id = Guid.NewGuid();
        using SqliteConnection connection = Open();
        InsertAudit(connection, null, id, request.RequestId, request.CapabilityId,
            fingerprint, target, "Requested", string.Empty, string.Empty);
        return id;
    }

    public void RecordDecision(Guid attemptId, SegaCapabilityAuthorizationDecision decision)
    {
        UpdateAttempt(attemptId, decision.Status == SegaCapabilityAuthorizationStatus.Allowed
            ? "Authorized; result pending" : decision.Status.ToString(),
            decision.ScopeId?.ToString("D") ?? string.Empty,
            decision.Status == SegaCapabilityAuthorizationStatus.Allowed
                ? "Dispatch authorized. This is not proof of completion."
                : "No handler was dispatched.");
    }

    public void FinishAttempt(Guid attemptId, SegaCapabilityResult result)
    {
        UpdateAttempt(attemptId, result.Status.ToString(), result.AuthorizationScopeId?.ToString("D") ?? "",
            $"Changed={result.ChangedSystemState}; OutcomeUncertain={result.OutcomeUncertain}; " +
            $"ExitCode={result.ExitCode?.ToString() ?? "-"}; HttpStatus={result.HttpStatusCode?.ToString() ?? "-"}");
        RaiseChanged();
    }

    private void UpdateAttempt(Guid id, string status, string scopeId, string detail)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE authority_audit SET status=$status,scope_id=$scope,detail=$detail WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$scope", scopeId);
        command.Parameters.AddWithValue("$detail", detail);
        if (command.ExecuteNonQuery() != 1) throw new IOException("The action audit record is unavailable.");
    }

    private static void InsertAudit(SqliteConnection connection, SqliteTransaction? transaction,
        Guid id, Guid requestId, string capability, string fingerprint, string target,
        string status, string scopeId, string detail)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO authority_audit
              (id,time_utc,request_id,capability,fingerprint,target,status,scope_id,detail)
            VALUES($id,$time,$request,$cap,$fp,$target,$status,$scope,$detail);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$request", requestId.ToString("D"));
        command.Parameters.AddWithValue("$cap", capability);
        command.Parameters.AddWithValue("$fp", fingerprint);
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$scope", scopeId);
        command.Parameters.AddWithValue("$detail", detail);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<SegaAuthorityAuditRow> ReadAudit(int limit = 200)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,time_utc,capability,target,status,scope_id,detail
            FROM authority_audit ORDER BY time_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        using SqliteDataReader reader = command.ExecuteReader();
        List<SegaAuthorityAuditRow> rows = [];
        while (reader.Read()) rows.Add(new()
        {
            Id = reader.GetString(0), TimeUtc = reader.GetString(1), Capability = reader.GetString(2),
            Target = reader.GetString(3), Status = reader.GetString(4), ScopeId = reader.GetString(5),
            Detail = reader.GetString(6)
        });
        return rows;
    }

    private void RaiseChanged()
    {
        if (Changed == null) return;
        foreach (Action subscriber in Changed.GetInvocationList().Cast<Action>())
        {
            try { subscriber(); }
            catch (Exception ex) { Debug.WriteLine($"[AuthorizationUI] {ex.GetType().Name}"); }
        }
    }
}
