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
using BunbunBroll.Services.Orchestration;
using BunbunBroll.Components.Views.ScriptGenerator;

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{

    private void ShowBatchForm()
    {
        _currentView = "batch";
        _batchTheme = "";
        _batchSeed = null;
        _batchError = null;
        _batchProgressCurrent = 0;
        _batchProgressTotal = 0;
        _batchResults.Clear();
    }

    private async Task HandleBatchGenerate()
    {
        Console.WriteLine($"[DEBUG] HandleBatchGenerate called. Theme: '{_batchTheme}', Channel: '{_batchChannelName}'");
        
        if (string.IsNullOrWhiteSpace(_batchTheme) || string.IsNullOrWhiteSpace(_batchChannelName)) 
        {
            Console.WriteLine("[DEBUG] Validation failed: Theme or ChannelName is empty.");
            return;
        }

        // Find selected pattern
        var selectedPattern = _availablePatterns.FirstOrDefault(p => p.Id == _batchPatternId);
        if (selectedPattern == null)
        {
            _batchError = "Error: Pattern tidak valid atau belum dipilih.";
            Console.WriteLine("[DEBUG] Validation failed: Pattern invalid or not selected.");
            return;
        }

        _isBatchGenerating = true;
        _batchError = null;
        _batchProgressCurrent = 0;
        _batchProgressTotal = _batchCount;
        _batchResults.Clear();
        StateHasChanged();

        try
        {
            Console.WriteLine("[DEBUG] Calling BatchGenerator.GenerateConfigsAsync with sequential progress...");
            
            // Define progress callback
            Action<int, int> onProgress = (current, total) =>
            {
                _batchProgressCurrent = current;
                _batchProgressTotal = total;
                InvokeAsync(StateHasChanged);
            };

            var configs = await BatchGenerator.GenerateConfigsAsync(_batchTheme, _batchChannelName, _batchCount, selectedPattern, _batchSeed, onProgress);
            Console.WriteLine($"[DEBUG] BatchGenerator returned {configs.Count} configs.");
            
            if (configs.Count == 0)
            {
                _batchError = "AI tidak menghasilkan config. Silakan coba lagi dengan tema yang berbeda atau spesifik.";
                Console.WriteLine("[DEBUG] Config count is 0. Set error message.");
            }
            else
            {
                _batchError = null; // Clear progress message
            }

            _batchResults = configs.Select(c => new BatchConfigView.GeneratedConfig
            {
                Topic = c.Topic,
                Outline = c.Outline,
                TargetDurationMinutes = c.TargetDurationMinutes,
                SourceReferences = c.SourceReferences,
                MustHaveBeats = c.MustHaveBeats,
                ChannelName = c.ChannelName
            }).ToList();
        }
        catch (Exception ex) 
        { 
            _batchError = $"Gagal: {ex.Message}"; 
            Console.WriteLine($"[DEBUG] Exception caught: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally 
        { 
            _isBatchGenerating = false; 
            StateHasChanged(); 
            Console.WriteLine("[DEBUG] HandleBatchGenerate finished. isGenerating=false");
        }
    }

    private void HandleUseConfig(BatchConfigView.GeneratedConfig config)
    {
        _topic = config.Topic;
        _outline = config.Outline;
        
        if (config.MustHaveBeats?.Count > 0)
        {
            var beats = string.Join("\n", config.MustHaveBeats.Select(b => $"- {b}"));
            if (!string.IsNullOrWhiteSpace(_outline))
                _outline += "\n\n### MUST HAVE BEATS:\n" + beats;
            else
                _outline = "### MUST HAVE BEATS:\n" + beats;
        }

        _sourceReferences = config.SourceReferences;
        _targetDuration = config.TargetDurationMinutes;
        _channelName = config.ChannelName;
        _selectedPatternId = !string.IsNullOrEmpty(_batchPatternId) ? _batchPatternId : _selectedPatternId;
        _errorMessage = null;
        _currentView = "config";
    }

    private void HandleUseAllConfigs()
    {
        if (_batchResults.Count > 0)
            HandleUseConfig(_batchResults[0]);
    }

}
