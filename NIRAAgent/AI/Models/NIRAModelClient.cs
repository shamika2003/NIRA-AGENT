using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NIRAAgent.Diagnostics;
namespace NIRAAgent.AI.Models;

/// <summary>
/// Model transport for legacy NIRA features during V2 migration. NIRA's reasoning,
/// permission gates, tools and memory stay owned by their existing host services.
/// Model/endpoint configuration is isolated here, not scattered across cognition.
/// </summary>
public sealed class NIRAModelClient : IDisposable
{
    private readonly NIRAModelApiKeyService _apiKeys;
    private readonly HttpClient _http;
    private bool _disposed;

    public NIRAModelClient(NIRAModelApiKeyService apiKeys)
    {
        _apiKeys=apiKeys ?? throw new ArgumentNullException(nameof(apiKeys));
        _http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}) {
            Timeout=TimeSpan.FromMinutes(4) };
    }

    public Task<string> ChatAsync(string ModelName,string systemPrompt,string userMessage,
        CancellationToken cancellationToken=default,bool jsonMode=false)
    {
        var messages=new object[]{new{role="system",content=systemPrompt},
            new{role="user",content=userMessage}};
        return ExecuteAsync(ModelName,messages,systemPrompt,userMessage,0,jsonMode,cancellationToken);
    }

    public async Task<string> ChatWithImagesAsync(string modelName,string systemPrompt,
        string userMessage,IReadOnlyList<string> imagePaths,bool jsonMode=false,
        CancellationToken cancellationToken=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(imagePaths);
        var paths=imagePaths.Where(p=>!string.IsNullOrWhiteSpace(p))
            .Select(p=>Path.GetFullPath(p.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length==0 || paths.Length>NIRAAgent.AI.Cognition.NIRAImageInput.MaximumImagesPerModelCall)
            throw new InvalidOperationException("Image count exceeds the authorized model-call bounds.");
        const long MaxSingle=20*1024*1024;
        long cumulative=0;
        var content=new List<object>{new {type="text",text=userMessage}};
        foreach(var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo info=new(path);
            if (!info.Exists) throw new FileNotFoundException("Image is unavailable.",path);
            if(info.Length<=0 || info.Length>MaxSingle ||
                (cumulative+=info.Length)>NIRAAgent.AI.Cognition.NIRAImageInput.MaximumModelCallBytes)
                throw new InvalidOperationException("Image size exceeds model-call limits.");
            string mime=Path.GetExtension(path).ToLowerInvariant() switch {
                ".png"=>"image/png", ".jpg" or ".jpeg"=>"image/jpeg",
                ".webp"=>"image/webp", _=>throw new InvalidOperationException("Unsupported image format.")};
            byte[] bytes=await File.ReadAllBytesAsync(path,cancellationToken);
            content.Add(new {type="image_url",image_url=new{
                url="data:"+mime+";base64,"+Convert.ToBase64String(bytes)}});
        }
        var messages=new object[]{new{role="system",content=(object)systemPrompt},
            new{role="user",content=(object)content.ToArray()}};
        return await ExecuteAsync(modelName,messages,systemPrompt,userMessage,paths.Length,
            jsonMode,cancellationToken);
    }

    // Legacy callers expect an enumerable. NVIDIA exposes SSE, but a bounded
    // non-streaming model turn is safer than losing an answer to partial events.
    public async IAsyncEnumerable<string> StreamChatAsync(string systemPrompt,string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken=default)
    {
        string answer=await ChatAsync(NIRAModelSelection.Primary,systemPrompt,userMessage,cancellationToken);
        if(!string.IsNullOrEmpty(answer)) yield return answer;
    }

    private async Task<string> ExecuteAsync(string model,IReadOnlyList<object> messages,
        string systemPrompt,string userMessage,int imageCount,bool jsonMode,CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        Guid id=Guid.NewGuid();var timer=Stopwatch.StartNew();
        string modality=imageCount>0?"Vision":"Text";
        NIRADiagnosticTrace.Event("LLM-START",$"Call={id:D} Model={model} " +
            $"Mode={modality} Json={jsonMode} Images={imageCount} " +
            $"SystemChars={systemPrompt.Length} UserChars={userMessage.Length}");
        NIRADiagnosticTrace.ModelRequest(id,model,systemPrompt,userMessage,jsonMode,modality,imageCount);
        var payload=new Dictionary<string,object> {
            ["model"]=model,["messages"]=messages,["stream"]=false
        };
        NIRAActiveModelProvider.ConfigureRequest(payload);
        try
        {
            using var response=await SendWithCredentialRecoveryAsync(JsonSerializer.Serialize(payload),ct);
            if(response.Content.Headers.ContentLength>1024*1024)
                throw new InvalidOperationException("Model response exceeded size limit.");
            string raw=await response.Content.ReadAsStringAsync(ct);
            if(raw.Length>1024*1024)throw new InvalidOperationException("Model response exceeded size limit.");
            using JsonDocument doc=JsonDocument.Parse(raw);
            JsonElement root=doc.RootElement;
            if(!root.TryGetProperty("choices",out var choices) || choices.ValueKind!=JsonValueKind.Array ||
                choices.GetArrayLength()!=1 ||
                !choices[0].TryGetProperty("finish_reason",out var reason) ||
                reason.ValueKind!=JsonValueKind.String || reason.GetString()!="stop" ||
                !choices[0].TryGetProperty("message",out var msg) ||
                !msg.TryGetProperty("content",out var content) || content.ValueKind!=JsonValueKind.String)
                throw new InvalidOperationException("Model response was incomplete or not a text answer.");
            string answer=content.GetString()??string.Empty;
            if(string.IsNullOrWhiteSpace(answer))throw new InvalidOperationException("Model answer was empty.");
            NIRADiagnosticTrace.Event("LLM-END",$"Call={id:D} ElapsedMs={timer.ElapsedMilliseconds} ResponseChars={answer.Length}");
            NIRADiagnosticTrace.ModelResponse(id,model,answer);
            return answer;
        }
        catch(Exception ex)
        {
            NIRADiagnosticTrace.Event("LLM-FAILED",$"Call={id:D} ElapsedMs={timer.ElapsedMilliseconds} ErrorType={ex.GetType().Name}");
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendWithCredentialRecoveryAsync(string json,CancellationToken ct)
    {
        string key=_apiKeys.GetRequiredKey();
        for(int attempt=0;attempt<2;attempt++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,NIRAActiveModelProvider.ChatEndpoint);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content=new StringContent(json,Encoding.UTF8,"application/json");
            var response=await _http.SendAsync(request,HttpCompletionOption.ResponseContentRead,ct);
            if(response.IsSuccessStatusCode && response.StatusCode!=HttpStatusCode.Accepted)
                return response;
            // The host must not automatically retry rate limits or unknown inference outcomes.
            NIRAModelCredentialProblem? problem = response.StatusCode switch {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden=>NIRAModelCredentialProblem.InvalidOrRevoked,
                HttpStatusCode.PaymentRequired=>NIRAModelCredentialProblem.UsageUnavailable,
                _=>null};
            int status=(int)response.StatusCode;
            response.Dispose();
            if(problem is null || attempt>0)
                throw new HttpRequestException($"Model provider returned HTTP {status}.");
            key=await _apiKeys.ResolveProblemAsync(problem.Value,key,
                $"Model connection needs attention (HTTP {status}).",ct);
        }
        throw new InvalidOperationException("Credential recovery was incomplete.");
    }

    public void Dispose(){if(_disposed)return;_disposed=true;_http.Dispose();}
}
