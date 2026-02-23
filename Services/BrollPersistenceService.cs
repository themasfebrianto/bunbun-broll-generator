using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

public interface IBrollPersistenceService
{
    string? GetBrollPromptsFilePath(ScriptGenerationSession? session, string? sessionId);
    Task SaveBrollPromptsToDisk(List<BrollPromptItem> items, ScriptGenerationSession? session, string? sessionId);
    Task<List<BrollPromptItem>> LoadBrollPromptsFromDisk(ScriptGenerationSession? session, string? sessionId);
    Task SaveImageConfigToDisk(ImagePromptConfig config, ScriptGenerationSession? session, string? sessionId);
    Task<ImagePromptConfig> LoadImageConfigFromDisk(ScriptGenerationSession? session, string? sessionId);
    Task SaveGlobalContextToDisk(GlobalScriptContext context, ScriptGenerationSession? session, string? sessionId);
    Task<GlobalScriptContext?> LoadGlobalContextFromDisk(ScriptGenerationSession? session, string? sessionId);
    Task HandleDeleteBrollCache();
    void InvalidateBrollClassification(List<BrollPromptItem> items, ScriptGenerationSession? session, string? sessionId);
    Task SaveBrollMetadata(BrollSessionMetadata metadata, ScriptGenerationSession? session, string? sessionId);
    Task<BrollSessionMetadata?> LoadBrollMetadata(ScriptGenerationSession? session, string? sessionId);
}

public class BrollPersistenceService : IBrollPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string? GetBrollPromptsFilePath(ScriptGenerationSession? session, string? sessionId)
    {
        if (session == null) return null;

        if (!string.IsNullOrEmpty(session.OutputDirectory))
        {
            var directPath = Path.Combine(session.OutputDirectory, "broll-prompts.json");
            if (File.Exists(directPath)) return directPath;
        }

        if (!string.IsNullOrEmpty(session.OutputDirectory))
        {
            var relativePath = Path.Combine(Directory.GetCurrentDirectory(), session.OutputDirectory, "broll-prompts.json");
            if (File.Exists(relativePath)) return relativePath;
        }

        var constructedPath = Path.Combine(Directory.GetCurrentDirectory(), "output", session.Id, "broll-prompts.json");
        return constructedPath;
    }

    public async Task SaveBrollMetadata(BrollSessionMetadata metadata, ScriptGenerationSession? session, string? sessionId)
    {
        if (session == null || string.IsNullOrEmpty(sessionId)) return;

        var dir = Path.Combine("output", sessionId);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "broll-metadata.json");
        var json = JsonSerializer.Serialize(metadata, _jsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<BrollSessionMetadata?> LoadBrollMetadata(ScriptGenerationSession? session, string? sessionId)
    {
        if (session == null || string.IsNullOrEmpty(sessionId)) return null;

        var path = Path.Combine("output", sessionId, "broll-metadata.json");
        if (!File.Exists(path)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<BrollSessionMetadata>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveBrollPromptsToDisk(List<BrollPromptItem> items, ScriptGenerationSession? session, string? sessionId)
    {
        var filePath = GetBrollPromptsFilePath(session, sessionId);
        if (filePath == null || items.Count == 0) return;

        try
        {
            var saveData = items.Select(i => new BrollPromptSaveItem
            {
                Index = i.Index, Timestamp = i.Timestamp, ScriptText = i.ScriptText,
                MediaType = i.MediaType, Prompt = i.Prompt, Reasoning = i.Reasoning,
                WhiskStatus = i.WhiskStatus, WhiskImagePath = i.WhiskImagePath, WhiskError = i.WhiskError,
                SelectedVideoUrl = i.SelectedVideoUrl, LocalVideoPath = i.LocalVideoPath, KenBurnsMotion = i.KenBurnsMotion,
                WhiskVideoStatus = i.WhiskVideoStatus, WhiskVideoPath = i.WhiskVideoPath, WhiskVideoError = i.WhiskVideoError,
                Style = i.Style, Filter = i.Filter, Texture = i.Texture, FilteredVideoPath = i.FilteredVideoPath,
                TextOverlay = i.TextOverlay,
                EstimatedDurationSeconds = i.EstimatedDurationSeconds,
                StartTimeSeconds = i.StartTimeSeconds,
                EndTimeSeconds = i.EndTimeSeconds
            }).ToList();

            var json = JsonSerializer.Serialize(saveData, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save broll prompts: {ex.Message}");
            throw;
        }
    }

    public async Task<List<BrollPromptItem>> LoadBrollPromptsFromDisk(ScriptGenerationSession? session, string? sessionId)
    {
        var filePath = GetBrollPromptsFilePath(session, sessionId);
        if (filePath == null || !File.Exists(filePath)) return new List<BrollPromptItem>();

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var saveItems = JsonSerializer.Deserialize<List<BrollPromptSaveItem>>(json, _jsonOptions);

            if (saveItems == null) return new List<BrollPromptItem>();

            var items = saveItems.Select(s => new BrollPromptItem
            {
                Index = s.Index, Timestamp = s.Timestamp, ScriptText = s.ScriptText,
                MediaType = s.MediaType, Prompt = s.Prompt, Reasoning = s.Reasoning,
                WhiskStatus = s.WhiskStatus, WhiskImagePath = s.WhiskImagePath, WhiskError = s.WhiskError,
                SelectedVideoUrl = s.SelectedVideoUrl, LocalVideoPath = s.LocalVideoPath,
                KenBurnsMotion = (s.MediaType == BrollMediaType.ImageGeneration && s.KenBurnsMotion == KenBurnsMotionType.None)
                    ? BrollPromptItem.GetRandomMotion() : s.KenBurnsMotion,
                WhiskVideoStatus = s.WhiskVideoStatus, WhiskVideoPath = s.WhiskVideoPath, WhiskVideoError = s.WhiskVideoError,
                Style = s.Style, Filter = s.Filter, Texture = s.Texture, FilteredVideoPath = s.FilteredVideoPath,
                TextOverlay = s.TextOverlay,
                EstimatedDurationSeconds = s.EstimatedDurationSeconds,
                StartTimeSeconds = s.StartTimeSeconds,
                EndTimeSeconds = s.EndTimeSeconds
            }).ToList();

            // Sanitize paths on load — normalize backslashes to forward slashes
            // and fix legacy paths with "output\scripts\" prefix
            bool changed = false;
            foreach (var item in items)
            {
                // Normalize all backslashes in media paths (fixes Windows-style paths on Linux)
                if (!string.IsNullOrEmpty(item.WhiskImagePath) && item.WhiskImagePath.Contains('\\'))
                {
                    item.WhiskImagePath = item.WhiskImagePath.Replace('\\', '/');
                    changed = true;
                }
                if (!string.IsNullOrEmpty(item.WhiskVideoPath) && item.WhiskVideoPath.Contains('\\'))
                {
                    item.WhiskVideoPath = item.WhiskVideoPath.Replace('\\', '/');
                    changed = true;
                }
                if (!string.IsNullOrEmpty(item.FilteredVideoPath) && item.FilteredVideoPath.Contains('\\'))
                {
                    item.FilteredVideoPath = item.FilteredVideoPath.Replace('\\', '/');
                    changed = true;
                }
                // Fix legacy "output/scripts/" prefix
                if (!string.IsNullOrEmpty(item.WhiskImagePath) && item.WhiskImagePath.Contains("output/scripts/"))
                {
                    item.WhiskImagePath = item.WhiskImagePath.Replace("output/scripts/", "output/");
                    changed = true;
                }
            }

            if (changed)
                await SaveBrollPromptsToDisk(items, session, sessionId);

            return items;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load broll prompts: {ex.Message}");
            return new List<BrollPromptItem>();
        }
    }

    public async Task SaveImageConfigToDisk(ImagePromptConfig config, ScriptGenerationSession? session, string? sessionId)
    {
        var brollPath = GetBrollPromptsFilePath(session, sessionId);
        if (brollPath == null) return;

        var configPath = Path.Combine(Path.GetDirectoryName(brollPath)!, "image-config.json");
        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(configPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save image config: {ex.Message}");
        }
    }

    public async Task<ImagePromptConfig> LoadImageConfigFromDisk(ScriptGenerationSession? session, string? sessionId)
    {
        var brollPath = GetBrollPromptsFilePath(session, sessionId);
        if (brollPath == null) return new ImagePromptConfig();

        var configPath = Path.Combine(Path.GetDirectoryName(brollPath)!, "image-config.json");
        if (!File.Exists(configPath)) return new ImagePromptConfig();

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<ImagePromptConfig>(json, _jsonOptions) ?? new ImagePromptConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load image config: {ex.Message}");
            return new ImagePromptConfig();
        }
    }

    public async Task SaveGlobalContextToDisk(GlobalScriptContext context, ScriptGenerationSession? session, string? sessionId)
    {
        var brollPath = GetBrollPromptsFilePath(session, sessionId);
        if (brollPath == null) return;

        var contextPath = Path.Combine(Path.GetDirectoryName(brollPath)!, "narrative-context.json");
        try
        {
            var json = JsonSerializer.Serialize(context, _jsonOptions);
            await File.WriteAllTextAsync(contextPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save narrative context: {ex.Message}");
        }
    }

    public async Task<GlobalScriptContext?> LoadGlobalContextFromDisk(ScriptGenerationSession? session, string? sessionId)
    {
        var brollPath = GetBrollPromptsFilePath(session, sessionId);
        if (brollPath == null) return null;

        var contextPath = Path.Combine(Path.GetDirectoryName(brollPath)!, "narrative-context.json");
        if (!File.Exists(contextPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(contextPath);
            return JsonSerializer.Deserialize<GlobalScriptContext>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load narrative context: {ex.Message}");
            return null;
        }
    }

    public Task HandleDeleteBrollCache()
    {
        try
        {
            var cachePath = Path.Combine(Directory.GetCurrentDirectory(), "cache", "broll");
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
                Console.WriteLine("B-Roll cache deleted successfully.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to delete B-Roll cache: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public void InvalidateBrollClassification(List<BrollPromptItem> items, ScriptGenerationSession? session, string? sessionId)
    {
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.WhiskImagePath) && File.Exists(item.WhiskImagePath))
                try { File.Delete(item.WhiskImagePath); } catch { }
            if (!string.IsNullOrEmpty(item.WhiskVideoPath) && File.Exists(item.WhiskVideoPath))
                try { File.Delete(item.WhiskVideoPath); } catch { }
            if (!string.IsNullOrEmpty(item.FilteredVideoPath) && File.Exists(item.FilteredVideoPath))
                try { File.Delete(item.FilteredVideoPath); } catch { }
            foreach (var video in item.SearchResults)
            {
                if (!string.IsNullOrEmpty(video.LocalPath) && File.Exists(video.LocalPath))
                    try { File.Delete(video.LocalPath); } catch { }
            }
        }

        items.Clear();

        var filePath = GetBrollPromptsFilePath(session, sessionId);
        if (filePath != null && File.Exists(filePath))
        {
            try { File.Delete(filePath); } catch { }
        }
    }

    // DTO for serialization
    private class BrollPromptSaveItem
    {
        public int Index { get; set; }
        public string Timestamp { get; set; } = "";
        public string ScriptText { get; set; } = "";
        public BrollMediaType MediaType { get; set; }
        public string Prompt { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public WhiskGenerationStatus WhiskStatus { get; set; }
        public string? WhiskImagePath { get; set; }
        public string? WhiskError { get; set; }
        public string? SelectedVideoUrl { get; set; }
        public string? LocalVideoPath { get; set; }
        public KenBurnsMotionType KenBurnsMotion { get; set; }
        public WhiskGenerationStatus WhiskVideoStatus { get; set; }
        public string? WhiskVideoPath { get; set; }
        public string? WhiskVideoError { get; set; }
        public VideoStyle Style { get; set; }
        public VideoFilter Filter { get; set; }
        public VideoTexture Texture { get; set; }
        public string? FilteredVideoPath { get; set; }
        public TextOverlay? TextOverlay { get; set; }
        public double EstimatedDurationSeconds { get; set; }
        public double StartTimeSeconds { get; set; }
        public double EndTimeSeconds { get; set; }
    }
}
