using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using BunbunBroll.Models;
using BunbunBroll.Services;
using BunbunBroll.Orchestration;
using BunbunBroll.Components.Views.ScriptGenerator;

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{

    private void SubscribeToRunningSessionsForList()
    {
        _listSubscription?.Dispose();
        foreach (var session in _sessions.Where(s => BgService.IsRunning(s.Id)))
        {
            SubscribeToSessionForList(session.Id);
        }
    }

    private void SubscribeToSessionForList(string sessionId)
    {
        var sub = EventBus.Subscribe(sessionId, evt =>
        {
            InvokeAsync(() =>
            {
                if (evt.Type == GenerationEventType.SessionCompleted ||
                    evt.Type == GenerationEventType.SessionFailed)
                {
                    _ = InvokeAsync(async () =>
                    {
                        await LoadSessionsAsync();
                        StateHasChanged();
                    });
                }
                else if (_currentView == "list")
                {
                    StateHasChanged();
                }
            });
        });
        _listSubscriptions.Add(sub);
    }

    private async Task LoadSessionsAsync()
    {
        _isLoadingSessions = true;
        try
        {
            _sessions = await ScriptService.ListSessionsAsync();
            await LoadBrollSummariesAsync();
        }
        catch { _sessions = new(); }
        finally { _isLoadingSessions = false; }
    }

    private async Task LoadBrollSummariesAsync()
    {
        _brollSummaries.Clear();
        foreach (var session in _sessions.Where(s => s.Status == SessionStatus.Completed))
        {
            try
            {
                var filePath = Path.Combine(session.OutputDirectory, "broll-prompts.json");
                if (!File.Exists(filePath)) continue;

                var json = await File.ReadAllTextAsync(filePath);
                var items = System.Text.Json.JsonSerializer.Deserialize<List<BrollPromptSaveItem>>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });

                if (items == null || items.Count == 0) continue;

                var summary = new BRollSummary
                {
                    TotalSegments = items.Count,
                    VideoCount = items.Count(i => i.MediaType == BrollMediaType.BrollVideo),
                    ImageGenCount = items.Count(i => i.MediaType == BrollMediaType.ImageGeneration),
                    VideosSelected = items.Count(i => i.MediaType == BrollMediaType.BrollVideo && (!string.IsNullOrEmpty(i.SelectedVideoUrl) || !string.IsNullOrEmpty(i.LocalVideoPath) || !string.IsNullOrEmpty(i.FilteredVideoPath))),
                    ImagesReady = items.Count(i => i.MediaType == BrollMediaType.ImageGeneration && (!string.IsNullOrEmpty(i.WhiskImagePath) || !string.IsNullOrEmpty(i.WhiskVideoPath) || !string.IsNullOrEmpty(i.FilteredVideoPath))),
                    ThumbnailPaths = items
                        .Where(i => !string.IsNullOrEmpty(i.WhiskImagePath))
                        .Select(i => ResolveLocalPath(i.WhiskImagePath)) // Use resilient resolution
                        .Where(resolved => !string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                        .Take(4)
                        .ToList()
                };
                _brollSummaries[session.Id] = summary;
            }
            catch { /* skip sessions with unreadable broll data */ }
        }
    }

    private void HandleDeleteSession(ScriptGenerationSession session)
    {
        _deleteTarget = session;
        _showDeleteConfirm = true;
    }

    private void CancelDelete()
    {
        _showDeleteConfirm = false;
        _deleteTarget = null;
    }

    private async Task ConfirmDelete()
    {
        if (_deleteTarget == null) return;
        _isDeleting = true;
        try
        {
            await ScriptService.DeleteSessionAsync(_deleteTarget.Id);
            _sessions.Remove(_deleteTarget);
        }
        catch (Exception ex) { _errorMessage = $"Gagal menghapus: {ex.Message}"; }
        finally
        {
            _isDeleting = false;
            _showDeleteConfirm = false;
            _deleteTarget = null;
            StateHasChanged();
        }
    }

    private async Task HandleViewSession(ScriptGenerationSession session)
    {
        try
        {
            Console.WriteLine($"[DEBUG] HandleViewSession called for session: {session.Id}");
            _sessionId = session.Id;
            _resultSession = session;
            _totalPhases = session.Phases.Count;

            if (session.Status == SessionStatus.Completed)
            {
                Console.WriteLine($"[DEBUG] Session is completed. Loading results and prompts...");
                await LoadResultSections(session);
                await LoadBrollPromptsFromDisk();

                // If no saved B-Roll prompts exist, parse them from the generated script so they are ready
                if (_brollPromptItems.Count == 0)
                {
                    await ParseScriptToBrollItemsAsync();
                }

                Console.WriteLine($"[DEBUG] Done loading. Switching view to results...");
                _currentView = "results";
                // _ = AutoSearchMissingBrollSegments();
            }
            else if (session.Status == SessionStatus.Running || session.Status == SessionStatus.Failed || BgService.IsRunning(session.Id))
            {
                _phaseStatuses = session.Phases.OrderBy(p => p.Order).Select(p => new PhaseStatusItem
                {
                    PhaseId = p.PhaseId, Name = p.PhaseName, Order = p.Order,
                    Status = p.Status.ToString(), WordCount = p.WordCount ?? 0
                }).ToList();
                _completedPhases = session.Phases.Count(p => p.Status == PhaseStatus.Completed);
                _progressPercent = _totalPhases > 0 ? (double)_completedPhases / _totalPhases * 100 : 0;
                _progressMessage = session.ErrorMessage ?? "Sedang berjalan...";
                _errorMessage = session.Status == SessionStatus.Failed ? session.ErrorMessage : null;
                _currentView = "progress";

                if (BgService.IsRunning(session.Id))
                {
                    SubscribeToProgress(session.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] HandleViewSession crashed: {ex}");
        }
    }

    private async Task LoadResultSections(ScriptGenerationSession session)
    {
        _resultSections.Clear();
        
        Dictionary<string, List<string>> outlineDist = new();
        if (!string.IsNullOrEmpty(session.OutlineDistributionJson))
        {
            try { outlineDist = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string>>>(session.OutlineDistributionJson) ?? new(); }
            catch { }
        }

        foreach (var phase in session.Phases.OrderBy(p => p.Order))
        {
            var content = "";
            var isMissing = false;
            var debugInfo = "";

            if (!string.IsNullOrEmpty(phase.ContentFilePath) && File.Exists(phase.ContentFilePath))
            {
                content = await File.ReadAllTextAsync(phase.ContentFilePath);
            }
            else if (!string.IsNullOrEmpty(phase.ContentFilePath) && File.Exists(Path.Combine(Directory.GetCurrentDirectory(), phase.ContentFilePath)))
            {
                content = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), phase.ContentFilePath));
            }
            else
            {
                var expectedPath = Path.Combine(Directory.GetCurrentDirectory(), "output", session.Id, "scripts", $"{phase.Order:D2}-{phase.PhaseId}.md");
                if (File.Exists(expectedPath))
                {
                    content = await File.ReadAllTextAsync(expectedPath);
                }
                else
                {
                    isMissing = true;
                    debugInfo = $"DB Path: '{phase.ContentFilePath}'\nCWD: '{Directory.GetCurrentDirectory()}'\nTried constructed: '{expectedPath}'";
                }
            }

            List<string>? points = null;
            if (outlineDist.TryGetValue(phase.PhaseId, out var pts)) points = pts;

            _resultSections.Add(new ResultSection
            {
                PhaseId = phase.PhaseId,
                PhaseName = phase.PhaseName,
                Order = phase.Order,
                Content = isMissing ? debugInfo : content,
                WordCount = phase.WordCount ?? 0,
                DurationSeconds = phase.DurationSeconds ?? 0,
                IsValidated = phase.IsValidated,
                IsExpanded = true,
                OutlinePoints = points,
                IsFileMissing = isMissing
            });
        }
        _totalWords = session.Phases.Sum(p => p.WordCount ?? 0);
        _totalMinutes = (int)(session.Phases.Sum(p => p.DurationSeconds ?? 0) / 60);
        _validatedCount = session.Phases.Count(p => p.IsValidated);
    }

}