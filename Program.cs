using BunbunBroll.Components;
using BunbunBroll.Services;
using BunbunBroll.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Authentication Support (Blazor-level only)
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<CustomAuthStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<CustomAuthStateProvider>());

// Configure SQLite
builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseSqlite("Data Source=bunbun.db"));

// Configure settings from appsettings.json
builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<GeminiSettings>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<PexelsSettings>(builder.Configuration.GetSection("Pexels"));
builder.Services.Configure<PixabaySettings>(builder.Configuration.GetSection("Pixabay"));
builder.Services.Configure<DownloaderSettings>(builder.Configuration.GetSection("Downloader"));

// Register core services
builder.Services.AddScoped<IScriptProcessor, ScriptProcessor>();
builder.Services.AddScoped<IPipelineOrchestrator, PipelineOrchestrator>();
builder.Services.AddScoped<IProjectService, ProjectService>();

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
builder.Services.AddHttpClient<PexelsAssetBroker>((sp, client) =>
{
    var settings = builder.Configuration.GetSection("Pexels").Get<PexelsSettings>()!;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.DefaultRequestHeaders.Add("Authorization", settings.ApiKey);
})
.AddPolicyHandler(GetRetryPolicy());

// Configure HttpClient for Pixabay API
builder.Services.AddHttpClient<PixabayAssetBroker>((sp, client) =>
{
    var settings = builder.Configuration.GetSection("Pixabay").Get<PixabaySettings>()!;
    client.BaseAddress = new Uri(settings.BaseUrl);
})
.AddPolicyHandler(GetRetryPolicy());

// Register Halal Video Filter (singleton so toggle state persists across requests)
builder.Services.AddSingleton<IHalalVideoFilter, HalalVideoFilter>();

// Register Composite Asset Broker (combines Pexels + Pixabay + Halal Filter)
builder.Services.AddScoped<IAssetBroker, CompositeAssetBroker>();

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

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
