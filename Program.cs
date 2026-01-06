using BunBunBroll.Components;
using BunBunBroll.Services;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure settings from appsettings.json
builder.Services.Configure<GeminiSettings>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<PexelsSettings>(builder.Configuration.GetSection("Pexels"));
builder.Services.Configure<DownloaderSettings>(builder.Configuration.GetSection("Downloader"));

// Register core services
builder.Services.AddScoped<IScriptProcessor, ScriptProcessor>();
builder.Services.AddScoped<IPipelineOrchestrator, PipelineOrchestrator>();

// Configure HttpClient for Gemini (Local LLM)
builder.Services.AddHttpClient<IIntelligenceService, IntelligenceService>((sp, client) =>
{
    var settings = builder.Configuration.GetSection("Gemini").Get<GeminiSettings>()!;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");
    client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
})
.AddPolicyHandler(GetRetryPolicy());

// Configure HttpClient for Pexels API
builder.Services.AddHttpClient<IAssetBroker, PexelsAssetBroker>((sp, client) =>
{
    var settings = builder.Configuration.GetSection("Pexels").Get<PexelsSettings>()!;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Authorization", settings.ApiKey);
})
.AddPolicyHandler(GetRetryPolicy());

// Configure HttpClient for Downloader (generic, no auth)
builder.Services.AddHttpClient<IDownloaderService, DownloaderService>()
    .AddPolicyHandler(GetRetryPolicy());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Polly retry policy with exponential backoff
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 2s, 4s, 8s
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Console.WriteLine($"Retry {retryAttempt} after {timespan.TotalSeconds}s due to {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
            });
}
