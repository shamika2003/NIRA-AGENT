using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Data.Sqlite;

namespace NIRAAgent.Authorization;

// Secret bytes are stored by Windows Credential Manager under the current
// Windows user. The SQLite file contains only non-secret metadata so cognition,
// logs and the normal authority audit never receive password material.
public sealed class NIRACredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 2560;

    private readonly string _connectionString;

    public string DatabasePath { get; }

    public NIRACredentialStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NIRAAgent",
                "credentials",
                "NIRA-credentials.db"))
    {
    }

    public NIRACredentialStore(string databasePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "NIRA's secure credential store currently requires Windows Credential Manager.");
        }

        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        _connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = 5
            }
            .ToString();

        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;

            CREATE TABLE IF NOT EXISTS credentials (
                id TEXT PRIMARY KEY,
                origin TEXT NOT NULL,
                account_label TEXT NOT NULL,
                username TEXT NOT NULL,
                target_name TEXT NOT NULL UNIQUE,
                created_utc TEXT NOT NULL,
                last_used_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS credentials_origin_label
            ON credentials(origin, account_label COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS credential_login_routes (
                credential_id TEXT PRIMARY KEY,
                origin TEXT NOT NULL,
                login_route TEXT NOT NULL,
                last_observed_utc TEXT NOT NULL,
                FOREIGN KEY(credential_id) REFERENCES credentials(id)
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<NIRACredentialMetadata> ReadForOrigin(string origin)
    {
        string normalizedOrigin = NormalizeOrigin(origin);

        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, origin, account_label, username, created_utc, last_used_utc
            FROM credentials
            WHERE origin = $origin COLLATE NOCASE
            ORDER BY last_used_utc DESC, created_utc DESC;
            """;
        command.Parameters.AddWithValue("$origin", normalizedOrigin);

        List<NIRACredentialMetadata> values = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(
                new NIRACredentialMetadata
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Origin = reader.GetString(1),
                    AccountLabel = reader.GetString(2),
                    Username = reader.GetString(3),
                    CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(4)),
                    LastUsedAtUtc = DateTimeOffset.Parse(reader.GetString(5))
                });
        }

        return values;
    }

    // Only non-secret metadata is returned. This is safe for NIRA's account
    // discovery, unlike reading a credential material/secret.
    public IReadOnlyList<NIRACredentialMetadata> ReadAllMetadata(int limit = 40)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, origin, account_label, username, created_utc, last_used_utc
            FROM credentials ORDER BY last_used_utc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        using SqliteDataReader reader = command.ExecuteReader();
        List<NIRACredentialMetadata> found = [];
        while (reader.Read()) found.Add(new NIRACredentialMetadata
        {
            Id = Guid.Parse(reader.GetString(0)), Origin = reader.GetString(1),
            AccountLabel = reader.GetString(2), Username = reader.GetString(3),
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(4)),
            LastUsedAtUtc = DateTimeOffset.Parse(reader.GetString(5))
        });
        return found;
    }

    // Record the ACTUAL authorized login page, never an invented URL and never
    // an URL query, fragment, username or password. An observed route is not
    // evidence that the website accepted credentials.
    public void RecordObservedLoginRoute(Guid credentialId, string origin, string loginUrl)
    {
        if (credentialId == Guid.Empty) return; // Use-once credentials stay transient.
        string expectedOrigin = NormalizeOrigin(origin);
        if (!Uri.TryCreate(loginUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("https" or "http") ||
            !string.Equals(uri.GetLeftPart(UriPartial.Authority), expectedOrigin,
                StringComparison.OrdinalIgnoreCase))
            return;
        UriBuilder safe = new(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO credential_login_routes(credential_id, origin, login_route, last_observed_utc)
            SELECT id,origin,$route,$at FROM credentials
            WHERE id=$id AND origin=$origin COLLATE NOCASE
            ON CONFLICT(credential_id) DO UPDATE SET
              login_route=excluded.login_route,
              last_observed_utc=excluded.last_observed_utc;
            """;
        command.Parameters.AddWithValue("$id", credentialId.ToString("D"));
        command.Parameters.AddWithValue("$origin", expectedOrigin);
        command.Parameters.AddWithValue("$route", safe.Uri.AbsoluteUri);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public string? ReadObservedLoginRoute(Guid credentialId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT login_route FROM credential_login_routes WHERE credential_id=$id;";
        command.Parameters.AddWithValue("$id", credentialId.ToString("D"));
        return command.ExecuteScalar() as string;
    }

    public NIRACredentialMetadata Save(
        string origin,
        string accountLabel,
        string username,
        string secret)
    {
        string normalizedOrigin = NormalizeOrigin(origin);
        string normalizedLabel = NormalizeAccountLabel(accountLabel, username);
        string normalizedUsername = NormalizeUsername(username);

        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException("A credential secret cannot be empty.");
        }

        if (Encoding.Unicode.GetByteCount(secret) > MaximumCredentialBlobBytes)
        {
            throw new InvalidOperationException(
                "Credential secret is too large for Windows Credential Manager.");
        }

        NIRACredentialMetadata? existing =
            ReadForOrigin(normalizedOrigin)
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.AccountLabel,
                            normalizedLabel,
                            StringComparison.OrdinalIgnoreCase));

        Guid id = existing?.Id ?? Guid.NewGuid();
        string targetName = BuildTargetName(id);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset created = existing?.CreatedAtUtc ?? now;

        WriteWindowsCredential(targetName, normalizedUsername, secret);

        try
        {
            using SqliteConnection connection = Open();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO credentials
                    (id, origin, account_label, username, target_name, created_utc, last_used_utc)
                VALUES
                    ($id, $origin, $label, $username, $target, $created, $used)
                ON CONFLICT(id) DO UPDATE SET
                    origin = excluded.origin,
                    account_label = excluded.account_label,
                    username = excluded.username,
                    target_name = excluded.target_name,
                    last_used_utc = excluded.last_used_utc;
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$origin", normalizedOrigin);
            command.Parameters.AddWithValue("$label", normalizedLabel);
            command.Parameters.AddWithValue("$username", normalizedUsername);
            command.Parameters.AddWithValue("$target", targetName);
            command.Parameters.AddWithValue("$created", created.ToString("O"));
            command.Parameters.AddWithValue("$used", now.ToString("O"));
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch
        {
            if (existing == null)
            {
                DeleteWindowsCredential(targetName, ignoreMissing: true);
            }

            throw;
        }

        return new NIRACredentialMetadata
        {
            Id = id,
            Origin = normalizedOrigin,
            AccountLabel = normalizedLabel,
            Username = normalizedUsername,
            CreatedAtUtc = created,
            LastUsedAtUtc = now
        };
    }

    public NIRACredentialMaterial Read(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Credential ID is required.", nameof(id));
        }

        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT origin, account_label, username, target_name, created_utc, last_used_utc
            FROM credentials
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new KeyNotFoundException("The requested stored credential does not exist.");
        }

        string origin = reader.GetString(0);
        string accountLabel = reader.GetString(1);
        string username = reader.GetString(2);
        string targetName = reader.GetString(3);
        DateTimeOffset created = DateTimeOffset.Parse(reader.GetString(4));

        string secret = ReadWindowsCredential(targetName, out string storedUsername);
        if (!string.IsNullOrWhiteSpace(storedUsername))
        {
            username = storedUsername;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Touch(connection, id, now);

        return new NIRACredentialMaterial
        {
            Metadata = new NIRACredentialMetadata
            {
                Id = id,
                Origin = origin,
                AccountLabel = accountLabel,
                Username = username,
                CreatedAtUtc = created,
                LastUsedAtUtc = now
            },
            Username = username,
            Secret = secret
        };
    }

    public void Delete(Guid id)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        string? targetName;
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT target_name FROM credentials WHERE id=$id;";
            read.Parameters.AddWithValue("$id", id.ToString("D"));
            targetName = read.ExecuteScalar() as string;
        }

        if (targetName == null)
        {
            transaction.Rollback();
            return;
        }

        DeleteWindowsCredential(targetName, ignoreMissing: true);

        using (SqliteCommand deleteRoute = connection.CreateCommand())
        {
            deleteRoute.Transaction = transaction;
            deleteRoute.CommandText = "DELETE FROM credential_login_routes WHERE credential_id=$id;";
            deleteRoute.Parameters.AddWithValue("$id", id.ToString("D"));
            deleteRoute.ExecuteNonQuery();
        }
        using SqliteCommand delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM credentials WHERE id=$id;";
        delete.Parameters.AddWithValue("$id", id.ToString("D"));
        delete.ExecuteNonQuery();
        transaction.Commit();
    }

    public static string NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin?.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Credential use requires an absolute HTTP/HTTPS website origin.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string NormalizeAccountLabel(string accountLabel, string username)
    {
        string value = accountLabel?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = username?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            value = "default";
        }

        if (value.Length > 160)
        {
            throw new InvalidOperationException("Credential account label is too long.");
        }

        return value;
    }

    private static string NormalizeUsername(string username)
    {
        string value = username?.Trim() ?? string.Empty;
        if (value.Length > 512)
        {
            throw new InvalidOperationException("Credential username is too long.");
        }

        return value;
    }

    private static string BuildTargetName(Guid id) =>
        $"NIRAAgent/{id:D}";

    private SqliteConnection Open()
    {
        SqliteConnection connection = new(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void Touch(
        SqliteConnection connection,
        Guid id,
        DateTimeOffset now)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE credentials SET last_used_utc=$used WHERE id=$id;";
        command.Parameters.AddWithValue("$used", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static void WriteWindowsCredential(
        string targetName,
        string username,
        string secret)
    {
        byte[] secretBytes = Encoding.Unicode.GetBytes(secret);
        if (secretBytes.Length > MaximumCredentialBlobBytes)
        {
            throw new InvalidOperationException(
                "Credential secret is too large for Windows Credential Manager.");
        }

        IntPtr blob = IntPtr.Zero;

        try
        {
            blob = Marshal.AllocHGlobal(secretBytes.Length);
            Marshal.Copy(secretBytes, 0, blob, secretBytes.Length);

            CREDENTIAL credential = new()
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = targetName,
                Comment = "NIRA secure website credential",
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = username
            };

            if (!CredWriteW(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"Windows Credential Manager rejected the credential. Win32={Marshal.GetLastWin32Error()}.");
            }
        }
        finally
        {
            Array.Clear(secretBytes, 0, secretBytes.Length);

            if (blob != IntPtr.Zero)
            {
                for (int i = 0; i < secretBytes.Length; i++)
                {
                    Marshal.WriteByte(blob, i, 0);
                }

                Marshal.FreeHGlobal(blob);
            }
        }
    }

    private static string ReadWindowsCredential(
        string targetName,
        out string username)
    {
        if (!CredReadW(targetName, CredTypeGeneric, 0, out IntPtr pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                throw new InvalidOperationException(
                    "Credential metadata exists but the Windows Credential Manager secret is missing.");
            }

            throw new InvalidOperationException(
                $"Windows Credential Manager could not read the credential. Win32={error}.");
        }

        try
        {
            CREDENTIAL credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            username = credential.UserName ?? string.Empty;

            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            int blobSize = checked((int)credential.CredentialBlobSize);
            byte[] bytes = new byte[blobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static void DeleteWindowsCredential(
        string targetName,
        bool ignoreMissing)
    {
        if (CredDeleteW(targetName, CredTypeGeneric, 0))
        {
            return;
        }

        int error = Marshal.GetLastWin32Error();
        if (ignoreMissing && error == ErrorNotFound)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Windows Credential Manager could not delete the credential. Win32={error}.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(
        [In] ref CREDENTIAL credential,
        uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(
        string target,
        uint type,
        uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}


