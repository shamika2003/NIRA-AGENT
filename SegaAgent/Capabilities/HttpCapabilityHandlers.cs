/*
 * filename: HttpCapabilityHandlers.cs
 */

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SegaAgent.Capabilities;


public sealed class SegaHttpRequestCapabilityHandler
    : ISegaCapabilityHandler,
      IDisposable
{
    private readonly HttpClient
        _http;


    public SegaCapabilityDescriptor Descriptor
    {
        get;
    } =
        new SegaCapabilityDescriptor
        {
            Id =
                SegaCapabilityIds.HttpRequest,

            Description =
                "Make a bounded HTTP/HTTPS request. GET/HEAD are observation risk; methods that can change remote state require authorization.",

            DefaultRisk =
                SegaCapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter("url", "string", true, "Absolute http/https URL."),
                    Parameter("method", "string", false, "GET, HEAD, POST, PUT, PATCH, or DELETE. Default GET."),
                    Parameter("headers", "object", false, "Optional request headers."),
                    Parameter("body", "string", false, "Optional UTF-8 request body."),
                    Parameter("contentType", "string", false, "Body content type. Default application/json when body exists."),
                    Parameter("timeoutSeconds", "integer", false, "Timeout from 1-120 seconds. Default 30."),
                    Parameter("maxResponseChars", "integer", false, "Maximum response body characters. Default 20000, maximum 48000.")
                }
        };


    public SegaHttpRequestCapabilityHandler()
    {
        SocketsHttpHandler handler =
            new()
            {
                AllowAutoRedirect = false,
                UseCookies = false,

                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate |
                    DecompressionMethods.Brotli
            };


        _http =
            new HttpClient(
                handler,
                disposeHandler: true)
            {
                Timeout =
                    Timeout.InfiniteTimeSpan
            };


        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "SegaAgent",
                "1.0"));
    }


    public SegaCapabilityRisk ResolveRisk(
        SegaCapabilityRequest request)
    {
        string method =
            SegaCapabilityArguments.GetOptionalString(
                request,
                "method",
                16)
            ?? "GET";


        return method.Trim().ToUpperInvariant() is
            "GET" or "HEAD"
            ? SegaCapabilityRisk.Observe
            : SegaCapabilityRisk.Execute;
    }


    public async Task<SegaCapabilityHandlerResult> ExecuteAsync(
        SegaCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        Uri uri =
            ParseUri(
                SegaCapabilityArguments.RequireString(
                    request,
                    "url",
                    4096));


        string methodText =
            SegaCapabilityArguments.GetOptionalString(
                request,
                "method",
                16)
            ?? "GET";


        HttpMethod method =
            ParseMethod(
                methodText);


        string? body =
            SegaCapabilityArguments.GetOptionalRawString(
                request,
                "body",
                2_000_000);


        string contentType =
            SegaCapabilityArguments.GetOptionalString(
                request,
                "contentType",
                256)
            ?? "application/json";


        int timeoutSeconds =
            SegaCapabilityArguments.GetInteger(
                request,
                "timeoutSeconds",
                30,
                1,
                120);


        int maxResponseChars =
            SegaCapabilityArguments.GetInteger(
                request,
                "maxResponseChars",
                20000,
                1000,
                48000);


        using HttpRequestMessage message =
            new(
                method,
                uri);


        if (body !=
            null)
        {
            message.Content =
                new StringContent(
                    body,
                    Encoding.UTF8,
                    contentType);
        }


        JsonElement? headers =
            SegaCapabilityArguments.GetOptionalObject(
                request,
                "headers");


        if (headers.HasValue)
        {
            foreach (
                JsonProperty property
                in headers.Value.EnumerateObject())
            {
                if (property.Value.ValueKind !=
                    JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"HTTP header '{property.Name}' must have a string value.");
                }


                string value =
                    property.Value.GetString()
                    ?? string.Empty;


                if (!message.Headers.TryAddWithoutValidation(
                        property.Name,
                        value))
                {
                    if (message.Content ==
                        null)
                    {
                        message.Content =
                            new StringContent(
                                string.Empty,
                                Encoding.UTF8,
                                "text/plain");
                    }


                    message.Content.Headers.TryAddWithoutValidation(
                        property.Name,
                        value);
                }
            }
        }


        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);


        timeout.CancelAfter(
            TimeSpan.FromSeconds(
                timeoutSeconds));


        using HttpResponseMessage response =
            await _http.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);


        StringBuilder output =
            new();


        output.AppendLine(
            $"Status={(int)response.StatusCode} {response.ReasonPhrase}");
        output.AppendLine(
            $"FinalUrl={response.RequestMessage?.RequestUri}");


        foreach (
            KeyValuePair<string, IEnumerable<string>> header
            in response.Headers)
        {
            if (!header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                output.AppendLine($"Header:{header.Key}={string.Join(", ", header.Value)}");
        }


        if (response.Content !=
            null)
        {
            foreach (
                KeyValuePair<string, IEnumerable<string>> header
                in response.Content.Headers)
            {
                output.AppendLine(
                    $"ContentHeader:{header.Key}={string.Join(", ", header.Value)}");
            }


            if (method !=
                HttpMethod.Head)
            {
                string text = await ReadBoundedTextAsync(response.Content, maxResponseChars, timeout.Token);


                output.AppendLine();
                output.AppendLine(
                    "BODY:");
                output.Append(
                    SegaCapabilityArguments.Truncate(
                        text,
                        maxResponseChars));
            }
        }


        return new SegaCapabilityHandlerResult
        {
            Succeeded = response.IsSuccessStatusCode,
            HttpStatusCode = (int)response.StatusCode,
            Summary =
                $"HTTP {method.Method} '{uri}' returned {(int)response.StatusCode} {response.ReasonPhrase}.",

            Output =
                output.ToString().TrimEnd(),

            ChangedSystemState =
                method !=
                    HttpMethod.Get
                &&
                method !=
                    HttpMethod.Head
        };
    }


    private static async Task<string> ReadBoundedTextAsync(HttpContent content, int limit, CancellationToken ct)
    {
        await using Stream stream = await content.ReadAsStreamAsync(ct);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        char[] buffer = new char[Math.Min(limit + 1, 4096)];
        StringBuilder text = new();
        while (text.Length <= limit)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - text.Length)), ct);
            if (read == 0) break;
            text.Append(buffer, 0, read);
        }
        return text.Length > limit ? text.ToString(0, limit) + "\n[Response truncated.]" : text.ToString();
    }

    public void Dispose()
    {
        _http.Dispose();
    }


    private static Uri ParseUri(
        string value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri? uri)
            ||
            (uri.Scheme !=
                Uri.UriSchemeHttp
             &&
             uri.Scheme !=
                Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "HTTP capability requires an absolute http:// or https:// URL.");
        }


        return uri;
    }


    private static HttpMethod ParseMethod(
        string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "GET" =>
                HttpMethod.Get,

            "HEAD" =>
                HttpMethod.Head,

            "POST" =>
                HttpMethod.Post,

            "PUT" =>
                HttpMethod.Put,

            "PATCH" =>
                HttpMethod.Patch,

            "DELETE" =>
                HttpMethod.Delete,

            _ =>
                throw new InvalidOperationException(
                    "HTTP method must be GET, HEAD, POST, PUT, PATCH, or DELETE.")
        };
    }


    private static SegaCapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new SegaCapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class SegaHttpDownloadCapabilityHandler
    : ISegaCapabilityHandler,
      IDisposable
{
    private readonly HttpClient
        _http;


    public SegaCapabilityDescriptor Descriptor
    {
        get;
    } =
        new SegaCapabilityDescriptor
        {
            Id =
                SegaCapabilityIds.HttpDownload,

            Description =
                "Download one HTTP/HTTPS resource to a file using a temporary file and atomic final move, returning size/hash evidence.",

            DefaultRisk =
                SegaCapabilityRisk.Modify,

            Parameters =
                new[]
                {
                    Parameter("url", "string", true, "Absolute http/https URL."),
                    Parameter("destination", "string", true, "Destination file path."),
                    Parameter("overwrite", "boolean", false, "Replace an existing destination. Default false."),
                    Parameter("timeoutSeconds", "integer", false, "Timeout from 1-600 seconds. Default 120."),
                    Parameter("maxBytes", "integer", false, "Maximum download size in bytes. Default 268435456 (256 MiB), maximum 1073741824 (1 GiB).")
                }
        };


    public SegaHttpDownloadCapabilityHandler()
    {
        SocketsHttpHandler handler =
            new()
            {
                AllowAutoRedirect = false,
                UseCookies = false,

                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate |
                    DecompressionMethods.Brotli
            };


        _http =
            new HttpClient(
                handler,
                disposeHandler: true)
            {
                Timeout =
                    Timeout.InfiniteTimeSpan
            };


        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "SegaAgent",
                "1.0"));
    }


    public SegaCapabilityRisk ResolveRisk(
        SegaCapabilityRequest request)
    {
        return SegaCapabilityArguments.GetBoolean(
                request,
                "overwrite")
            ? SegaCapabilityRisk.Destructive
            : SegaCapabilityRisk.Modify;
    }


    public async Task<SegaCapabilityHandlerResult> ExecuteAsync(
        SegaCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        Uri uri =
            ParseUri(
                SegaCapabilityArguments.RequireString(
                    request,
                    "url",
                    4096));


        string destination =
            SegaCapabilityArguments.NormalizePath(
                SegaCapabilityArguments.RequireString(
                    request,
                    "destination",
                    32760));


        bool overwrite =
            SegaCapabilityArguments.GetBoolean(
                request,
                "overwrite");


        int timeoutSeconds =
            SegaCapabilityArguments.GetInteger(
                request,
                "timeoutSeconds",
                120,
                1,
                600);


        int maxBytes =
            SegaCapabilityArguments.GetInteger(
                request,
                "maxBytes",
                268_435_456,
                1,
                1_073_741_824);


        string? directory =
            Path.GetDirectoryName(
                destination);


        if (string.IsNullOrWhiteSpace(
                directory)
            ||
            !Directory.Exists(
                directory))
        {
            throw new DirectoryNotFoundException(
                "Download destination directory does not exist. Create it explicitly when intended.");
        }


        if (File.Exists(
                destination)
            &&
            !overwrite)
        {
            throw new IOException(
                "Download destination already exists and overwrite=false.");
        }


        string temporary =
            destination
            + ".sega-download-"
            + Guid.NewGuid().ToString("N")
            + ".tmp";


        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);


        timeout.CancelAfter(
            TimeSpan.FromSeconds(
                timeoutSeconds));


        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);


            if ((int)response.StatusCode is >= 300 and < 400)
                return new SegaCapabilityHandlerResult
                {
                    Succeeded = false,
                    HttpStatusCode = (int)response.StatusCode,
                    Summary = "Download returned a redirect. Submit its Location as a separate authorized request.",
                    Output = $"Status={(int)response.StatusCode}\nLocation={response.Headers.Location}",
                    ChangedSystemState = false
                };
            response.EnsureSuccessStatusCode();


            long? declaredLength =
                response.Content.Headers.ContentLength;


            if (declaredLength.HasValue
                &&
                declaredLength.Value >
                    maxBytes)
            {
                throw new InvalidOperationException(
                    $"Download Content-Length {declaredLength.Value} exceeds maxBytes={maxBytes}.");
            }


            await using Stream input =
                await response.Content.ReadAsStreamAsync(
                    timeout.Token);


            await using FileStream output =
                new(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);


            byte[] buffer =
                new byte[81920];


            long total =
                0;


            while (true)
            {
                int read =
                    await input.ReadAsync(
                        buffer,
                        timeout.Token);


                if (read ==
                    0)
                {
                    break;
                }


                total +=
                    read;


                if (total >
                    maxBytes)
                {
                    throw new InvalidOperationException(
                        $"Download exceeded maxBytes={maxBytes}.");
                }


                await output.WriteAsync(
                    buffer.AsMemory(
                        0,
                        read),
                    timeout.Token);
            }


            await output.FlushAsync(
                timeout.Token);


            await output.DisposeAsync();
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite);

            string hash =
                ComputeSha256(
                    destination);


            FileInfo file =
                new(
                    destination);


            return new SegaCapabilityHandlerResult
            {
                Summary =
                    $"Downloaded '{uri}' to '{destination}'. Length={file.Length} | SHA256={hash}.",

                Output =
                    $"Url={uri}\nDestination={destination}\nLength={file.Length}\nSHA256={hash}\nLastWriteUtc={file.LastWriteTimeUtc:O}",

                ChangedSystemState =
                    true
            };
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


    public void Dispose()
    {
        _http.Dispose();
    }


    private static Uri ParseUri(
        string value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri? uri)
            ||
            (uri.Scheme !=
                Uri.UriSchemeHttp
             &&
             uri.Scheme !=
                Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "HTTP download requires an absolute http:// or https:// URL.");
        }


        return uri;
    }


    private static string ComputeSha256(
        string path)
    {
        using FileStream stream =
            File.OpenRead(
                path);


        byte[] hash =
            SHA256.HashData(
                stream);


        return Convert.ToHexString(
            hash)
            .ToLowerInvariant();
    }


    private static SegaCapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new SegaCapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}
