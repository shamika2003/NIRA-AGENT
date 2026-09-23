using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using NIRAAgent.Capabilities;
using NIRAAgent.Browser;

namespace NIRAAgent.Authorization;

// The checked, canonical request is the request the handler executes.
// Folder permissions govern direct primitives, not the effects of arbitrary
// programs. Execution therefore requires a separate, exact-request approval.
public sealed class NIRACapabilityRequestPolicy
{
    private readonly NIRAAuthorityStore _store;
    private readonly NIRABrowserService _browser;

    public NIRACapabilityRequestPolicy(
        NIRAAuthorityStore store,
        NIRABrowserService browser)
    {
        _store = store;
        _browser = browser;
    }
    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public NIRACapabilityRequest Prepare(NIRACapabilityRequest request)
    {
        Dictionary<string, JsonElement> args = ReadObject(request.Arguments);
        foreach (string key in new[] { "path", "source", "destination", "workingdirectory" })
            if (args.TryGetValue(key, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
                args[key] = JsonSerializer.SerializeToElement(NormalizeLocalPath(RequireText(value, key)));

        if (request.CapabilityId is NIRACapabilityIds.ProcessStart or NIRACapabilityIds.ShellExecute)
        {
            if (!args.TryGetValue("workingdirectory", out JsonElement cwd) || cwd.ValueKind == JsonValueKind.Null)
                args["workingdirectory"] = JsonSerializer.SerializeToElement(NormalizeLocalPath(Environment.CurrentDirectory));
            if (request.CapabilityId == NIRACapabilityIds.ProcessStart)
            {
                args["filename"] = JsonSerializer.SerializeToElement(ResolveExecutable(
                    NIRACapabilityArguments.RequireString(request, "fileName", 32760)));
                args.TryAdd("arguments", JsonSerializer.SerializeToElement(""));
            }
            else
            {
                string shell = (NIRACapabilityArguments.GetOptionalString(request, "shell", 32) ?? "powershell")
                    .ToLowerInvariant();
                args["shell"] = JsonSerializer.SerializeToElement(shell);
                args["__executable"] = JsonSerializer.SerializeToElement(ResolveShellExecutable(shell));
            }
        }

        if (request.CapabilityId is NIRACapabilityIds.HttpRequest or NIRACapabilityIds.HttpDownload)
        {
            Uri uri = ParseUri(NIRACapabilityArguments.RequireString(request, "url", 4096));
            args["url"] = JsonSerializer.SerializeToElement(uri.AbsoluteUri);
            string method = (NIRACapabilityArguments.GetOptionalString(request, "method", 16) ?? "GET").ToUpperInvariant();
            if (method is not ("GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE"))
                throw new InvalidOperationException("Unsupported HTTP method.");
            if (request.CapabilityId == NIRACapabilityIds.HttpRequest)
                args["method"] = JsonSerializer.SerializeToElement(method);
            if (args.TryGetValue("headers", out JsonElement headers) && headers.ValueKind != JsonValueKind.Null)
            {
                Dictionary<string, JsonElement> normalized = ReadObject(headers);
                foreach ((string name, JsonElement value) in normalized)
                {
                    string text = RequireText(value, name);
                    if (name.IndexOfAny(['\r', '\n']) >= 0 || text.IndexOfAny(['\r', '\n']) >= 0)
                        throw new InvalidOperationException("HTTP headers cannot contain newlines.");
                    if (name is "host" or "content-length" or "transfer-encoding" or "connection")
                        throw new InvalidOperationException($"HTTP header '{name}' is controlled by the runtime.");
                }
                args["headers"] = JsonSerializer.SerializeToElement(normalized);
            }
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserSessionOpen)
        {
            string? initialUrl = NIRACapabilityArguments.GetOptionalString(request, "initialUrl", 4096);
            if (!string.IsNullOrWhiteSpace(initialUrl))
                args["initialurl"] = JsonSerializer.SerializeToElement(ParseUri(initialUrl).AbsoluteUri);
        }

        if (request.CapabilityId is NIRACapabilityIds.BrowserNavigate or NIRACapabilityIds.BrowserRequest)
        {
            Uri uri = ParseUri(NIRACapabilityArguments.RequireString(request, "url", 4096));
            args["url"] = JsonSerializer.SerializeToElement(uri.AbsoluteUri);
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserRequest)
        {
            string method = NIRABrowserService.NormalizeHttpMethod(
                NIRACapabilityArguments.GetOptionalString(request, "method", 16));
            args["method"] = JsonSerializer.SerializeToElement(method);

            if (args.TryGetValue("headers", out JsonElement headers) && headers.ValueKind != JsonValueKind.Null)
            {
                Dictionary<string, JsonElement> normalized = ReadObject(headers);
                foreach ((string name, JsonElement value) in normalized)
                {
                    string text = RequireText(value, name);
                    if (name.IndexOfAny(['\r', '\n']) >= 0 || text.IndexOfAny(['\r', '\n']) >= 0)
                        throw new InvalidOperationException("Browser request headers cannot contain newlines.");
                    if (name is "host" or "content-length" or "transfer-encoding" or "connection")
                        throw new InvalidOperationException($"Browser request header '{name}' is controlled by the runtime.");
                    if (IsCredentialHeader(name))
                        throw new InvalidOperationException(
                            $"Browser request header '{name}' is credential-sensitive. Use the Playwright browser session/cookie jar instead of exposing credentials in capability arguments.");
                }
                args["headers"] = JsonSerializer.SerializeToElement(normalized);
            }
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserAuthenticate)
        {
            string? rawPageId = NIRACapabilityArguments.GetOptionalString(request, "pageId", 80);
            Guid pageId;
            if (string.IsNullOrWhiteSpace(rawPageId))
                pageId = _browser.TryGetActiveOwnedPageId() ??
                    throw new InvalidOperationException(
                        "AmbiguousBrowserPage: call browser.current and browser.inspect to select the correct task-owned page; never ask the user for a runtime GUID.");
            else if (!Guid.TryParse(rawPageId, out pageId) || pageId == Guid.Empty)
                throw new InvalidOperationException(
                    "pageId must be a runtime GUID; call browser.current yourself to recover it.");

            if (!_browser.TryGetAuthorizationOrigin(pageId, out string browserOrigin))
                throw new InvalidOperationException(
                    "The browser page does not currently have a grounded HTTP/HTTPS origin. Inspect/navigate the page again before requesting secure authentication.");

            string? usernameRef = NIRACapabilityArguments.GetOptionalString(request, "usernameRef", 80);
            string? passwordRef = NIRACapabilityArguments.GetOptionalString(request, "passwordRef", 80);
            string? submitRef = NIRACapabilityArguments.GetOptionalString(request, "submitRef", 80);

            if (string.IsNullOrWhiteSpace(usernameRef) && string.IsNullOrWhiteSpace(passwordRef))
            {
                // A unique password field from the actual latest inspection is
                // authoritative DOM evidence, not a model-guessed selector.
                if (_browser.TryGetUniqueInspectedLoginRefs(pageId,
                    out string? inspectedUsername, out string? inspectedPassword,
                    out string? inspectedSubmit))
                {
                    usernameRef = inspectedUsername;
                    passwordRef = inspectedPassword;
                    if (string.IsNullOrWhiteSpace(submitRef)) submitRef = inspectedSubmit;
                    if (!string.IsNullOrWhiteSpace(usernameRef))
                        args["usernameref"] = JsonSerializer.SerializeToElement(usernameRef);
                    if (!string.IsNullOrWhiteSpace(passwordRef))
                        args["passwordref"] = JsonSerializer.SerializeToElement(passwordRef);
                    if (!string.IsNullOrWhiteSpace(submitRef))
                        args["submitref"] = JsonSerializer.SerializeToElement(submitRef);
                    Debug.WriteLine($"[BrowserFlow] AUTH REFS RESOLVED | Page={pageId:D} | " +
                        $"Username={usernameRef != null} | Password={passwordRef != null} | Submit={submitRef != null}");
                }
                else
                    throw new InvalidOperationException("MissingLoginFields: no unique inspected password field; inspect the current login form and use its exact field refs. The secure credential UI was not invoked.");
            }

            // Keep live page/origin authorization in this trusted preparation
            // layer, but defer ephemeral DOM-ref validation to the handler's
            // credential-safe preflight. The handler can then return a FRESH
            // inspection when a ref is stale, without ever retrieving a secret.
            // Crucially, do NOT treat this as permission to submit an ungrounded
            // control: EnsureAuthenticationMayProceedAsync validates all refs
            // immediately before the trusted credential broker is consulted.

            args["pageid"] = JsonSerializer.SerializeToElement(pageId.ToString("D"));
            args["__origin"] = JsonSerializer.SerializeToElement(browserOrigin);
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserUpload)
        {
            string? rawPageId = NIRACapabilityArguments.GetOptionalString(request, "pageId", 80);
            Guid pageId;
            if (string.IsNullOrWhiteSpace(rawPageId))
                pageId = _browser.TryGetActiveOwnedPageId() ??
                    throw new InvalidOperationException("AmbiguousBrowserPage: inspect/select your task-owned page; never ask the human for a page GUID.");
            else if (!Guid.TryParse(rawPageId, out pageId) || pageId == Guid.Empty)
                throw new InvalidOperationException("Invalid runtime pageId; call browser.current yourself.");
            if (!_browser.TryGetAuthorizationOrigin(pageId, out string liveOrigin))
                throw new InvalidOperationException("Upload requires a current inspected HTTP/HTTPS browser page ID.");

            string elementRef = NIRACapabilityArguments.RequireString(request, "ref", 80);
            if (!_browser.IsGroundedElementRef(pageId, elementRef))
                throw new InvalidOperationException("Upload target must be grounded by a fresh browser.inspect.");

            string path = NormalizeLocalPath(NIRACapabilityArguments.RequireString(request, "path", 32760));
            ValidatePath(path);
            (string sha256, long bytes) = FingerprintUploadFile(path);
            args["pageid"] = JsonSerializer.SerializeToElement(pageId.ToString("D"));
            args["path"] = JsonSerializer.SerializeToElement(path);
            args["__origin"] = JsonSerializer.SerializeToElement(liveOrigin);
            args["__sha256"] = JsonSerializer.SerializeToElement(sha256);
            args["__bytes"] = JsonSerializer.SerializeToElement(bytes);
        }

        if (IsBrowserPageAction(request.CapabilityId))
        {
            string? rawPageId = NIRACapabilityArguments.GetOptionalString(request, "pageId", 80);
            Guid pageId;
            if (string.IsNullOrWhiteSpace(rawPageId))
                pageId = _browser.TryGetActiveOwnedPageId() ??
                    throw new InvalidOperationException("AmbiguousBrowserPage: inspect/select your task-owned page; never ask the human for an internal GUID.");
            else if (!Guid.TryParse(rawPageId, out pageId) || pageId == Guid.Empty)
                throw new InvalidOperationException("Invalid pageId; call browser.current yourself.");

            if (!_browser.TryGetAuthorizationOrigin(pageId, out string browserOrigin))
                throw new InvalidOperationException(
                    "The browser page does not currently have a grounded HTTP/HTTPS origin. Inspect/navigate the page again before requesting a state-changing browser action.");

            string elementRef =
                NIRACapabilityArguments.RequireString(
                    request,
                    "ref",
                    80);

            if (!_browser.IsGroundedElementRef(pageId, elementRef))
                throw new InvalidOperationException(
                    "The browser element ref is not grounded in the latest browser.inspect result for this page. Re-inspect the page and use an exact returned ref.");

            args["pageid"] = JsonSerializer.SerializeToElement(pageId.ToString("D"));
            args["__origin"] = JsonSerializer.SerializeToElement(browserOrigin);
        }

        if (request.CapabilityId == NIRACapabilityIds.ProcessStop)
        {
            int pid = NIRACapabilityArguments.RequireInteger(request, "pid", 1, int.MaxValue);
            if (pid == Environment.ProcessId) throw new InvalidOperationException("NIRA cannot terminate its own authorization host.");
            using Process process = Process.GetProcessById(pid);
            args["__startticks"] = JsonSerializer.SerializeToElement(process.StartTime.ToUniversalTime().Ticks);
        }
        if (request.CapabilityId == NIRACapabilityIds.DirectoryList)
        {
            string pattern = NIRACapabilityArguments.GetOptionalString(request, "pattern", 256) ?? "*";
            if (pattern.Contains('/') || pattern.Contains('\\') || pattern.Contains(':'))
                throw new InvalidOperationException("Directory patterns must be filenames, not paths.");
        }
        return request with { Arguments = JsonSerializer.SerializeToElement(args) };
    }

    public NIRAAuthorityOperation Describe(NIRACapabilityRequest request, NIRACapabilityRisk risk)
    {
        List<string> paths = [];
        foreach (string name in new[] { "path", "source", "destination" })
        {
            string? path = NIRACapabilityArguments.GetOptionalString(request, name, 32760);
            if (path != null) paths.Add(path);
        }
        // filesystem.locate traverses the requested root. Treat that root
        // exactly like any other filesystem target for protected-directory,
        // sensitive-location and scope checks; a different argument name must
        // never bypass the existing authority boundary.
        if (request.CapabilityId == NIRACapabilityIds.FileLocate)
            paths.Add(NIRACapabilityArguments.RequireString(request, "root", 32760));
        bool sensitive = false;
        foreach (string path in paths)
        {
            ValidatePath(path);
            if (IsWithin(path, _store.ProtectedDirectory) ||
                (request.CapabilityId == NIRACapabilityIds.FileDelete && IsWithin(_store.ProtectedDirectory, path)))
                throw new UnauthorizedAccessException("The authority store is managed only by Permissions.");
            sensitive |= IsCredentialPath(path);
        }
        if (request.CapabilityId == NIRACapabilityIds.FileDelete && paths.Count == 1 &&
            NIRACapabilityArguments.GetBoolean(request, "recursive") && Directory.Exists(paths[0]))
        {
            foreach (string child in EnumerateCheckedTree(paths[0]))
                sensitive |= IsCredentialPath(child);
        }
        string? cwd = NIRACapabilityArguments.GetOptionalString(request, "workingDirectory", 32760);
        if (cwd != null) ValidatePath(cwd);
        foreach (string name in new[] { "fileName", "__executable" })
        {
            string? executable = NIRACapabilityArguments.GetOptionalString(request, name, 32760);
            if (executable != null) ValidateExecutionPath(executable);
        }
        string origin = "";
        string method = "";
        string target = string.Join(" -> ", paths);
        if (request.CapabilityId is NIRACapabilityIds.HttpRequest or NIRACapabilityIds.HttpDownload)
        {
            Uri uri = ParseUri(NIRACapabilityArguments.RequireString(request, "url", 4096));
            origin = uri.GetLeftPart(UriPartial.Authority);
            method = NIRACapabilityArguments.GetOptionalString(request, "method", 16) ?? "GET";
            target = $"{method} {origin}" +
                (paths.Count == 0 ? "" : $" -> {string.Join(" -> ", paths)}");
            JsonElement? headers = NIRACapabilityArguments.GetOptionalObject(request, "headers");
            sensitive |= headers.HasValue && headers.Value.EnumerateObject().Any(h => IsCredentialHeader(h.Name));
            sensitive |= uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Uri.UnescapeDataString(part.Split('=')[0]).ToLowerInvariant())
                .Any(name => name is "token" or "access_token" or "api_key" or "apikey" or "key" or "password" or "secret" or "signature" or "sig");
        }
        if (request.CapabilityId == NIRACapabilityIds.BrowserSessionOpen)
        {
            string profile = NIRACapabilityArguments.GetOptionalString(request, "profile", 40) ?? "default";
            bool headed = NIRACapabilityArguments.GetBoolean(request, "headed");
            string? initialUrl = NIRACapabilityArguments.GetOptionalString(request, "initialUrl", 4096);
            target = $"NIRA browser profile='{profile}' | headed={headed}" +
                (string.IsNullOrWhiteSpace(initialUrl) ? string.Empty : $" | initialUrl={ParseUri(initialUrl).GetLeftPart(UriPartial.Authority)}");
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserNavigate)
        {
            Uri uri = ParseUri(NIRACapabilityArguments.RequireString(request, "url", 4096));
            origin = uri.GetLeftPart(UriPartial.Authority);
            target = $"Browser navigate | {origin}";
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserRecoveryResume)
        {
            string rawPageId = NIRACapabilityArguments.RequireString(request, "pageId", 80);
            if (!Guid.TryParse(rawPageId, out Guid recoveredPageId) || recoveredPageId == Guid.Empty ||
                !_browser.TryGetAuthorizationOrigin(recoveredPageId, out string currentOrigin))
                throw new InvalidOperationException(
                    "Browser recovery requires a live page ID from browser.current/browser.inspect.");
            origin = currentOrigin;
            target = $"Browser recovery GET | currentOrigin={origin} | PageId={recoveredPageId:D}";
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserRequest)
        {
            Uri uri = ParseUri(NIRACapabilityArguments.RequireString(request, "url", 4096));
            origin = uri.GetLeftPart(UriPartial.Authority);
            method = NIRABrowserService.NormalizeHttpMethod(
                NIRACapabilityArguments.GetOptionalString(request, "method", 16));
            target = $"Browser-context HTTP {method} {origin}";

            JsonElement? headers = NIRACapabilityArguments.GetOptionalObject(request, "headers");
            sensitive |= headers.HasValue && headers.Value.EnumerateObject().Any(h => IsCredentialHeader(h.Name));
            sensitive |= HasCredentialQuery(uri);
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserAuthenticate)
        {
            string rawPageId = NIRACapabilityArguments.RequireString(request, "pageId", 80);
            if (!Guid.TryParse(rawPageId, out Guid pageId) || pageId == Guid.Empty)
                throw new InvalidOperationException("Browser pageId is invalid.");

            string preparedOrigin = NIRACapabilityArguments.RequireString(request, "__origin", 4096);
            if (!_browser.TryGetAuthorizationOrigin(pageId, out string liveOrigin) ||
                !string.Equals(preparedOrigin, liveOrigin, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The browser page changed origin before secure authentication. Inspect the current page again.");
            }

            origin = liveOrigin;
            target = $"browser.authenticate | {origin} | PageId={pageId:D}";
        }

        if (request.CapabilityId == NIRACapabilityIds.BrowserUpload)
        {
            Guid pageId = Guid.Parse(NIRACapabilityArguments.RequireString(request, "pageId", 80));
            string preparedOrigin = NIRACapabilityArguments.RequireString(request, "__origin", 4096);
            if (!_browser.TryGetAuthorizationOrigin(pageId, out string liveOrigin) ||
                !string.Equals(preparedOrigin, liveOrigin, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Upload destination changed; re-inspect and request approval again.");

            string filePath = NIRACapabilityArguments.RequireString(request, "path", 32760);
            (string liveHash, long liveBytes) = FingerprintUploadFile(filePath);
            string approvedHash = NIRACapabilityArguments.RequireString(request, "__sha256", 64);
            long approvedBytes = NIRACapabilityArguments.RequireInteger(request, "__bytes", 0, 20 * 1024 * 1024);
            if (liveBytes != approvedBytes || !string.Equals(liveHash, approvedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("File changed since upload preflight; approval must not be reused.");
            origin = liveOrigin;
            target = $"browser.upload | {origin} | {filePath} | bytes={liveBytes} | sha256={liveHash}";
            // File disclosure is an independent boundary: neither a trusted
            // website origin nor a trusted folder grants upload authority.
            sensitive = true;
        }

        if (IsBrowserPageAction(request.CapabilityId))
        {
            string rawPageId = NIRACapabilityArguments.RequireString(request, "pageId", 80);
            if (!Guid.TryParse(rawPageId, out Guid pageId) || pageId == Guid.Empty)
                throw new InvalidOperationException("Browser pageId is invalid.");

            string preparedOrigin = NIRACapabilityArguments.RequireString(request, "__origin", 4096);
            if (!_browser.TryGetAuthorizationOrigin(pageId, out string liveOrigin) ||
                !string.Equals(preparedOrigin, liveOrigin, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The browser page changed origin before dispatch. Inspect the current page and request the action again against fresh evidence.");
            }

            origin = liveOrigin;
            target = $"{request.CapabilityId} | {origin} | PageId={pageId:D}";
        }

        if (request.CapabilityId == NIRACapabilityIds.ProcessStart)
            target = $"{NIRACapabilityArguments.RequireString(request, "fileName")} | cwd={cwd}";
        if (request.CapabilityId == NIRACapabilityIds.ShellExecute)
            target = $"{NIRACapabilityArguments.RequireString(request, "shell")} | cwd={cwd}";
        if (request.CapabilityId == NIRACapabilityIds.ProcessStop)
            target = $"PID={NIRACapabilityArguments.RequireInteger(request, "pid", 1, int.MaxValue)}";

        bool externalVision =
            request.CapabilityId == NIRACapabilityIds.VisionInspect;

        bool secureCredentialUse =
            request.CapabilityId == NIRACapabilityIds.BrowserAuthenticate;

        if (externalVision)
        {
            string evidenceId =
                NIRACapabilityArguments.RequireString(
                    request,
                    "evidenceId",
                    80);

            target =
                $"Ollama cloud visual interpretation | EvidenceId={evidenceId}";
        }

        if (target.Length == 0) target = request.CapabilityId;
        bool execution = request.CapabilityId is NIRACapabilityIds.ProcessStart or NIRACapabilityIds.ShellExecute;
        bool browserAction = IsBrowserOriginScoped(request.CapabilityId) && risk != NIRACapabilityRisk.Observe;
        bool browserStateChangingRequest =
            request.CapabilityId == NIRACapabilityIds.BrowserRequest &&
            risk != NIRACapabilityRisk.Observe;
        string notice = request.CapabilityId == NIRACapabilityIds.BrowserUpload
            ? "This authorizes exposing the exact local file to the listed website origin and its page scripts. Selecting a file can itself trigger an immediate website upload. Confirm the file path, size and SHA-256. Approval is one-time; site clicks and trusted folders never grant file disclosure. NIRA will NOT separately click Submit in this operation; inspect the result before any further action."
            : sensitive
            ? "This request may access credentials or send private data. Approval applies once; its payload is not written to the audit log."
            : secureCredentialUse
                ? $"This allows NIRA's trusted credential broker to use an approved login for {origin}. The secret never enters model-visible capability arguments, cognition, or the normal action audit. A remembered site grant can permit future credential use on this exact origin until revoked."
            : externalVision
                ? "This sends the grounded screenshot pixels to NIRA's configured Ollama cloud vision model for interpretation. Remembering this permission allows future vision.inspect captures to be sent for visual analysis until you revoke the permission."
                : browserAction
                    ? $"This lets NIRA perform the listed browser action on {origin}. Remembering creates a permission for this browser capability on that website origin; it does not expose stored cookies/passwords to cognition and does not authorize other origins."
                    : browserStateChangingRequest
                        ? $"This sends an authenticated browser-context {method} request to {origin}. Because this may change account/server state, remembering permits only this exact canonical request again; it does not create a broad authenticated API grant for the site."
                        : execution
                        ? "This command runs with your Windows account's access. Its working folder is not a sandbox. A trusted project can optionally include a bounded build/test or read-only Git execution profile; commands outside those profiles still require their own approval."
                        : request.CapabilityId == NIRACapabilityIds.ProcessStop
                            ? "This stops the identified process. Approval applies once and checks that the PID still identifies the same process."
                            : "Only the listed capability and matching scope are authorized. Other operations keep their own permission checks.";
        string suggestedRoot = paths.Count == 1
            ? request.CapabilityId == NIRACapabilityIds.DirectoryCreate
                ? (Directory.Exists(paths[0])
                    ? paths[0]
                    : Path.GetDirectoryName(paths[0]) ?? "")
                : Path.GetDirectoryName(paths[0]) ?? ""
            : paths.Count > 1 ? Path.GetDirectoryName(paths[^1]) ?? "" : "";
        return new()
        {
            Request = request, Risk = risk, Fingerprint = Fingerprint(request),
            Paths = paths.ToArray(), Origin = origin, Method = method, Target = target,
            WorkingDirectory = cwd ?? string.Empty,
            Sensitive = sensitive, RequiresCredentialUse = secureCredentialUse,
            RequiresExplicitAuthorization = externalVision || request.CapabilityId == NIRACapabilityIds.BrowserUpload,
            CanRemember = !sensitive && request.CapabilityId != NIRACapabilityIds.ProcessStop,
            SuggestedRoot = suggestedRoot, Notice = notice
        };
    }

    public void ValidatePath(string path)
    {
        string canonical = NormalizeLocalPath(path);
        if (!string.Equals(canonical, path, PathComparison))
            throw new InvalidOperationException("The canonical path changed before dispatch.");
        string root = Path.GetPathRoot(path)!;
        string current = root;
        foreach (string component in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked files and junctions require a direct canonical target, not a path through a link.");
            if ((attributes & FileAttributes.Directory) == 0 && OperatingSystem.IsWindows())
                RejectHardLink(current);
        }
    }

    // Executables are authorized as exact execution requests rather than by
    // filesystem mutation scopes. Windows servicing legitimately uses hard
    // links for some system binaries, so rejecting every multiply-linked
    // executable incorrectly blocks programs such as Notepad/PowerShell.
    // We still require a canonical direct path and reject reparse traversal.
    private static void ValidateExecutionPath(string path)
    {
        string canonical = NormalizeLocalPath(path);
        if (!string.Equals(canonical, path, PathComparison))
            throw new InvalidOperationException("The executable path changed before dispatch.");
        if (!File.Exists(canonical))
            throw new FileNotFoundException("Executable does not exist.", canonical);

        string root = Path.GetPathRoot(canonical)!;
        string current = root;
        foreach (string component in canonical[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Executable paths cannot traverse linked files or junctions.");
        }
    }

    private IEnumerable<string> EnumerateCheckedTree(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);
        int count = 0;
        while (pending.Count > 0)
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (++count > 10000) throw new InvalidOperationException("Recursive deletion is limited to 10,000 checked entries. Choose a smaller target.");
                ValidatePath(child);
                if (IsWithin(child, _store.ProtectedDirectory))
                    throw new UnauthorizedAccessException("Recursive deletion cannot include the authority store.");
                yield return child;
                if (Directory.Exists(child)) pending.Push(child);
            }
        }
    }

    public static string NormalizeLocalPath(string value)
    {
        string input = Environment.ExpandEnvironmentVariables(value.Trim());
        if (input.Length == 0 || input.Contains('\0')) throw new InvalidOperationException("A local path is required.");
        if (OperatingSystem.IsWindows())
        {
            if (input.StartsWith(@"\\") || input.StartsWith("//"))
                throw new InvalidOperationException("Device and network paths are not supported by local folder grants.");
            if (input.Contains('~'))
                throw new InvalidOperationException("Use the full path instead of a Windows short-name alias.");
            foreach (string part in input.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
                if (part is not ("." or "..") && (part.EndsWith('.') || part.EndsWith(' ')))
                    throw new InvalidOperationException("Paths cannot contain components ending in a dot or space.");
        }
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        if (full.Length > 32760 || full.Contains('%')) throw new InvalidOperationException("Invalid or unresolved filesystem path.");
        if (OperatingSystem.IsWindows() && full.IndexOf(':', 2) >= 0)
            throw new InvalidOperationException("Alternate data streams are not supported by folder grants.");
        return full;
    }

    public static bool IsWithin(string path, string root)
    {
        string p = Path.TrimEndingDirectorySeparator(path);
        string r = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(p, r, PathComparison) ||
            p.StartsWith(Path.EndsInDirectorySeparator(r) ? r : r + Path.DirectorySeparatorChar, PathComparison);
    }

    public static string ResolveExecutable(string value)
    {
        if (Path.IsPathFullyQualified(value))
        {
            string full = NormalizeLocalPath(value);
            if (!File.Exists(full)) throw new FileNotFoundException("Executable does not exist.", full);
            return full;
        }
        if (value.Contains('/') || value.Contains('\\'))
            throw new InvalidOperationException("Executable paths must be fully qualified, or a name found on PATH.");
        string[] suffixes = OperatingSystem.IsWindows() && !Path.HasExtension(value) ? [".exe", ".com"] : [""];
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory.Trim('"'))) continue;
            foreach (string suffix in suffixes)
            {
                string candidate = Path.Combine(directory.Trim('"'), value + suffix);
                if (File.Exists(candidate)) return NormalizeLocalPath(candidate);
            }
        }
        throw new FileNotFoundException($"Executable '{value}' was not found on PATH. Supply its full path.");
    }

    public static string ResolveShellExecutable(string shell) => shell switch
    {
        "powershell" when OperatingSystem.IsWindows() => ResolveExecutable(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")),
        "cmd" when OperatingSystem.IsWindows() => ResolveExecutable(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")),
        "pwsh" => ResolveExecutable(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh"),
        _ => throw new InvalidOperationException("This shell is unavailable on the current operating system.")
    };

    public static Uri ParseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("Use an HTTP/HTTPS URL without embedded credentials.");
        return new UriBuilder(uri) { Fragment = "" }.Uri;
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Expected a JSON object.");
        Dictionary<string, JsonElement> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!result.TryAdd(property.Name.ToLowerInvariant(), property.Value.Clone()))
                throw new InvalidOperationException($"Duplicate argument '{property.Name}'.");
        return result.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value);
    }
    private static string RequireText(JsonElement value, string name) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new InvalidOperationException($"'{name}' must be text.");
    public static string Fingerprint(NIRACapabilityRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.BuildSignature()))).ToLowerInvariant();

    private static bool IsCredentialHeader(string name) => name.ToLowerInvariant() is
        "authorization" or "proxy-authorization" or "cookie" or "x-api-key" or "api-key" or "x-auth-token";
    private static bool IsCredentialPath(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/');
        string file = parts[^1].ToLowerInvariant();
        return parts.Any(x => x.ToLowerInvariant() is ".ssh" or ".aws" or ".azure" or "gcloud") ||
            file is ".env" or ".git-credentials" or "credentials" or "credentials.json" or "login data" or "cookies" ||
            file.StartsWith(".env.", StringComparison.Ordinal) ||
            Path.GetExtension(file) is ".pem" or ".key" or ".pfx" or ".p12";
    }

    // Fixed upper bound protects NIRA's runtime from loading enormous uploads.
    // All approval fingerprints bind exact file bytes, path and destination.
    private static (string Sha256, long Bytes) FingerprintUploadFile(string path)
    {
        const long maximumUploadBytes = 20L * 1024 * 1024;
        FileInfo info = new(path);
        if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0)
            throw new FileNotFoundException("Upload source is not a regular existing file.", path);
        if (info.Length > maximumUploadBytes)
            throw new InvalidOperationException("This upload capability currently supports files up to 20 MiB.");
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using SHA256 algorithm = SHA256.Create();
        string digest = Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant();
        if (stream.Length > maximumUploadBytes || stream.Length != info.Length)
            throw new IOException("The source file changed during upload preflight.");
        return (digest, stream.Length);
    }

    public static bool IsBrowserPageAction(string capabilityId) =>
        capabilityId is NIRACapabilityIds.BrowserClick
            or NIRACapabilityIds.BrowserFill
            or NIRACapabilityIds.BrowserSelect
            or NIRACapabilityIds.BrowserDownload;

    public static bool IsBrowserOriginScoped(string capabilityId) =>
        IsBrowserPageAction(capabilityId) ||
        capabilityId == NIRACapabilityIds.BrowserAuthenticate;

    private static bool HasCredentialQuery(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Uri.UnescapeDataString(part.Split('=')[0]).ToLowerInvariant())
            .Any(name => name is "token" or "access_token" or "api_key" or "apikey" or "key" or "password" or "secret" or "signature" or "sig");

    public static string BuildPreview(NIRACapabilityRequest request)
    {
        // This exact payload is displayed only in the trusted UI, never saved in
        // the audit trail or inserted into model context as a permission grant.
        return JsonSerializer.Serialize(request.Arguments, new JsonSerializerOptions { WriteIndented = true });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    private static void RejectHardLink(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out FileInformation info))
            throw new IOException("Unable to verify the target file identity.");
        if (info.Links > 1)
            throw new UnauthorizedAccessException("Files with multiple hard links are not accepted by direct filesystem grants.");
    }
}
