using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using BunbunBroll.Services;
using BunbunBroll.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{
    // Expansion state
    private bool _isExpanding = false;
    private bool _isSlicing = false;
    private bool _isValidating = false;
    private string? _expansionError = null;
    private bool _showExpansionDetails = false;
    private string? _processingStatus = null;
    private int _processingProgress = 0;

    // Expansion Configuration
    private bool _usePadCap = true;
    private double _padCapMs = 300.0;

    // File paths
    private string? _voFilePath = null;
    private string? _srtFilePath = null;
    private string? _stitchedVoUrl = null;

    // Expansion data
    private SrtExpansionResult? _expansionResult = null;
    private List<SrtEntry>? _expandedEntries = null;
    private Dictionary<int, double>? _pauseDurations = null;
    private ExpansionStats? _expansionStats = null;
    private List<VoSegment>? _voSegments = null;
    private VoSliceValidationResult? _validationResult = null;

    [Inject] private ISrtExpansionService SrtExpansionService { get; set; } = null!;
    [Inject] private IVoSlicingService VoSlicingService { get; set; } = null!;

    private async void DetectExistingVoAndSrt()
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? "");
        if (string.IsNullOrEmpty(_sessionId) || !Directory.Exists(outputDir)) return;

        // Detect existing VO file
        if (string.IsNullOrEmpty(_voFilePath) || !File.Exists(_voFilePath))
        {
            var voFiles = Directory.GetFiles(outputDir, "original_vo.*");
            if (voFiles.Length > 0)
            {
                _voFilePath = voFiles[0];
            }
        }

        // Detect existing SRT file
        if (string.IsNullOrEmpty(_srtFilePath) || !File.Exists(_srtFilePath))
        {
            var srtPath = Path.Combine(outputDir, "uploaded_capcut.srt");
            if (File.Exists(srtPath))
            {
                _srtFilePath = srtPath;
            }
        }

        // Detect previous processing results
        var stitchedPath = Path.Combine(outputDir, "stitched_vo.mp3");
        var expandedSrtDir = Path.Combine(outputDir, "srt");
        var expandedSrtPath = Path.Combine(expandedSrtDir, "expanded.srt");

        if (File.Exists(stitchedPath) && File.Exists(expandedSrtPath) && _expandedEntries == null)
        {
            try
            {
                // Reload expanded SRT entries
                var srtContent = File.ReadAllText(expandedSrtPath);
                _expandedEntries = SrtService.ParseSrt(srtContent);

                // Recalculate pause durations from entries
                _pauseDurations = SrtService.CalculatePauseDurations(_expandedEntries);

                // Reload persisted expansion metadata (overlays, LLM status)
                var metadataPath = Path.Combine(outputDir, "expansion-result.json");
                if (File.Exists(metadataPath))
                {
                    var metaJson = File.ReadAllText(metadataPath);
                    var metadata = JsonSerializer.Deserialize<ExpansionResultMetadata>(metaJson, _jsonOptions);
                    if (metadata != null)
                    {
                        _expansionResult = new SrtExpansionResult
                        {
                            IsSuccess = true,
                            LlmDetectionSuccess = metadata.LlmDetectionSuccess,
                            LlmDetectionWarning = metadata.LlmDetectionWarning,
                            LlmTokensUsed = metadata.LlmTokensUsed,
                            DetectedOverlays = metadata.DetectedOverlays ?? new(),
                            PauseDurations = metadata.PauseDurations ?? new(),
                            ExpandedEntries = _expandedEntries,
                            Statistics = metadata.Statistics ?? new()
                        };
                        _pauseDurations = metadata.PauseDurations ?? _pauseDurations;
                        _expansionStats = metadata.Statistics;
                    }
                }

                // Reload persisted validation result (enables the Proceed button)
                var validationPath = Path.Combine(outputDir, "validation-result.json");
                if (File.Exists(validationPath))
                {
                    try
                    {
                        var valJson = File.ReadAllText(validationPath);
                        _validationResult = JsonSerializer.Deserialize<VoSliceValidationResult>(valJson, _jsonOptions);
                    }
                    catch { /* validation will be null, user can re-process */ }
                }

                // Reconstruct _voSegments from sliced audio files on disk
                var segmentsDir = Path.Combine(outputDir, "vo_segments");
                if (Directory.Exists(segmentsDir) && _expandedEntries != null)
                {
                    _voSegments = new List<VoSegment>();
                    for (int i = 0; i < _expandedEntries.Count; i++)
                    {
                        var entry = _expandedEntries[i];
                        // Match segment files by index pattern (e.g., segment_001.wav - 1-based)
                        var segFile = Directory.GetFiles(segmentsDir, $"segment_{i + 1:D3}.*").FirstOrDefault();
                        if (segFile != null)
                        {
                            _voSegments.Add(new VoSegment
                            {
                                Index = i + 1,
                                AudioPath = segFile,
                                StartTime = entry.StartTime,
                                EndTime = entry.EndTime,
                                DurationSeconds = entry.Duration.TotalSeconds,
                                Text = entry.Text,
                                IsValid = true
                            });
                        }
                    }
                }

                // Set player URL explicitly so the UI shows up
                _stitchedVoUrl = $"/project-assets/{_sessionId}/stitched_vo.mp3";
                _showExpansionDetails = true;
                await InvokeAsync(StateHasChanged);

                // BACKWARD COMPATIBILITY: If validation result wasn't found (old session before fix), compute it now
                if (_validationResult == null && _voSegments?.Count > 0 && _expandedEntries?.Count > 0)
                {
                    try
                    {
                        // Set a quick temporary state so users know we are validating
                        _isValidating = true;
                        await InvokeAsync(StateHasChanged);

                        // For old sessions, we need actual durations to run validation properly
                        foreach (var seg in _voSegments)
                        {
                            seg.ActualDurationSeconds = await VoSlicingService.GetAudioDurationAsync(seg.AudioPath);
                        }
                        
                        _validationResult = await VoSlicingService.ValidateSlicedSegmentsAsync(_voSegments, _expandedEntries);
                        
                        if (_resultSession != null)
                        {
                            _resultSession.SliceValidationResult = _validationResult;
                        }

                        // Save it to avoid re-running next time
                        var validationJson = JsonSerializer.Serialize(_validationResult, _jsonOptions);
                        await File.WriteAllTextAsync(validationPath, validationJson);
                    }
                    catch { /* validation failed, UI just won't show it */ }
                    finally
                    {
                        _isValidating = false;
                        await InvokeAsync(StateHasChanged);
                    }
                }
            }
            catch
            {
                // If reload fails, user can re-process
            }
        }
    }

    private async Task HandleVoFileUpload(InputFileChangeEventArgs e)
    {
        try
        {
            var file = e.File;
            if (file == null) return;

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputDir);

            // Delete old VO files
            foreach (var old in Directory.GetFiles(outputDir, "original_vo.*"))
            {
                try { File.Delete(old); } catch { }
            }

            var filePath = Path.Combine(outputDir, "original_vo" + Path.GetExtension(file.Name));
            using var stream = file.OpenReadStream(maxAllowedSize: 100 * 1024 * 1024);
            using var fs = new FileStream(filePath, FileMode.Create);
            await stream.CopyToAsync(fs);

            _voFilePath = filePath;
            _expansionError = null;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _expansionError = $"VO upload failed: {ex.Message}";
        }
    }

    private async Task HandleSrtFileUpload(InputFileChangeEventArgs e)
    {
        try
        {
            var file = e.File;
            if (file == null) return;

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? Guid.NewGuid().ToString());
            Directory.CreateDirectory(outputDir);

            // Delete old SRT file
            var oldSrt = Path.Combine(outputDir, "uploaded_capcut.srt");
            if (File.Exists(oldSrt))
            {
                try { File.Delete(oldSrt); } catch { }
            }

            var filePath = Path.Combine(outputDir, "uploaded_capcut.srt");
            using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            using var fs = new FileStream(filePath, FileMode.Create);
            await stream.CopyToAsync(fs);

            _srtFilePath = filePath;
            _expansionError = null;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _expansionError = $"SRT upload failed: {ex.Message}";
        }
    }

    private async Task HandleProcessVo()
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? Guid.NewGuid().ToString());
        var capCutSrtPath = _srtFilePath;
        
        if (string.IsNullOrEmpty(capCutSrtPath) || !File.Exists(capCutSrtPath))
        {
            _expansionError = "Please upload a CapCut SRT file.";
            return;
        }

        if (string.IsNullOrEmpty(_voFilePath) || !File.Exists(_voFilePath))
        {
            _expansionError = "Please upload VO file first";
            return;
        }

        _isExpanding = true;
        _isSlicing = true;
        _expansionError = null;
        StateHasChanged();

        try
        {
            // 1. Expand SRT
            SetProgress("Expanding SRT entries...", 10);
            var expansionResult = await SrtExpansionService.ExpandCapCutSrtAsync(
                capCutSrtPath,
                _sessionId ?? Guid.NewGuid().ToString(),
                outputDir,
                _usePadCap,
                _padCapMs
            );

            if (!expansionResult.IsSuccess)
            {
                _expansionError = expansionResult.ErrorMessage ?? "Expansion failed";
                return;
            }

            _expandedEntries = expansionResult.ExpandedEntries;
            _pauseDurations = expansionResult.PauseDurations;
            _expansionStats = expansionResult.Statistics;
            _expansionResult = expansionResult;

            // Persist expansion metadata for reload
            await SaveExpansionMetadataAsync(outputDir, expansionResult);

            if (_resultSession != null)
            {
                _resultSession.ExpandedSrtPath = expansionResult.ExpandedSrtPath;
                _resultSession.ExpandedAt = DateTime.UtcNow;
                _resultSession.ExpansionStatistics = expansionResult.Statistics;
            }

            // 2. Slice VO
            SetProgress("Slicing VO into segments...", 30);
            var sliceResult = await VoSlicingService.SliceVoAsync(
                _voFilePath,
                _expandedEntries,
                outputDir
            );

            if (!sliceResult.IsSuccess)
            {
                _expansionError = $"Slicing failed: {string.Join(", ", sliceResult.Errors)}";
                return;
            }

            _voSegments = sliceResult.Segments;

            if (_resultSession != null)
            {
                _resultSession.VoSegments = _voSegments;
                _resultSession.VoSegmentsDirectory = sliceResult.OutputDirectory;
            }

            // Calculate and append Tail Silence
            var lastEntry = _expandedEntries.LastOrDefault();
            if (lastEntry != null && sliceResult.SourceDurationSeconds > 0)
            {
                var tailSilence = sliceResult.SourceDurationSeconds - lastEntry.OriginalEndTime.TotalSeconds;
                if (tailSilence > 0)
                {
                    // Index of the last entry represents the pause AFTER the last word
                    _pauseDurations[_expandedEntries.Count - 1] = Math.Round(tailSilence, 3);
                }
            }

            // RE-CALCULATE ACTUAL TIMINGS based on sliced WAV exact durations to eliminate cumulative drift
            SrtService.RetimeEntriesWithActualDurations(_expandedEntries, _voSegments, _pauseDurations);

            // Rewrite expanded.srt and expanded.lrc with the corrected precision timings
            var srtDir = Path.Combine(outputDir, "srt");
            Directory.CreateDirectory(srtDir);
            var updatedSrtContent = SrtService.FormatExpandedSrt(_expandedEntries, _expansionResult?.DetectedOverlays);
            await File.WriteAllTextAsync(Path.Combine(srtDir, "expanded.srt"), updatedSrtContent);
            
            var sbLrc = new System.Text.StringBuilder();
            foreach (var entry in _expandedEntries)
            {
                var minutes = (int)entry.StartTime.TotalMinutes;
                var seconds = entry.StartTime.Seconds;
                var centiseconds = entry.StartTime.Milliseconds / 10;
                sbLrc.AppendLine($"[{minutes:D2}:{seconds:D2}.{centiseconds:D2}]{entry.Text}");
            }
            await File.WriteAllTextAsync(Path.Combine(srtDir, "expanded.lrc"), sbLrc.ToString());

            // Update session tracking with corrected data if needed
            if (_resultSession != null && _expansionResult != null)
            {
                await SaveExpansionMetadataAsync(outputDir, _expansionResult);
            }

            // 3. Stitch VO back together with pauses
            SetProgress("Stitching segments with pauses...", 60);
            var stitchedPath = await VoSlicingService.StitchVoAsync(_voSegments, _pauseDurations, sliceResult.OutputDirectory);
            if (!string.IsNullOrEmpty(stitchedPath))
            {
                if (_resultSession != null)
                {
                    _resultSession.StitchedVoPath = stitchedPath;
                }
                
                // Set relative URL for player
                // Assuming "output" is mapped to "/project-assets"
                _stitchedVoUrl = $"/project-assets/{_sessionId}/stitched_vo.mp3?t={DateTime.UtcNow.Ticks}";
            }
            else
            {
                _expansionError = "VO stitching failed.";
                return;
            }

            // 4. Validate
            SetProgress("Validating sliced segments...", 85);
            await HandleValidateSlices();
            SetProgress("Done!", 100);
            _showExpansionDetails = true;
        }
        catch (Exception ex)
        {
            _expansionError = $"Processing failed: {ex.Message}";
        }
        finally
        {
            _isExpanding = false;
            _isSlicing = false;
            _processingStatus = null;
            _processingProgress = 0;
            StateHasChanged();
        }
    }

    private async Task HandleValidateSlices()
    {
        if (_voSegments == null || _expandedEntries == null)
            return;

        _isValidating = true;
        StateHasChanged();

        try
        {
            _validationResult = await VoSlicingService.ValidateSlicedSegmentsAsync(_voSegments, _expandedEntries);

            if (_resultSession != null)
            {
                _resultSession.SliceValidationResult = _validationResult;
            }

            // Persist validation result to disk for reload
            try
            {
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? "");
                if (Directory.Exists(outputDir))
                {
                    var validationJson = JsonSerializer.Serialize(_validationResult, _jsonOptions);
                    await File.WriteAllTextAsync(Path.Combine(outputDir, "validation-result.json"), validationJson);
                }
            }
            catch { /* non-critical */ }
            
            // Note: OnExpansionComplete() will be called manually by user clicking "Proceed" rather than auto-proceeding
        }
        catch (Exception ex)
        {
            _expansionError = $"Validation failed: {ex.Message}";
        }
        finally
        {
            _isValidating = false;
            StateHasChanged();
        }
    }

    private void SetProgress(string status, int progress)
    {
        _processingStatus = status;
        _processingProgress = progress;
        InvokeAsync(StateHasChanged);
    }

    // === Expansion Persistence ===

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task SaveExpansionMetadataAsync(string outputDir, SrtExpansionResult result)
    {
        try
        {
            var metadata = new ExpansionResultMetadata
            {
                LlmDetectionSuccess = result.LlmDetectionSuccess,
                LlmDetectionWarning = result.LlmDetectionWarning,
                LlmTokensUsed = result.LlmTokensUsed,
                DetectedOverlays = result.DetectedOverlays,
                PauseDurations = result.PauseDurations,
                Statistics = result.Statistics
            };
            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "expansion-result.json"), json);
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Lightweight DTO for persisting expansion results to disk.
    /// </summary>
    private class ExpansionResultMetadata
    {
        public bool LlmDetectionSuccess { get; set; }
        public string? LlmDetectionWarning { get; set; }
        public int LlmTokensUsed { get; set; }
        public Dictionary<int, TextOverlayDto> DetectedOverlays { get; set; } = new();
        public Dictionary<int, double> PauseDurations { get; set; } = new();
        public ExpansionStats? Statistics { get; set; }
    }
}
