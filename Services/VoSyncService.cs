using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using BunbunBroll.Models;
using BunbunBroll.Utils;
using Microsoft.Extensions.Logging;

namespace BunbunBroll.Services;

public class VoSyncService
{
    private readonly ILogger<VoSyncService> _logger;

    public VoSyncService(ILogger<VoSyncService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Syncs CapCut VO to the App's true timeline by matching SRTs, slicing audio, and adding silence.
    /// </summary>
    public async Task<string> SyncVoiceoverToAppTimeline(
        string capCutVoMp3Path, 
        string capCutVoSrtPath, 
        string appTruthSrtPath, 
        string outputDir)
    {
        _logger.LogInformation("Starting Hybrid VO Sync...");

        // 1. Parse both SRTs
        var capCutSubtitles = ParseSrt(capCutVoSrtPath);
        var appSubtitles = ParseSrt(appTruthSrtPath);

        // 2. Align timestamps using Hybrid Approach
        var alignedSubtitles = AlignTimestamps(appSubtitles, capCutSubtitles);

        // 3. Prepare Ffmpeg and Slice/Pad
        var concatListPath = Path.Combine(outputDir, "vo_ffmpeg_concat.txt");
        var concatLines = new List<string>();
        
        // This is to keep track of the NEW timeline
        TimeSpan currentPlayhead = TimeSpan.Zero;
        var finalAdjustedSubtitles = new List<SrtEntry>();

        for (int i = 0; i < appSubtitles.Count; i++)
        {
            var appTarget = appSubtitles[i];
            var mappedSource = alignedSubtitles[i];

            // If we couldn't align, we fallback to just taking the duration of the CapCut (or 0)
            TimeSpan duration = mappedSource?.Duration ?? TimeSpan.FromSeconds(2);
            TimeSpan capCutStartOffset = mappedSource?.StartTime ?? TimeSpan.Zero;

            // Calculate needed silence BEFORE this block
            TimeSpan silenceNeeded = appTarget.StartTime - currentPlayhead;

            if (silenceNeeded.TotalMilliseconds > 0)
            {
                string silenceFile = Path.Combine(outputDir, $"silence_{i}.mp3");
                await RunFfmpegAsync($"-f lavfi -i anullsrc=r=44100:cl=stereo -t {silenceNeeded.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -b:a 192k \"{silenceFile}\"");
                concatLines.Add($"file 'silence_{i}.mp3'");
                currentPlayhead += silenceNeeded;
            }

            // Slice CapCut chunk
            string chunkFile = Path.Combine(outputDir, $"vo_chunk_{i}.mp3");
            if (mappedSource != null)
            {
                await RunFfmpegAsync($"-i \"{capCutVoMp3Path}\" -ss {capCutStartOffset.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -t {duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -b:a 192k \"{chunkFile}\"");
            }
            else
            {
                // Fallback if total match failure: just make silence of that duration
                await RunFfmpegAsync($"-f lavfi -i anullsrc=r=44100:cl=stereo -t {duration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} -b:a 192k \"{chunkFile}\"");
            }
            concatLines.Add($"file 'vo_chunk_{i}.mp3'");

            // Build new subtitle block for output
            finalAdjustedSubtitles.Add(new SrtEntry
            {
                Index = i + 1,
                StartTime = currentPlayhead,
                EndTime = currentPlayhead + duration,
                Text = appTarget.Text
            });

            currentPlayhead += duration;
        }

        // 4. Concat everything
        File.WriteAllLines(concatListPath, concatLines);
        string finalVoFile = Path.Combine(outputDir, "Adjusted_Final_VO.mp3");
        await RunFfmpegAsync($"-f concat -safe 0 -i \"{Path.GetFileName(concatListPath)}\" -c copy \"{Path.GetFileName(finalVoFile)}\"", outputDir);

        // 5. Write final synced SRT
        string finalSrtFile = Path.Combine(outputDir, "Adjusted_Final_VO.srt");
        WriteSrt(finalAdjustedSubtitles, finalSrtFile);

        _logger.LogInformation($"VO Sync Complete! File: {finalVoFile}");
        return finalVoFile;
    }

    public List<SrtEntry> AlignTimestamps(List<SrtEntry> appSubtitles, List<SrtEntry> capCutSubtitles)
    {
        var aligned = new List<SrtEntry>();
        int capCutSearchStartIndex = 0;

        foreach (var appSub in appSubtitles)
        {
            var bestMatch = FindBestSentenceMatch(appSub.Text, capCutSubtitles, capCutSearchStartIndex, out int endIdxRef);
            
            if (bestMatch != null)
            {
                aligned.Add(bestMatch);
                capCutSearchStartIndex = endIdxRef + 1; // Move window forward
            }
            else
            {
                _logger.LogWarning($"Sentence-level match failed for: '{appSub.Text}'. Falling back to Word-level...");
                var fallbackMatch = FallbackWordLevelMatch(appSub.Text, capCutSubtitles, capCutSearchStartIndex);
                aligned.Add(fallbackMatch); // Can be null if totally unmatchable
            }
        }

        return aligned;
    }

    private SrtEntry? FindBestSentenceMatch(string targetText, List<SrtEntry> sourcePool, int startIndex, out int matchedEndIndex)
    {
        matchedEndIndex = startIndex;
        if (startIndex >= sourcePool.Count) return null;

        // Try to form windows of 1, 2, or 3 CapCut blocks to match this 1 App block
        double bestScore = 0;
        SrtEntry? bestEntry = null;
        int bestEndIdx = startIndex;

        for (int windowSize = 1; windowSize <= 4; windowSize++)
        {
            if (startIndex + windowSize > sourcePool.Count) break;

            var windowBlocks = sourcePool.Skip(startIndex).Take(windowSize).ToList();
            string combinedText = string.Join(" ", windowBlocks.Select(b => b.Text));
            
            double score = FuzzyMatcher.CalculateSimilarity(targetText, combinedText);

            if (score > bestScore)
            {
                bestScore = score;
                bestEndIdx = startIndex + windowSize - 1;
                bestEntry = new SrtEntry
                {
                    StartTime = windowBlocks.First().StartTime,
                    EndTime = windowBlocks.Last().EndTime,
                    Text = combinedText
                };
            }
        }

        // If similarity is high enough (e.g. > 0.7), we accept this sentence-level match
        if (bestScore > 0.7 && bestEntry != null)
        {
            matchedEndIndex = bestEndIdx;
            return bestEntry;
        }

        return null;
    }

    private SrtEntry? FallbackWordLevelMatch(string targetText, List<SrtEntry> sourcePool, int startIndex)
    {
        // Forword search window
        var windowBlocks = sourcePool.Skip(startIndex).Take(10).ToList();
        if (!windowBlocks.Any()) return null;

        // Flatten cap cut to estimated words + timestamp mapping
        var wordMapping = new List<(string Word, TimeSpan MidTime)>();
        foreach (var block in windowBlocks)
        {
            var words = block.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            double msPerChar = block.Duration.TotalMilliseconds / Math.Max(1, block.Text.Length);
            
            TimeSpan currentWordStart = block.StartTime;
            foreach (var word in words)
            {
                TimeSpan wordDuration = TimeSpan.FromMilliseconds(word.Length * msPerChar);
                TimeSpan midTime = currentWordStart + TimeSpan.FromMilliseconds(wordDuration.TotalMilliseconds / 2.0);
                wordMapping.Add((word, midTime));
                currentWordStart += wordDuration + TimeSpan.FromMilliseconds(msPerChar); // space
            }
        }

        var targetWords = targetText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (targetWords.Length == 0) return null;

        string firstWord = targetWords.First();
        string lastWord = targetWords.Last();

        // Very basic find indices
        int firstMatchIdx = wordMapping.FindIndex(w => FuzzyMatcher.CalculateSimilarity(firstWord, w.Word) > 0.8);
        int lastMatchIdx = wordMapping.FindLastIndex(w => FuzzyMatcher.CalculateSimilarity(lastWord, w.Word) > 0.8);

        if (firstMatchIdx != -1 && lastMatchIdx != -1 && lastMatchIdx >= firstMatchIdx)
        {
            // Pad 200ms around the mid points to be safe
            return new SrtEntry
            {
                StartTime = wordMapping[firstMatchIdx].MidTime - TimeSpan.FromMilliseconds(200),
                EndTime = wordMapping[lastMatchIdx].MidTime + TimeSpan.FromMilliseconds(200),
                Text = targetText
            };
        }

        return null;
    }

    private List<SrtEntry> ParseSrt(string filePath)
    {
        var items = new List<SrtEntry>();
        if (!File.Exists(filePath)) return items;

        var lines = File.ReadAllLines(filePath);
        SrtEntry? currentItem = null;
        
        foreach (var line in lines)
        {
            if (int.TryParse(line.Trim(), out int index) && currentItem == null)
            {
                currentItem = new SrtEntry { Index = index, Text = "" };
            }
            else if (line.Contains("-->") && currentItem != null)
            {
                var times = line.Split(new[] { " --> " }, StringSplitOptions.None);
                currentItem.StartTime = TimeSpan.ParseExact(times[0].Trim().Replace(',', '.'), @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
                currentItem.EndTime = TimeSpan.ParseExact(times[1].Trim().Replace(',', '.'), @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            }
            else if (string.IsNullOrWhiteSpace(line) && currentItem != null)
            {
                currentItem.Text = currentItem.Text.Trim();
                items.Add(currentItem);
                currentItem = null;
            }
            else if (currentItem != null)
            {
                currentItem.Text += line + " ";
            }
        }
        if (currentItem != null && !string.IsNullOrWhiteSpace(currentItem.Text)) 
        {
            items.Add(currentItem);
        }
        
        return items;
    }

    private void WriteSrt(List<SrtEntry> items, string filePath)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.AppendLine(item.Index.ToString());
            sb.AppendLine($"{item.StartTime.ToString(@"hh\:mm\:ss\,fff")} --> {item.EndTime.ToString(@"hh\:mm\:ss\,fff")}");
            sb.AppendLine(item.Text);
            sb.AppendLine();
        }
        File.WriteAllText(filePath, sb.ToString());
    }

    private async Task RunFfmpegAsync(string arguments, string workingDirectory = null!)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y {arguments}", 
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? string.Empty
            }
        };

        process.Start();
        
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
             _logger.LogError($"FFmpeg Error: {error}");
             throw new Exception($"FFmpeg failed: {error}");
        }
    }
}
