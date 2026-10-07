/*
 * filename: NIRABridgeCredentialStore.cs
 */

using System.Security.Cryptography;
using System.Text;

namespace NIRAAgent.Integrations.Elvara.Bridge;


// =============================================================
// NIRA BRIDGE APP CREDENTIAL STORE
//
// A registered ELVARA app receives a randomly generated,
// per-install bearer credential stored only in the current
// Windows user's LocalApplicationData.
//
// This is intentionally NOT a hardcoded source secret.
//
// Security boundary:
// - bridge still binds to 127.0.0.1 only
// - chat requires both a registered app route and its bearer token
// - token is never returned by bridge HTTP endpoints
//
// Like other same-user desktop IPC credentials, this does not
// claim to protect against malware already executing as the same
// Windows user. It does prevent unauthenticated localhost callers
// and other user profiles from simply claiming an app identity.
// =============================================================

public sealed class NIRABridgeCredentialStore
{
    private const int TokenBytes =
        48;


    private readonly object
        _sync =
            new();


    private readonly Dictionary<string, string>
        _tokens =
            new(
                StringComparer.OrdinalIgnoreCase);


    private readonly string
        _credentialDirectory;


    public NIRABridgeCredentialStore(
        ElvaraAppRegistry apps)
    {
        ArgumentNullException.ThrowIfNull(
            apps);


        _credentialDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "ELVARA",
                "NIRA",
                "bridge",
                "credentials");


        Directory.CreateDirectory(
            _credentialDirectory);


        foreach (
            ElvaraAppDescriptor app
            in apps.GetAll())
        {
            if (!app.AllowEmbeddedNIRA)
            {
                continue;
            }


            _ =
                GetOrCreateToken(
                    app.AppId);
        }
    }


    public string GetCredentialPath(
        string appId)
    {
        string normalized =
            NormalizeAppId(
                appId);


        return Path.Combine(
            _credentialDirectory,
            normalized +
            ".token");
    }


    public bool ValidateBearer(
        string appId,
        string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(
                authorizationHeader))
        {
            return false;
        }


        const string prefix =
            "Bearer ";


        if (!authorizationHeader.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }


        string presented =
            authorizationHeader[
                prefix.Length..
            ]
            .Trim();


        if (presented.Length ==
            0)
        {
            return false;
        }


        string expected =
            GetOrCreateToken(
                appId);


        byte[] presentedHash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    presented));


        byte[] expectedHash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    expected));


        return CryptographicOperations.FixedTimeEquals(
            presentedHash,
            expectedHash);
    }


    private string GetOrCreateToken(
        string appId)
    {
        string normalized =
            NormalizeAppId(
                appId);


        lock (_sync)
        {
            if (_tokens.TryGetValue(
                    normalized,
                    out string? cached)
                &&
                !string.IsNullOrWhiteSpace(
                    cached))
            {
                return cached;
            }


            string path =
                GetCredentialPath(
                    normalized);


            string token =
                ReadExistingToken(
                    path);


            if (string.IsNullOrWhiteSpace(
                    token))
            {
                token =
                    CreateToken();


                WriteTokenAtomically(
                    path,
                    token);
            }


            _tokens[normalized] =
                token;


            System.Diagnostics.Debug.WriteLine(
                $"[NIRABridgeAuth] READY | App={normalized} | CredentialPath='{path}'");


            return token;
        }
    }


    private static string ReadExistingToken(
        string path)
    {
        try
        {
            if (!File.Exists(
                    path))
            {
                return string.Empty;
            }


            string token =
                File.ReadAllText(
                    path,
                    Encoding.UTF8)
                .Trim();


            return token.Length >=
                40
                ? token
                : string.Empty;
        }
        catch (
            IOException)
        {
            return string.Empty;
        }
        catch (
            UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }


    private static string CreateToken()
    {
        byte[] bytes =
            RandomNumberGenerator.GetBytes(
                TokenBytes);


        return Convert
            .ToBase64String(
                bytes)
            .TrimEnd(
                '=')
            .Replace(
                '+',
                '-')
            .Replace(
                '/',
                '_');
    }


    private static void WriteTokenAtomically(
        string path,
        string token)
    {
        string? directory =
            Path.GetDirectoryName(
                path);


        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new InvalidOperationException(
                "NIRA bridge credential directory is invalid.");
        }


        Directory.CreateDirectory(
            directory);


        string temporary =
            path +
            "." +
            Guid.NewGuid().ToString(
                "N")
            +
            ".tmp";


        File.WriteAllText(
            temporary,
            token +
            Environment.NewLine,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier:
                    false));


        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    temporary);

                return;
            }


            File.Move(
                temporary,
                path);
        }
        finally
        {
            if (File.Exists(
                    temporary))
            {
                try
                {
                    File.Delete(
                        temporary);
                }
                catch
                {
                }
            }
        }
    }


    private static string NormalizeAppId(
        string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            appId);


        string normalized =
            appId
                .Trim()
                .ToLowerInvariant();


        foreach (
            char character
            in normalized)
        {
            if (
                char.IsLetterOrDigit(
                    character)
                ||
                character is
                    '-' or
                    '_' or
                    '.')
            {
                continue;
            }


            throw new InvalidOperationException(
                "ELVARA application ID contains unsupported characters.");
        }


        return normalized;
    }
}
