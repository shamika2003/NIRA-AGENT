/*
 * filename: NIRALocalBridgeService.cs
 */

using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Mind;

namespace NIRAAgent.Integrations.Elvara.Bridge;


// =============================================================
// NIRA LOCAL BRIDGE SERVICE
//
// Localhost entry point for trusted registered ELVARA products.
//
// Applications do not own an LLM connection. Their Ask-NIRA
// surfaces send the user utterance + UI reference context here,
// and this service routes the request into the existing NIRA
// mind runtime.
// =============================================================

public sealed class NIRALocalBridgeService
    : BackgroundService
{
    private const int MaximumRequestBodyBytes =
        64 * 1024;


    private const int MaximumMessageCharacters =
        12000;


    private readonly ElvaraAppRegistry
        _apps;


    private readonly NIRAMindRuntime
        _mind;


    private readonly HttpListener
        _listener =
            new();


    private readonly JsonSerializerOptions
        _jsonOptions =
            new()
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,

                PropertyNameCaseInsensitive =
                    true,

                WriteIndented =
                    false
            };


    private volatile bool
        _started;


    public NIRALocalBridgeService(
        ElvaraAppRegistry apps,
        NIRAMindRuntime mind)
    {
        _apps =
            apps
            ?? throw new ArgumentNullException(
                nameof(apps));


        _mind =
            mind
            ?? throw new ArgumentNullException(
                nameof(mind));


        _listener.Prefixes.Add(
            NIRABridgeOptions.ListenerPrefix);
    }


    public bool IsRunning =>
        _started
        &&
        _listener.IsListening;


    // =========================================================
    // HOSTED SERVICE
    // =========================================================

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            _listener.Start();


            _started =
                true;


            System.Diagnostics.Debug.WriteLine(
                $"[NIRABridge] LISTENING | {NIRABridgeOptions.BaseUrl}");


            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;


                try
                {
                    context =
                        await _listener
                            .GetContextAsync()
                            .WaitAsync(
                                stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException)
                    when (
                        stoppingToken.IsCancellationRequested
                        ||
                        !_listener.IsListening)
                {
                    break;
                }
                catch (ObjectDisposedException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }


                _ =
                    HandleContextSafelyAsync(
                        context,
                        stoppingToken);
            }
        }
        finally
        {
            _started =
                false;


            if (_listener.IsListening)
            {
                try
                {
                    _listener.Stop();
                }
                catch (HttpListenerException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }


            System.Diagnostics.Debug.WriteLine(
                "[NIRABridge] STOPPED");
        }
    }


    public override async Task StopAsync(
        CancellationToken cancellationToken)
    {
        _started =
            false;


        if (_listener.IsListening)
        {
            try
            {
                _listener.Stop();
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }


        await base.StopAsync(
            cancellationToken);
    }


    public override void Dispose()
    {
        try
        {
            _listener.Close();
        }
        catch
        {
        }


        base.Dispose();
    }


    // =========================================================
    // REQUEST HANDLING
    // =========================================================

    private async Task HandleContextSafelyAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleContextAsync(
                context,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            TryClose(
                context.Response);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(
                context.Response,
                HttpStatusCode.BadRequest,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "invalid_json",

                    Message =
                        "The NIRA bridge request body is not valid JSON."
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[NIRABridge] REQUEST_FAILED | {ex.GetType().Name}: {ex.Message}");


            await WriteJsonAsync(
                context.Response,
                HttpStatusCode.InternalServerError,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "bridge_error",

                    Message =
                        "The NIRA local bridge could not process the request."
                },
                CancellationToken.None);
        }
    }


    private async Task HandleContextAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        HttpListenerRequest request =
            context.Request;


        HttpListenerResponse response =
            context.Response;


        ApplySecurityHeaders(
            response);


        if (!IsLoopbackRequest(
                request))
        {
            await WriteJsonAsync(
                response,
                HttpStatusCode.Forbidden,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "loopback_required",

                    Message =
                        "NIRA's ELVARA bridge accepts localhost requests only."
                },
                cancellationToken);

            return;
        }


        string method =
            request.HttpMethod
                .Trim()
                .ToUpperInvariant();


        string path =
            NormalizePath(
                request.Url?.AbsolutePath);


        if (
            method ==
                "GET"
            &&
            path ==
                "/health")
        {
            await WriteJsonAsync(
                response,
                HttpStatusCode.OK,
                new NIRABridgeHealthResponse(),
                cancellationToken);

            return;
        }


        if (
            method ==
                "GET"
            &&
            path ==
                "/v1/apps")
        {
            NIRABridgeAppResponse[] apps =
                _apps
                    .GetAll()
                    .Select(
                        ToBridgeApp)
                    .ToArray();


            await WriteJsonAsync(
                response,
                HttpStatusCode.OK,
                new NIRABridgeAppsResponse
                {
                    Apps =
                        apps
                },
                cancellationToken);

            return;
        }


        if (
            method ==
                "POST"
            &&
            TryParseChatPath(
                path,
                out string chatAppId))
        {
            await HandleChatAsync(
                request,
                response,
                chatAppId,
                cancellationToken);

            return;
        }


        if (
            method ==
                "GET"
            &&
            path.StartsWith(
                "/v1/apps/",
                StringComparison.OrdinalIgnoreCase))
        {
            string encodedAppId =
                path[
                    "/v1/apps/".Length..
                ];


            if (
                string.IsNullOrWhiteSpace(
                    encodedAppId)
                ||
                encodedAppId.Contains(
                    '/'))
            {
                await WriteNotFoundAsync(
                    response,
                    cancellationToken);

                return;
            }


            string appId =
                Uri.UnescapeDataString(
                    encodedAppId);


            if (!_apps.TryGet(
                    appId,
                    out ElvaraAppDescriptor? app)
                ||
                app ==
                    null)
            {
                await WriteJsonAsync(
                    response,
                    HttpStatusCode.NotFound,
                    new NIRABridgeErrorResponse
                    {
                        Error =
                            "unknown_app",

                        Message =
                            $"'{appId}' is not a registered ELVARA application."
                    },
                    cancellationToken);

                return;
            }


            await WriteJsonAsync(
                response,
                HttpStatusCode.OK,
                ToBridgeApp(
                    app),
                cancellationToken);

            return;
        }


        await WriteNotFoundAsync(
            response,
            cancellationToken);
    }


    // =========================================================
    // CHAT
    // =========================================================

    private async Task HandleChatAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string appId,
        CancellationToken cancellationToken)
    {
        if (!_apps.TryGet(
                appId,
                out ElvaraAppDescriptor? app)
            ||
            app ==
                null)
        {
            await WriteJsonAsync(
                response,
                HttpStatusCode.NotFound,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "unknown_app",

                    Message =
                        $"'{appId}' is not a registered ELVARA application."
                },
                cancellationToken);

            return;
        }


        if (!app.AllowEmbeddedNIRA)
        {
            await WriteJsonAsync(
                response,
                HttpStatusCode.Forbidden,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "embedded_nira_disabled",

                    Message =
                        $"Embedded NIRA access is disabled for '{app.AppId}'."
                },
                cancellationToken);

            return;
        }


        if (
            request.ContentLength64 >
                MaximumRequestBodyBytes)
        {
            await WriteJsonAsync(
                response,
                HttpStatusCode.RequestEntityTooLarge,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "request_too_large",

                    Message =
                        "The NIRA bridge request body is too large."
                },
                cancellationToken);

            return;
        }


        using StreamReader reader =
            new(
                request.InputStream,
                request.ContentEncoding
                    ??
                    Encoding.UTF8,
                detectEncodingFromByteOrderMarks:
                    true,
                leaveOpen:
                    false);


        string body =
            await reader.ReadToEndAsync(
                cancellationToken);


        if (Encoding.UTF8.GetByteCount(
                body) >
            MaximumRequestBodyBytes)
        {
            await WriteJsonAsync(
                response,
                HttpStatusCode.RequestEntityTooLarge,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "request_too_large",

                    Message =
                        "The NIRA bridge request body is too large."
                },
                cancellationToken);

            return;
        }


        NIRABridgeChatRequest? chatRequest =
            JsonSerializer.Deserialize<NIRABridgeChatRequest>(
                body,
                _jsonOptions);


        if (
            chatRequest ==
                null
            ||
            string.IsNullOrWhiteSpace(
                chatRequest.Message))
        {
            await WriteJsonAsync(
                response,
                HttpStatusCode.BadRequest,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "message_required",

                    Message =
                        "A non-empty message is required."
                },
                cancellationToken);

            return;
        }


        string message =
            chatRequest.Message.Trim();


        if (message.Length >
            MaximumMessageCharacters)
        {
            await WriteJsonAsync(
                response,
                HttpStatusCode.BadRequest,
                new NIRABridgeErrorResponse
                {
                    Error =
                        "message_too_long",

                    Message =
                        $"Message exceeds the {MaximumMessageCharacters} character limit."
                },
                cancellationToken);

            return;
        }


        NIRAExternalAppContext appContext =
            new()
            {
                AppId =
                    app.AppId,

                Surface =
                    NormalizeContextValue(
                        chatRequest.Surface,
                        "embedded",
                        120),

                Page =
                    NormalizeContextValue(
                        chatRequest.Page,
                        string.Empty,
                        160),

                SelectedEntity =
                    NormalizeContextValue(
                        chatRequest.SelectedEntity,
                        string.Empty,
                        160)
            };


        StringBuilder reply =
            new();


        string speechFallback =
            string.Empty;


        Guid? runId =
            null;


        await foreach (
            NIRAOutputChunk chunk
            in _mind.ProcessExternalAppMessageAsync(
                message,
                appContext,
                cancellationToken))
        {
            if (chunk.RunId !=
                Guid.Empty)
            {
                runId =
                    chunk.RunId;
            }


            if (
                chunk.Type ==
                    NIRAOutputChunkType.Text
                &&
                !string.IsNullOrWhiteSpace(
                    chunk.Content))
            {
                reply.Append(
                    chunk.Content);
            }


            if (
                chunk.Type ==
                    NIRAOutputChunkType.Text
                &&
                !string.IsNullOrWhiteSpace(
                    chunk.SpeechContent))
            {
                speechFallback =
                    chunk.SpeechContent.Trim();
            }
        }


        string finalReply =
            reply
                .ToString()
                .Trim();


        if (
            string.IsNullOrWhiteSpace(
                finalReply)
            &&
            !string.IsNullOrWhiteSpace(
                speechFallback))
        {
            finalReply =
                speechFallback;
        }


        await WriteJsonAsync(
            response,
            HttpStatusCode.OK,
            new NIRABridgeChatResponse
            {
                AppId =
                    app.AppId,

                Reply =
                    finalReply,

                RunId =
                    runId,

                Scope =
                    $"app:{app.AppId}"
            },
            cancellationToken);
    }


    // =========================================================
    // RESPONSE HELPERS
    // =========================================================

    private async Task WriteNotFoundAsync(
        HttpListenerResponse response,
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(
            response,
            HttpStatusCode.NotFound,
            new NIRABridgeErrorResponse
            {
                Error =
                    "not_found",

                Message =
                    "The requested NIRA bridge route does not exist."
            },
            cancellationToken);
    }


    private async Task WriteJsonAsync<T>(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        byte[] body =
            JsonSerializer.SerializeToUtf8Bytes(
                value,
                _jsonOptions);


        response.StatusCode =
            (int)statusCode;


        response.ContentType =
            "application/json; charset=utf-8";


        response.ContentEncoding =
            Encoding.UTF8;


        response.ContentLength64 =
            body.LongLength;


        try
        {
            await response
                .OutputStream
                .WriteAsync(
                    body,
                    cancellationToken);
        }
        finally
        {
            TryClose(
                response);
        }
    }


    private static void ApplySecurityHeaders(
        HttpListenerResponse response)
    {
        response.Headers[
            "Cache-Control"] =
                "no-store";


        response.Headers[
            "X-Content-Type-Options"] =
                "nosniff";
    }


    private static void TryClose(
        HttpListenerResponse response)
    {
        try
        {
            response.OutputStream.Close();
        }
        catch
        {
        }


        try
        {
            response.Close();
        }
        catch
        {
        }
    }


    // =========================================================
    // REQUEST VALIDATION
    // =========================================================

    private static bool IsLoopbackRequest(
        HttpListenerRequest request)
    {
        IPEndPoint? remote =
            request.RemoteEndPoint;


        return remote !=
                null
            &&
            IPAddress.IsLoopback(
                remote.Address);
    }


    private static bool TryParseChatPath(
        string path,
        out string appId)
    {
        appId =
            string.Empty;


        const string prefix =
            "/v1/apps/";


        const string suffix =
            "/chat";


        if (
            !path.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase)
            ||
            !path.EndsWith(
                suffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }


        int appLength =
            path.Length
            -
            prefix.Length
            -
            suffix.Length;


        if (appLength <=
            0)
        {
            return false;
        }


        string encoded =
            path.Substring(
                prefix.Length,
                appLength);


        if (
            string.IsNullOrWhiteSpace(
                encoded)
            ||
            encoded.Contains(
                '/'))
        {
            return false;
        }


        appId =
            Uri.UnescapeDataString(
                encoded)
                .Trim();


        return !string.IsNullOrWhiteSpace(
            appId);
    }


    private static string NormalizeContextValue(
        string? value,
        string fallback,
        int maximumLength)
    {
        string clean =
            string.IsNullOrWhiteSpace(
                value)
                ? fallback
                : value.Trim();


        return clean.Length <=
                maximumLength
            ? clean
            : clean[
                ..maximumLength];
    }


    private static string NormalizePath(
        string? value)
    {
        string path =
            string.IsNullOrWhiteSpace(
                value)
                ? "/"
                : value.Trim();


        if (!path.StartsWith(
                '/'))
        {
            path =
                "/" +
                path;
        }


        if (
            path.Length >
                1
            &&
            path.EndsWith(
                '/'))
        {
            path =
                path.TrimEnd(
                    '/');
        }


        return path;
    }


    private static NIRABridgeAppResponse ToBridgeApp(
        ElvaraAppDescriptor app)
    {
        return new NIRABridgeAppResponse
        {
            AppId =
                app.AppId,

            DisplayName =
                app.DisplayName,

            ProductName =
                app.ProductName,

            AllowEmbeddedNIRA =
                app.AllowEmbeddedNIRA,

            AllowNIRAQuery =
                app.AllowNIRAQuery,

            Description =
                app.Description
        };
    }
}
