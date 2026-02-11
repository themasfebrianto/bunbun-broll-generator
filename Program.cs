using BunbunBroll.Components;
using BunbunBroll.Services;
using BunbunBroll.Orchestration;
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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=bunbun.db";
builder.Services.AddDbContext<AppDbContext>(options => 
    options.UseSqlite(connectionString));

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
builder.Services.AddScoped<IShortVideoComposer, ShortVideoComposer>();

// Script Generation services
builder.Services.AddSingleton<IPatternRegistry, PatternRegistry>();
builder.Services.AddScoped<IScriptOrchestrator, ScriptOrchestrator>();
builder.Services.AddScoped<IScriptGenerationService, ScriptGenerationService>();
builder.Services.AddScoped<ConfigBatchGenerator>();

// Toast notification service
builder.Services.AddScoped<BunbunBroll.Services.ToastService>();

// Configure HttpClient for Gemini (Local LLM) - Uses IOptions pattern for env var support
builder.Services.AddHttpClient<IIntelligenceService, IntelligenceService>()
.ConfigureHttpClient((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Gemini:BaseUrl"] ?? "http://127.0.0.1:8317";
    var apiKey = config["Gemini:ApiKey"] ?? "sk-dummy";
    var timeout = int.TryParse(config["Gemini:TimeoutSeconds"], out var t) ? t : 30;
    
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    client.Timeout = TimeSpan.FromSeconds(timeout);
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

// Initialize database with automatic migration for new columns
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Load pattern registry
    var patternRegistry = scope.ServiceProvider.GetRequiredService<IPatternRegistry>();
    var patternsDir = builder.Configuration["Patterns:Directory"] ?? "patterns";
    if (!Path.IsPathRooted(patternsDir))
        patternsDir = Path.Combine(Directory.GetCurrentDirectory(), patternsDir);
    patternRegistry.LoadFromDirectory(patternsDir);

    // Add new columns if they don't exist (for existing databases)
    try
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        // Check if KeywordsJson column exists
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*) FROM pragma_table_info('Sentences') WHERE name='KeywordsJson'
        ";
        var result = await command.ExecuteScalarAsync();
        var columnExists = Convert.ToInt32(result) > 0;

        if (!columnExists)
        {
            // Add new columns for keyword persistence
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = @"
                ALTER TABLE Sentences ADD COLUMN KeywordsJson TEXT;
                ALTER TABLE Sentences ADD COLUMN SuggestedCategory TEXT;
                ALTER TABLE Sentences ADD COLUMN DetectedMood TEXT;
            ";
            await alterCommand.ExecuteNonQueryAsync();
            Console.WriteLine("Added keyword persistence columns to existing database.");
        }

        // Check if VideoDuration column exists (for % match accuracy)
        command.CommandText = @"
            SELECT COUNT(*) FROM pragma_table_info('Sentences') WHERE name='VideoDuration'
        ";
        result = await command.ExecuteScalarAsync();
        columnExists = Convert.ToInt32(result) > 0;

        if (!columnExists)
        {
            // Add VideoDuration column
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = @"
                ALTER TABLE Sentences ADD COLUMN VideoDuration INTEGER DEFAULT 0;
            ";
            await alterCommand.ExecuteNonQueryAsync();
            Console.WriteLine("Added VideoDuration column to existing database.");
        }

        // Check if ChannelName column exists in ScriptGenerationSessions
        command.CommandText = @"
            SELECT COUNT(*) FROM pragma_table_info('ScriptGenerationSessions') WHERE name='ChannelName'
        ";
        result = await command.ExecuteScalarAsync();
        columnExists = Convert.ToInt32(result) > 0;

        if (!columnExists)
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = @"
                ALTER TABLE ScriptGenerationSessions ADD COLUMN ChannelName TEXT DEFAULT '';
            ";
            await alterCommand.ExecuteNonQueryAsync();
            Console.WriteLine("Added ChannelName column to ScriptGenerationSessions.");
        }

        await connection.CloseAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Note: Database migration info: {ex.Message}");
    }
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
