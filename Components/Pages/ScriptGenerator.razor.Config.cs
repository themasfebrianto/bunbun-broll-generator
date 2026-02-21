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

namespace BunbunBroll.Components.Pages;

public partial class ScriptGenerator
{

    private string GetBreadcrumbTitle()
    {
        return _currentView switch
        {
            "config" => "Buat Script Baru",
            "batch" => "Batch Generate",
            "progress" => _topic ?? "Generating...",
            "results" => _resultSession?.Topic ?? "Script Editor",
            "broll-prompts" => _resultSession?.Topic ?? "B-Roll Prompts",
            _ => "Script Dashboard"
        };
    }

    private void ShowConfigForm()
    {
        _currentView = "config";
        _topic = "";
        _outline = null;
        _sourceReferences = null;
        _errorMessage = null;
    }

    private void HandleEditConfig()
    {
        if (_resultSession == null) return;
        _editTopic = _resultSession.Topic;
        _editOutline = _resultSession.Outline;
        _editSourceReferences = _resultSession.SourceReferences;
        _editTargetDuration = _resultSession.TargetDurationMinutes;
        _editChannelName = _resultSession.ChannelName;
        _showEditConfig = true;
    }

    private void CancelEditConfig()
    {
        _showEditConfig = false;
        _isSavingConfig = false;
    }

    private async Task SaveAndRegenerateConfig()
    {
        if (_resultSession == null) return;
        _isSavingConfig = true;
        _errorMessage = null;

        try
        {
            var config = new ScriptConfig
            {
                Topic = _editTopic,
                Outline = _editOutline,
                TargetDurationMinutes = _editTargetDuration,
                SourceReferences = _editSourceReferences,
                ChannelName = _editChannelName
            };

            Console.WriteLine($"[DEBUG] SaveAndRegenerateConfig: Sending TargetDurationMinutes = {_editTargetDuration}");

            await ScriptService.UpdateSessionAsync(_resultSession.Id, config);
            
            _resultSession.Topic = config.Topic;
            _resultSession.Outline = config.Outline;
            _resultSession.TargetDurationMinutes = config.TargetDurationMinutes;
            _resultSession.SourceReferences = config.SourceReferences;
            _resultSession.ChannelName = config.ChannelName;
            
            _showEditConfig = false;
            await HandleRegenerateAll();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Gagal menyimpan config: {ex.Message}";
        }
        finally
        {
            _isSavingConfig = false;
            StateHasChanged();
        }
    }

    private async Task HandleGenerate()
    {
        if (!CanGenerate) return;

        if (_sessionId != null && BgService.IsRunning(_sessionId))
        {
            _errorMessage = "Session ini sedang dalam proses generasi. Tunggu sampai selesai atau batalkan dulu.";
            return;
        }

        _isGenerating = true;
        _errorMessage = null;

        try
        {
            var session = await ScriptService.CreateSessionAsync(_selectedPatternId, _topic, _outline, _targetDuration, _sourceReferences, _channelName);
            _sessionId = session.Id;
            _totalPhases = session.Phases.Count;
            _completedPhases = 0;
            _progressPercent = 0;
            _progressMessage = "Mempersiapkan...";

            _phaseStatuses = session.Phases.OrderBy(p => p.Order).Select(p => new PhaseStatusItem
            {
                PhaseId = p.PhaseId, Name = p.PhaseName, Order = p.Order, Status = "Pending"
            }).ToList();

            _currentView = "progress";
            StateHasChanged();

            SubscribeToProgress(session.Id);
            SubscribeToSessionForList(session.Id);
            BgService.EnqueueGeneration(session.Id);
        }
        catch (Exception ex) { _errorMessage = $"Error: {ex.Message}"; }
        finally { _isGenerating = false; StateHasChanged(); }
    }

    private void SubscribeToProgress(string sessionId)
    {
        _progressSubscription?.Dispose();
        _progressSubscription = EventBus.Subscribe(sessionId, evt =>
        {
            InvokeAsync(() =>
            {
                switch (evt.Type)
                {
                    case GenerationEventType.PhaseStarted:
                    case GenerationEventType.PhaseCompleted:
                    case GenerationEventType.PhaseFailed:
                        var phase = _phaseStatuses.FirstOrDefault(p => p.PhaseId == evt.PhaseId);
                        if (phase != null)
                        {
                            phase.Status = evt.PhaseStatus ?? evt.Type.ToString();
                            if (evt.OutlinePoints?.Count > 0)
                                phase.OutlinePoints = evt.OutlinePoints;
                            if (!string.IsNullOrEmpty(evt.DurationTarget))
                                phase.DurationTarget = evt.DurationTarget;
                        }
                        _progressMessage = evt.Message;
                        break;

                    case GenerationEventType.SessionProgress:
                        _completedPhases = evt.CompletedPhases;
                        _totalPhases = evt.TotalPhases;
                        _progressPercent = evt.ProgressPercent;
                        _progressMessage = evt.Message;
                        break;

                    case GenerationEventType.SessionCompleted:
                        _progressSubscription?.Dispose();
                        _ = InvokeAsync(async () =>
                        {
                            var completedSession = await ScriptService.GetSessionAsync(sessionId);
                            if (completedSession != null)
                            {
                                _resultSession = completedSession;
                                await LoadResultSections(completedSession);
                                await LoadBrollPromptsFromDisk();
                                _currentView = "broll-prompts";
                                _ = AutoSearchMissingBrollSegments();
                            }
                            StateHasChanged();
                        });
                        break;

                    case GenerationEventType.SessionFailed:
                        _progressSubscription?.Dispose();
                        _errorMessage = evt.Message;
                        break;
                }
                StateHasChanged();
            });
        });
    }

    private async Task HandleCancelGeneration()
    {
        if (_isCancelling || string.IsNullOrEmpty(_sessionId)) return;
        _isCancelling = true;
        StateHasChanged();

        if (BgService.CancelGeneration(_sessionId))
        {
            _progressMessage = "Membatalkan generasi...";
            await Task.Delay(500);
        }
        else
        {
            _errorMessage = "Tidak bisa membatalkan: generasi tidak sedang berjalan.";
        }

        _isCancelling = false;
        StateHasChanged();
    }

}