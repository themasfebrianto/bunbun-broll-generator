using BunbunBroll.Data;
using BunbunBroll.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BunbunBroll.Services;

/// <summary>
/// Syncs ScriptGenerationSessions to/from git-tracked JSON files in the sessions/ directory.
/// This enables cross-machine project synchronization.
/// </summary>
public class SessionSyncService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionSyncService> _logger;
    private readonly string _sessionsDir;
    private readonly string _outputScriptsDir;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SessionSyncService(IServiceProvider serviceProvider, ILogger<SessionSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var baseDir = Directory.GetCurrentDirectory();
        _sessionsDir = Path.Combine(baseDir, "sessions");
        _outputScriptsDir = Path.Combine(baseDir, "output", "scripts");
    }

    // ============================================================
    //  EXPORT: DB → JSON file
    // ============================================================

    /// <summary>
    /// Exports a single session (with all phases and their content) to sessions/{id}/session.json
    /// </summary>
    public async Task ExportSessionAsync(string sessionId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var session = await db.ScriptGenerationSessions
                .Include(s => s.Phases)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
            {
                _logger.LogWarning("Session {SessionId} not found for export", sessionId);
                return;
            }

            var exportData = MapToExportModel(session);

            // Read phase content files and embed inline
            foreach (var phase in exportData.Phases)
            {
                if (!string.IsNullOrEmpty(phase.ContentFilePath) && File.Exists(phase.ContentFilePath))
                {
                    phase.Content = await File.ReadAllTextAsync(phase.ContentFilePath);
                }
                // Clear the file path — it's machine-specific
                phase.ContentFilePath = null;
            }

            // Write JSON
            var sessionDir = Path.Combine(_sessionsDir, sessionId);
            Directory.CreateDirectory(sessionDir);

            var jsonPath = Path.Combine(sessionDir, "session.json");
            var json = JsonSerializer.Serialize(exportData, _jsonOptions);
            await File.WriteAllTextAsync(jsonPath, json);

            _logger.LogInformation("Exported session {SessionId} ({Topic}) → {Path}",
                sessionId, session.Topic, jsonPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Exports ALL completed sessions from DB to sessions/ folder.
    /// Useful for initial migration.
    /// </summary>
    public async Task ExportAllAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sessionIds = await db.ScriptGenerationSessions
            .Where(s => s.Status == SessionStatus.Completed)
            .Select(s => s.Id)
            .ToListAsync();

        _logger.LogInformation("Exporting {Count} completed sessions...", sessionIds.Count);

        foreach (var id in sessionIds)
        {
            await ExportSessionAsync(id);
        }
    }

    // ============================================================
    //  IMPORT: JSON files → DB
    // ============================================================

    /// <summary>
    /// Scans sessions/ directory and imports any sessions not already in the DB.
    /// Called at app startup.
    /// </summary>
    public async Task ImportAllAsync()
    {
        if (!Directory.Exists(_sessionsDir))
        {
            _logger.LogInformation("No sessions/ directory found, skipping import.");
            return;
        }

        var sessionDirs = Directory.GetDirectories(_sessionsDir);
        if (sessionDirs.Length == 0)
        {
            _logger.LogInformation("No session folders found in sessions/.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existingIds = (await db.ScriptGenerationSessions
            .Select(s => s.Id)
            .ToListAsync()).ToHashSet();

        int imported = 0;
        foreach (var dir in sessionDirs)
        {
            var jsonPath = Path.Combine(dir, "session.json");
            if (!File.Exists(jsonPath)) continue;

            try
            {
                var json = await File.ReadAllTextAsync(jsonPath);
                var exportData = JsonSerializer.Deserialize<SessionExportModel>(json, _jsonOptions);
                if (exportData == null) continue;

                if (existingIds.Contains(exportData.Id))
                {
                    _logger.LogDebug("Session {Id} already exists, skipping.", exportData.Id);
                    continue;
                }

                await ImportSession(db, exportData);
                imported++;
                _logger.LogInformation("Imported session {Id} ({Topic})", exportData.Id, exportData.Topic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import session from {Path}", jsonPath);
            }
        }

        if (imported > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} new sessions from sessions/ folder.", imported);
        }
        else
        {
            _logger.LogInformation("All sessions already in DB, nothing to import.");
        }
    }

    // ============================================================
    //  Private Helpers
    // ============================================================

    private async Task ImportSession(AppDbContext db, SessionExportModel data)
    {
        // Create output directory for phase files
        var outputDir = Path.Combine(_outputScriptsDir, data.Id);
        Directory.CreateDirectory(outputDir);

        var session = new ScriptGenerationSession
        {
            Id = data.Id,
            PatternId = data.PatternId,
            Topic = data.Topic,
            Outline = data.Outline,
            OutlineDistributionJson = data.OutlineDistributionJson,
            TargetDurationMinutes = data.TargetDurationMinutes,
            SourceReferences = data.SourceReferences,
            ChannelName = data.ChannelName ?? string.Empty,
            Status = data.Status,
            OutputDirectory = outputDir,
            CreatedAt = data.CreatedAt,
            UpdatedAt = data.UpdatedAt,
            CompletedAt = data.CompletedAt,
            ErrorMessage = data.ErrorMessage
        };

        foreach (var phaseData in data.Phases)
        {
            string? contentFilePath = null;

            // Recreate the .md content file if content was embedded
            if (!string.IsNullOrEmpty(phaseData.Content))
            {
                var fileName = $"{phaseData.Order:D2}-{phaseData.PhaseId}.md";
                contentFilePath = Path.Combine(outputDir, fileName);
                await File.WriteAllTextAsync(contentFilePath, phaseData.Content);
            }

            session.Phases.Add(new ScriptGenerationPhase
            {
                Id = phaseData.Id,
                SessionId = data.Id,
                PhaseId = phaseData.PhaseId,
                PhaseName = phaseData.PhaseName,
                Order = phaseData.Order,
                Status = phaseData.Status,
                ContentFilePath = contentFilePath,
                WordCount = phaseData.WordCount,
                DurationSeconds = phaseData.DurationSeconds,
                IsValidated = phaseData.IsValidated,
                WarningsJson = phaseData.WarningsJson,
                CompletedAt = phaseData.CompletedAt
            });
        }

        db.ScriptGenerationSessions.Add(session);
    }

    private static SessionExportModel MapToExportModel(ScriptGenerationSession session)
    {
        return new SessionExportModel
        {
            Id = session.Id,
            PatternId = session.PatternId,
            Topic = session.Topic,
            Outline = session.Outline,
            OutlineDistributionJson = session.OutlineDistributionJson,
            TargetDurationMinutes = session.TargetDurationMinutes,
            SourceReferences = session.SourceReferences,
            ChannelName = session.ChannelName,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            CompletedAt = session.CompletedAt,
            ErrorMessage = session.ErrorMessage,
            Phases = session.Phases
                .OrderBy(p => p.Order)
                .Select(p => new PhaseExportModel
                {
                    Id = p.Id,
                    PhaseId = p.PhaseId,
                    PhaseName = p.PhaseName,
                    Order = p.Order,
                    Status = p.Status,
                    ContentFilePath = p.ContentFilePath,
                    WordCount = p.WordCount,
                    DurationSeconds = p.DurationSeconds,
                    IsValidated = p.IsValidated,
                    WarningsJson = p.WarningsJson,
                    CompletedAt = p.CompletedAt
                })
                .ToList()
        };
    }
}

// ============================================================
//  Export/Import DTO Models
// ============================================================

public class SessionExportModel
{
    public string Id { get; set; } = string.Empty;
    public string PatternId { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string? Outline { get; set; }
    public string? OutlineDistributionJson { get; set; }
    public int TargetDurationMinutes { get; set; }
    public string? SourceReferences { get; set; }
    public string? ChannelName { get; set; }
    public SessionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public List<PhaseExportModel> Phases { get; set; } = new();
}

public class PhaseExportModel
{
    public string Id { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string PhaseName { get; set; } = string.Empty;
    public int Order { get; set; }
    public PhaseStatus Status { get; set; }
    public int? WordCount { get; set; }
    public double? DurationSeconds { get; set; }
    public bool IsValidated { get; set; }
    public string? WarningsJson { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Embedded script content (replaces ContentFilePath for portability)
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Only used during export mapping, cleared before serialization.
    /// </summary>
    [JsonIgnore]
    public string? ContentFilePath { get; set; }
}
