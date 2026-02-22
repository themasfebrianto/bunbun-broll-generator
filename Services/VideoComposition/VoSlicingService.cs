using BunbunBroll.Models;
using System.Globalization;
using System.Text;

namespace BunbunBroll.Services;

public interface IVoSlicingService
{
    Task<VoSliceResult> SliceVoAsync(string voPath, List<SrtEntry> expandedEntries, string outputDirectory);
    Task<VoSliceValidationResult> ValidateSlicedSegmentsAsync(List<VoSegment> segments, List<SrtEntry> expandedEntries);
    Task<double> GetAudioDurationAsync(string audioPath);
    Task<string> StitchVoAsync(List<VoSegment> segments, Dictionary<int, double> pauseDurations, string outputDirectory);
}

public class VoSlicingService : IVoSlicingService
{
    private readonly ILogger<VoSlicingService> _logger;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;

    public VoSlicingService(ILogger<VoSlicingService> logger, IConfiguration config)
    {
        _logger = logger;
        
        var ffmpegDir = Path.GetFullPath(config["FFmpeg:BinaryDirectory"] 
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg-binaries"));
            
        var isWindows = OperatingSystem.IsWindows();
        
        _ffmpegPath = Path.Combine(ffmpegDir, isWindows ? "ffmpeg.exe" : "ffmpeg");
        if (!File.Exists(_ffmpegPath)) _ffmpegPath = "ffmpeg";
        
        _ffprobePath = Path.Combine(ffmpegDir, isWindows ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(_ffprobePath)) _ffprobePath = "ffprobe";
    }

    private async Task<(bool IsSuccess, string Output, string Error)> RunCommandAsync(string fileName, string arguments)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return (process.ExitCode == 0, await outputTask, await errorTask);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    public async Task<VoSliceResult> SliceVoAsync(string voPath, List<SrtEntry> expandedEntries, string outputDirectory)
    {
        var result = new VoSliceResult
        {
            SourceVoPath = voPath,
            OutputDirectory = outputDirectory
        };

        try
        {
            if (!File.Exists(voPath))
            {
                result.Errors.Add($"VO file not found: {voPath}");
                return result;
            }

            // Clean old segments and recreate directory
            var segmentsDir = Path.Combine(outputDirectory, "vo_segments");
            if (Directory.Exists(segmentsDir))
            {
                try { Directory.Delete(segmentsDir, true); } catch { }
            }
            Directory.CreateDirectory(segmentsDir);

            // Delete old stitched VO
            var oldStitched = Path.Combine(outputDirectory, "stitched_vo.mp3");
            if (File.Exists(oldStitched))
            {
                try { File.Delete(oldStitched); } catch { }
            }

            result.Segments = new List<VoSegment>();

            // Get source VO duration
            var sourceDuration = await GetAudioDurationAsync(voPath);
            result.SourceDurationSeconds = sourceDuration;
            _logger.LogInformation("Source VO duration: {Duration}s", sourceDuration);

            // Determine concurrency limit (leave 2 threads for UI/OS if possible, min 1)
            int maxConcurrency = Math.Max(1, Environment.ProcessorCount - 2);
            using var semaphore = new SemaphoreSlim(maxConcurrency);

            var segmentsBag = new System.Collections.Concurrent.ConcurrentBag<VoSegment>();
            var localWarnings = new System.Collections.Concurrent.ConcurrentBag<string>();
            var localErrors = new System.Collections.Concurrent.ConcurrentBag<string>();

            var slicingTasks = new List<Task>();

            // Slice VO for each expanded SRT entry using its original, un-shifted timestamp from CapCut
            for (int i = 0; i < expandedEntries.Count; i++)
            {
                var entry = expandedEntries[i];
                var index = i; // capture loop variable

                slicingTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var startTime = entry.OriginalStartTime.TotalSeconds;
                        var endTime = entry.OriginalEndTime.TotalSeconds;

                        startTime -= entry.PaddingStart.TotalSeconds;
                        endTime += entry.PaddingEnd.TotalSeconds;

                        var duration = endTime - startTime;

                        // Validate timing is within source VO
                        if (endTime > sourceDuration)
                        {
                            localWarnings.Add($"Entry {index + 1} ends at {endTime}s but VO is only {sourceDuration}s long");
                            // Adjust to available duration
                            endTime = sourceDuration;
                            duration = endTime - startTime;
                        }

                        var outputPath = Path.Combine(segmentsDir, $"segment_{index + 1:D3}.wav");

                        // Slice to WAV (lossless, no encoder delay), put -ss AFTER -i for sample-accurate output seeking in MP3
                        var ffmpegArgs = string.Format(CultureInfo.InvariantCulture, "-i \"{1}\" -ss {0:F3} -t {2:F3} -c:a pcm_s16le -ar 44100 -ac 2 -y \"{3}\"", startTime, voPath, duration, outputPath);

                        var ffmpegResult = await RunCommandAsync(_ffmpegPath, ffmpegArgs);

                        if (!ffmpegResult.IsSuccess)
                        {
                            localErrors.Add($"Failed to slice segment {index + 1}: {ffmpegResult.Error}");
                            return;
                        }

                        // Get actual duration of sliced segment
                        var actualDuration = await GetAudioDurationAsync(outputPath);
                        var durationDiff = Math.Abs(actualDuration - duration) * 1000; // in ms

                        var segment = new VoSegment
                        {
                            Index = index + 1,
                            AudioPath = outputPath,
                            StartTime = entry.StartTime, // Keep the mutated start time for metadata, but slicing uses OriginalStartTime
                            EndTime = entry.EndTime,
                            DurationSeconds = duration,
                            Text = entry.Text,
                            IsValid = Math.Abs(durationDiff) < 100, // Valid if within 100ms
                            ActualDurationSeconds = actualDuration,
                            DurationDifferenceMs = durationDiff
                        };

                        if (!segment.IsValid)
                        {
                            segment.ValidationError = $"Duration mismatch: expected {duration:F3}s, got {actualDuration:F3}s";
                        }

                        segmentsBag.Add(segment);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(slicingTasks);

            // Reassemble findings
            result.Segments = segmentsBag.OrderBy(s => s.Index).ToList();
            foreach (var w in localWarnings) result.Warnings.Add(w);
            foreach (var e in localErrors) result.Errors.Add(e);

            result.TotalSegments = result.Segments.Count(s => s.IsValid);
            result.TotalDurationSeconds = result.Segments.Sum(s => s.ActualDurationSeconds);
            result.IsSuccess = result.Segments.Count > 0;

            _logger.LogInformation("VO slicing complete: {Valid}/{Total} valid segments",
                result.TotalSegments, expandedEntries.Count);

            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Slicing failed: {ex.Message}");
            _logger.LogError(ex, "VO slicing failed");
            return result;
        }
    }

    public async Task<string> StitchVoAsync(List<VoSegment> segments, Dictionary<int, double> pauseDurations, string outputDirectory)
    {
        try
        {
            var segmentsDir = Path.Combine(outputDirectory, "vo_segments");
            
            // 1. Generate silence files for needed pause durations
            var distinctPauses = pauseDurations.Values.Distinct().ToList();
            var silenceFiles = new Dictionary<double, string>();
            
            foreach (var pause in distinctPauses)
            {
                var silencePath = Path.Combine(segmentsDir, $"silence_{pause}s.wav");
                if (!File.Exists(silencePath))
                {
                    var ffmpegArgs = string.Format(CultureInfo.InvariantCulture, "-f lavfi -i anullsrc=r=44100:cl=stereo -t {0:F3} -c:a pcm_s16le -y \"{1}\"", pause, silencePath);
                    await RunCommandAsync(_ffmpegPath, ffmpegArgs);
                }
                silenceFiles[pause] = silencePath;
            }

            // 2. Prepare concatenation list
            var concatListPath = Path.Combine(segmentsDir, "concat_list.txt");
            var sb = new StringBuilder();
            double runningDuration = 0;
            
            for (int i = 0; i < segments.Count; i++)
            {
                // Add head silence if defined (index -1)
                if (i == 0 && pauseDurations.TryGetValue(-1, out double headPause) && headPause > 0)
                {
                    var normalizedSilencePath = silenceFiles[headPause].Replace("\\", "/");
                    sb.Append($"file '{normalizedSilencePath}'\n");
                    runningDuration += headPause;
                }

                // Add the spoken segment
                // CRITICAL: FFmpeg's concat demuxer reads this text file and interprets `\` as an escape character. 
                // We MUST replace `\` with `/` to prevent paths like `segment_001.wav` from having `\s` swallowed.
                if (File.Exists(segments[i].AudioPath))
                {
                    var normalizedSegPath = segments[i].AudioPath.Replace("\\", "/");
                    sb.Append($"file '{normalizedSegPath}'\n");
                    runningDuration += segments[i].ActualDurationSeconds;
                }
                else
                {
                    _logger.LogWarning("Sequence {Index} skipped in concatenation because file is missing: {Path}", 
                        segments[i].Index, segments[i].AudioPath);
                }
                
                // Add silence if there's a pause defined
                if (pauseDurations.TryGetValue(i, out double pause) && pause > 0)
                {
                    var normalizedSilencePath = silenceFiles[pause].Replace("\\", "/");
                    sb.Append($"file '{normalizedSilencePath}'\n");
                    runningDuration += pause;
                }
            }
            
            await File.WriteAllTextAsync(concatListPath, sb.ToString());

            // 3. Concatenate using FFmpeg demuxer
            // Re-encode WAV segments into final MP3 (single encode = no cumulative drift)
            var stitchedPath = Path.Combine(outputDirectory, "stitched_vo.mp3");
            var concatArgs = $"-f concat -safe 0 -i \"{concatListPath}\" -c:a libmp3lame -q:a 2 -y \"{stitchedPath}\"";
            
            var result = await RunCommandAsync(_ffmpegPath, concatArgs);
            
            if (result.IsSuccess && File.Exists(stitchedPath))
            {
                return stitchedPath;
            }
            
            _logger.LogError("Stitching failed: {Error}", result.Error);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stitch VO");
            return string.Empty;
        }
    }


    public async Task<VoSliceValidationResult> ValidateSlicedSegmentsAsync(List<VoSegment> segments, List<SrtEntry> expandedEntries)
    {
        var result = new VoSliceValidationResult
        {
            IsValid = true,
            ValidSegments = 0,
            InvalidSegments = 0,
            WarningSegments = 0
        };

        try
        {
            for (int i = 0; i < segments.Count && i < expandedEntries.Count; i++)
            {
                var segment = segments[i];
                var entry = expandedEntries[i];

                // Since we added padding during slicing, the expected actual length of the WAV
                // is NOT entry.Duration.TotalSeconds, but rather the padded duration.
                // We should compare the actual sliced length against `segment.DurationSeconds` 
                // which contains the exact slice target duration from ffmpeg.
                var expectedDuration = segment.DurationSeconds;
                var actualDuration = segment.ActualDurationSeconds;
                var diffMs = Math.Abs(expectedDuration - actualDuration) * 1000;
                var diffPercent = (diffMs / expectedDuration) * 100;

                // Check validation rules
                if (diffMs > 200) // More than 200ms difference = error
                {
                    result.Issues.Add(new VoSegmentValidationIssue
                    {
                        SegmentIndex = i,
                        Text = entry.Text,
                        Issue = $"Duration mismatch: {diffMs:F0}ms ({diffPercent:F1}%)",
                        Severity = "Error"
                    });
                    result.InvalidSegments++;
                    segment.IsValid = false;
                }
                else if (diffMs > 100) // More than 100ms = warning
                {
                    result.Issues.Add(new VoSegmentValidationIssue
                    {
                        SegmentIndex = i,
                        Text = entry.Text,
                        Issue = $"Duration drift: {diffMs:F0}ms ({diffPercent:F1}%)",
                        Severity = "Warning"
                    });
                    result.WarningSegments++;
                }
                else
                {
                    result.ValidSegments++;
                    segment.IsValid = true;
                }

                // Track mismatches
                if (diffMs > 50)
                {
                    result.Mismatches.Add(new SegmentMismatch
                    {
                        SegmentIndex = i,
                        Text = entry.Text,
                        ExpectedDuration = expectedDuration,
                        ActualDuration = actualDuration,
                        DifferenceMs = diffMs,
                        DifferencePercent = diffPercent
                    });
                }
            }

            // Calculate accuracy score
            var totalSegments = segments.Count;
            if (totalSegments > 0)
            {
                result.AccuracyScore = ((double)result.ValidSegments / totalSegments) * 100
                    - (result.InvalidSegments * 5)
                    - (result.WarningSegments * 2);
                result.AccuracyScore = Math.Max(0, Math.Min(100, result.AccuracyScore));
            }

            result.IsValid = result.InvalidSegments == 0 && result.AccuracyScore >= 90;

            _logger.LogInformation("Validation: Score={Score}%, Valid={Valid}, Invalid={Invalid}, Warnings={Warn}",
                result.AccuracyScore, result.ValidSegments, result.InvalidSegments, result.WarningSegments);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed");
            result.IsValid = false;
            return result;
        }
    }

    public async Task<double> GetAudioDurationAsync(string audioPath)
    {
        try
        {
            // Use FFprobe to get duration
            var ffprobeArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"";
            var result = await RunCommandAsync(_ffprobePath, ffprobeArgs);

            if (double.TryParse(result.Output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
            {
                return duration;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audio duration: {Path}", audioPath);
            return 0;
        }
    }
}
