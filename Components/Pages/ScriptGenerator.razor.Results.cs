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

    private async Task HandleRegeneratePhase(ResultSection section)
    {
        if (section.IsRegenerating || _sessionId == null) return;
        section.IsRegenerating = true;
        StateHasChanged();

        try
        {
            var session = await ScriptService.RegeneratePhaseAsync(_sessionId, section.PhaseId);
            _resultSession = session;

            var updatedPhase = session.Phases.FirstOrDefault(p => p.PhaseId == section.PhaseId);
            if (updatedPhase != null)
            {
                if (!string.IsNullOrEmpty(updatedPhase.ContentFilePath) && File.Exists(updatedPhase.ContentFilePath))
                    section.Content = await File.ReadAllTextAsync(updatedPhase.ContentFilePath);
                section.WordCount = updatedPhase.WordCount ?? 0;
                section.DurationSeconds = updatedPhase.DurationSeconds ?? 0;
                section.IsValidated = updatedPhase.IsValidated;
                section.IsExpanded = true;
            }

            _totalWords = session.Phases.Sum(p => p.WordCount ?? 0);
            _totalMinutes = (int)(session.Phases.Sum(p => p.DurationSeconds ?? 0) / 60);
            _validatedCount = session.Phases.Count(p => p.IsValidated);
        }
        catch (Exception ex) { _errorMessage = $"Regenerate gagal: {ex.Message}"; }
        finally { section.IsRegenerating = false; StateHasChanged(); }

        InvalidateBrollClassification();
    }

    private async Task HandleRegenerateAll()
    {
        if (_isRegeneratingAll || _sessionId == null) return;

        if (BgService.IsRunning(_sessionId))
        {
            _errorMessage = "Generasi masih berjalan. Tunggu sampai selesai atau batalkan dulu.";
            StateHasChanged();
            return;
        }

        _isRegeneratingAll = true;
        _errorMessage = null;
        InvalidateBrollClassification();
        StateHasChanged();

        try
        {
            _progressMessage = "Mempersiapkan regenerasi...";
            _completedPhases = 0;
            _progressPercent = 0;

            // Reset all phases to Pending so they regenerate with new config
            Console.WriteLine($"[DEBUG] HandleRegenerateAll: Resetting phases for session {_sessionId}");
            await ScriptService.ResetSessionPhasesAsync(_sessionId);

            _phaseStatuses = _resultSections.Select(s => new PhaseStatusItem
            {
                PhaseId = s.PhaseId, Name = s.PhaseName, Order = s.Order, Status = "Pending"
            }).ToList();

            _currentView = "progress";
            StateHasChanged();

            _progressMessage = "Regenerating semua fase...";
            SubscribeToProgress(_sessionId);
            BgService.EnqueueGeneration(_sessionId);
        }
        catch (Exception ex) { _errorMessage = $"Regenerate All gagal: {ex.Message}"; }
        finally { _isRegeneratingAll = false; StateHasChanged(); }
    }

    private async Task HandleCopyPhaseContent(ResultSection section)
    {
        try
        {
            await JS.InvokeVoidAsync("copyToClipboard", section.Content);
            section.CopyState = "copied";
            StateHasChanged();

            _ = InvokeAsync(async () =>
            {
                await Task.Delay(2000);
                section.CopyState = "idle";
                StateHasChanged();
            });
        }
        catch { }
    }

    private async Task HandleBackToList() { await LoadSessionsAsync(); _currentView = "list"; _errorMessage = null; }
    private void HandleBackToResults() { _currentView = "results"; _classifyError = null; }

    private async Task HandleUpdatePhaseContent((ResultSection Section, string NewContent) args)
    {
        try
        {
            if (_resultSession == null) return;
            
            await ScriptService.UpdatePhaseContentAsync(_resultSession.Id, args.Section.PhaseId, args.NewContent);
            
            // Update local state
            args.Section.Content = args.NewContent;
            
            // Recalculate totals
            _totalWords = _resultSections.Sum(s => s.WordCount);
            // Simple re-estimation of duration
            var totalSeconds = _resultSections.Sum(s => s.DurationSeconds);
            _totalMinutes = (int)Math.Ceiling(totalSeconds / 60.0);
            
            StateHasChanged();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Gagal menyimpan perubahan: {ex.Message}";
            Console.Error.WriteLine(ex);
        }
    }

    private async Task HandleExportLrc()
    {
        if (_isExportingLrc || _resultSession == null) return;
        _isExportingLrc = true;
        _errorMessage = null;
        _lrcExportPath = null;
        StateHasChanged();

        try
        {
            var lrcContent = GenerateLrcContent();
            var safeTopic = System.Text.RegularExpressions.Regex.Replace(_resultSession.Topic ?? "script", @"[^a-zA-Z0-9\-]", "_");
            var safeFilename = $"{safeTopic}_{DateTime.Now:yyyyMMddHHmm}";
            var lrcPath = System.IO.Path.Combine(Environment.CurrentDirectory, "wwwroot", "exports", $"{safeFilename}.lrc");
            
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(lrcPath)!);
            await File.WriteAllTextAsync(lrcPath, lrcContent, System.Text.Encoding.UTF8);

            // Trigger download
            var bytes = System.Text.Encoding.UTF8.GetBytes(lrcContent);
            var base64 = Convert.ToBase64String(bytes);
            await JS.InvokeVoidAsync("downloadFile", $"{safeFilename}.lrc", base64, "text/plain");

            _lrcExportPath = lrcPath;
        }
        catch (Exception ex)
        {
            _errorMessage = $"Export LRC gagal: {ex.Message}";
        }
        finally
        {
            _isExportingLrc = false;
            StateHasChanged();
        }
    }


  
    private string GenerateLrcContent()
    {
        var sb = new System.Text.StringBuilder();
        var globalOffset = TimeSpan.Zero;
        // Regex to parse [mm:ss] or [mm:ss.f] or [mm:ss.ff] or [mm:ss.fff]
        // \d{1,3} : minutes (can be > 99)
        // \d{2}   : seconds
        // (?:.(\d{1,3}))? : optional milliseconds part
        var timestampPattern = new System.Text.RegularExpressions.Regex(@"\[(\d{1,3}):(\d{2})(?:\.(\d{1,3}))?\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var section in _resultSections.OrderBy(s => s.Order))
        {
            if (string.IsNullOrWhiteSpace(section.Content)) continue;
            var entries = ParseTimestampedEntries(section.Content, timestampPattern);

            if (entries.Count == 0)
            {
                var cleaned = CleanSubtitleText(section.Content);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    var durationSec = EstimateDuration(cleaned);
                    var duration = TimeSpan.FromSeconds(durationSec);
                    
                    var lines = SplitAndTimestampText(cleaned, globalOffset, duration);
                    foreach(var line in lines) sb.AppendLine(line);

                    globalOffset = globalOffset.Add(duration);
                }
                continue;
            }

            var phaseBase = entries[0].Timestamp;
            
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var normalizedTime = entry.Timestamp - phaseBase;
                if (normalizedTime < TimeSpan.Zero) normalizedTime = TimeSpan.Zero;
                var absoluteTime = globalOffset.Add(normalizedTime);
                
                var cleaned = CleanSubtitleText(entry.Text);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;

                TimeSpan entryDuration;
                if (i < entries.Count - 1)
                {
                     var nextNormalized = entries[i + 1].Timestamp - phaseBase;
                     if (nextNormalized < TimeSpan.Zero) nextNormalized = TimeSpan.Zero;
                     entryDuration = nextNormalized - normalizedTime;
                }
                else
                {
                     entryDuration = TimeSpan.FromSeconds(EstimateDuration(entry.Text));
                }

                // If duration is too short (e.g. same timestamp), fallback to estimate
                if (entryDuration.TotalSeconds < 1)
                     entryDuration = TimeSpan.FromSeconds(EstimateDuration(entry.Text));

                var lines = SplitAndTimestampText(cleaned, absoluteTime, entryDuration);
                foreach(var line in lines) sb.AppendLine(line);
            }

            // Update globalOffset for next phase
            if (entries.Count > 0)
            {
                var lastEntry = entries.Last();
                var lastNormTime = lastEntry.Timestamp - phaseBase;
                if (lastNormTime < TimeSpan.Zero) lastNormTime = TimeSpan.Zero;
                var lastEntryDuration = TimeSpan.FromSeconds(EstimateDuration(lastEntry.Text));
                globalOffset = globalOffset.Add(lastNormTime).Add(lastEntryDuration);
            }
        }

        return sb.ToString();
    }

    private List<string> SplitAndTimestampText(string text, TimeSpan startTime, TimeSpan duration)
    {
        const int MaxChars = 450; // Safer limit well below 490
        var result = new List<string>();
        
        var singleLine = System.Text.RegularExpressions.Regex.Replace(
                    text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " "), @"\s+", " ").Trim();

        if (string.IsNullOrWhiteSpace(singleLine)) return result;

        if (singleLine.Length <= MaxChars)
        {
             result.Add(FormatLrcLine(startTime, singleLine));
             return result;
        }

        var words = singleLine.Split(' ');
        var chunks = new List<string>();
        var currentChunk = new System.Text.StringBuilder();

        foreach (var word in words)
        {
            // If a single word is excessively long, split it forcefully
            if (word.Length > MaxChars)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                var remainingWord = word;
                while (remainingWord.Length > MaxChars)
                {
                    chunks.Add(remainingWord.Substring(0, MaxChars));
                    remainingWord = remainingWord.Substring(MaxChars);
                }
                currentChunk.Append(remainingWord).Append(" ");
                continue;
            }

            if (currentChunk.Length + word.Length + 1 > MaxChars)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }
            currentChunk.Append(word).Append(" ");
        }
        if (currentChunk.Length > 0) chunks.Add(currentChunk.ToString().Trim());

        // Distribute duration based on char count fraction
        double totalChars = singleLine.Length;
        var currentTime = startTime;
        
        foreach (var chunk in chunks)
        {
            result.Add(FormatLrcLine(currentTime, chunk));
            
            // Interpolate next start time
            // Duration of this chunk = TotalDuration * (ChunkLen / TotalLen)
            if (totalChars > 0)
            {
                var chunkDurationMs = duration.TotalMilliseconds * ((double)chunk.Length / totalChars);
                currentTime = currentTime.Add(TimeSpan.FromMilliseconds(chunkDurationMs));
            }
        }

        return result;
    }

    private string FormatLrcLine(TimeSpan ts, string text)
    {
        var minutes = (int)ts.TotalMinutes;
        var seconds = ts.Seconds;
        var centiseconds = ts.Milliseconds / 10;
        // Standard LRC format is [mm:ss.xx] (centiseconds)
        return $"[{minutes:D2}:{seconds:D2}.{centiseconds:D2}]{text}";
    }

    // Helper to parse content with timestamps
    private List<TimestampedEntry> ParseTimestampedEntries(string content, System.Text.RegularExpressions.Regex pattern)
    {
        var result = new List<TimestampedEntry>();
        var matches = pattern.Matches(content);
        
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (!int.TryParse(match.Groups[1].Value, out var m)) continue;
            if (!int.TryParse(match.Groups[2].Value, out var s)) continue;
            
            var ms = 0;
            if (match.Groups[3].Success)
            {
                var val = match.Groups[3].Value;
                // Normalize "1" -> 100ms, "10" -> 100ms (if it was .10), need to be careful.
                // Actually usually .1 = 100ms, .01 = 10ms.
                // But .123 = 123ms. 
                // Let's assume standard decimal fraction parsing
                if (val.Length == 1) val += "00";
                else if (val.Length == 2) val += "0";
                
                if (int.TryParse(val, out var parsedMs)) ms = parsedMs;
            }

            var ts = new TimeSpan(0, 0, m, s, ms);
            var startIndex = match.Index + match.Length;
            var length = (i < matches.Count - 1) ? matches[i + 1].Index - startIndex : content.Length - startIndex;
            
            if (length <= 0) continue;
            
            var text = content.Substring(startIndex, length).Trim();
            result.Add(new TimestampedEntry(ts, text));
        }

        return result;
    }

    public record TimestampedEntry(TimeSpan Timestamp, string Text);

    private static bool IsOnlyHeader(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith('#') ||
               System.Text.RegularExpressions.Regex.IsMatch(trimmed,
                   @"^\s*(?:Opening Hook|Kontekstualisasi|Multi-Dimensi|Climax|Eschatology|Contextualization|Content|Refleksi)\s*$",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static int EstimateDuration(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var baseDuration = words.Length / 2.33;
        var ellipsisCount = System.Text.RegularExpressions.Regex.Matches(text, @"\.\.\.").Count;
        var totalDuration = baseDuration + (ellipsisCount * 0.5);
        return (int)Math.Ceiling(Math.Max(3, totalDuration));
    }

    private static string CleanSubtitleText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var result = text;

        // Strip overlay tags and their content entirely (these are visual overlay markers, not narration)
        // Remove [OVERLAY:Type] or OVERLAY:Type tags
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[?OVERLAY:\w+\]?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Remove [ARABIC] and everything until the next tag or end of line
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[?ARABIC\]?:?\s*.*?(?=\[?(?:REF|TEXT|OVERLAY)\]?|$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        // Remove [REF] and its content
        result = System.Text.RegularExpressions.Regex.Replace(result, @"(?:\[?REF\]?|\bREF\b)\s*:?\s*.*?(?=\[?(?:TEXT|OVERLAY)\]?|$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        // Remove [TEXT] and its content
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[?TEXT\]?\s*:?\s*.*?(?=\[?\d{2}:\d{2}\]?|$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        // Clean up any remaining standalone tag remnants
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[?ARABIC\]?:?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[?REF\]?:?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[?TEXT\]?:?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        result = System.Text.RegularExpressions.Regex.Replace(result, @"^#+\s+.*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"^\s*(?:Opening Hook|Kontekstualisasi|Multi-Dimensi|Climax|Eschatology|Contextualization|Content|Segment \d+):?\s*$", "", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[\d{1,3}:\d{2}(?::\d{2})?\]", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\b\d{1,3}:\d{2}(?::\d{2})?\b", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[(?:Visual|Musik|Music|Efek|Effect|SFX|Audio|PAUSE)[^\]]*\]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\([^)]*\)", "");
        result = result.Replace("\"", "").Replace("'", "");
        result = result.Replace("*", "").Replace("_", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"#\S+", "");
        result = result.Replace("[", "").Replace("]", "");
        result = result.Replace(".. .", "").Replace("...", "");
        result = result.Replace("—", " ").Replace("–", " ").Replace("-", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
        result = result.Replace("Wallahuam bissawab", "Wallahu a'lam bish-shawab");
        result = result.Replace("Wallahualam bissawab", "Wallahu a'lam bish-shawab");
        
        return result.Trim();
    }

    private class PhaseStatusItem
    {
        public string PhaseId { get; set; } = "";
        public string Name { get; set; } = "";
        public int Order { get; set; }
        public string Status { get; set; } = "Pending";
        public int WordCount { get; set; }
        public List<string>? OutlinePoints { get; set; }
        public string DurationTarget { get; set; } = "";
    }

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
        public KenBurnsMotionType KenBurnsMotion { get; set; }
        public WhiskGenerationStatus WhiskVideoStatus { get; set; }
        public string? WhiskVideoPath { get; set; }
        public string? WhiskVideoError { get; set; }
        public VideoStyle Style { get; set; }
        public VideoFilter Filter { get; set; }
        public VideoTexture Texture { get; set; }
        public string? FilteredVideoPath { get; set; }
        public TextOverlay? TextOverlay { get; set; }
    }

}