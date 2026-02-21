using BunbunBroll.Models;
using Xabe.FFmpeg;
using System.Diagnostics;

namespace BunbunBroll.Services;

/// <summary>
/// VideoComposer partial: Clip concatenation and transition methods.
/// </summary>
public partial class VideoComposer
{
    /// <summary>
    /// Concatenate clips with transitions using FFmpeg xfade filter.
    /// </summary>
    private async Task ConcatenateClipsWithTransitionsAsync(
        List<string> clipPaths,
        string outputPath,
        VideoConfig config,
        CancellationToken cancellationToken)
    {
        if (clipPaths.Count == 0)
        {
            _logger.LogWarning("No clips to concatenate");
            return;
        }

        // If only one clip, just copy it
        if (clipPaths.Count == 1)
        {
            _logger.LogInformation("Only one clip, copying directly to output");
            File.Copy(clipPaths[0], outputPath, overwrite: true);
            return;
        }

        // If transitions disabled or Cut type, use simple concatenation
        if (!config.AddTransitions || config.Transition == TransitionType.Cut)
        {
            await ConcatenateClipsAsync(clipPaths, outputPath, cancellationToken);
            return;
        }

        try
        {
            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg executable not found");
            }

            var absoluteOutputPath = Path.GetFullPath(outputPath);
            var transitionName = config.Transition.GetFFmpegName();
            var transitionDurationStr = config.TransitionDuration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

            // Get durations of all clips and check for audio streams
            var clipDurations = new List<double>();
            var hasAudioStreams = new List<bool>();
            
            foreach (var clipPath in clipPaths)
            {
                var duration = await GetVideoDurationAsync(clipPath, cancellationToken);
                clipDurations.Add(duration);
                
                // Check if clip has audio stream
                var hasAudio = await CheckHasAudioStreamAsync(clipPath, cancellationToken);
                hasAudioStreams.Add(hasAudio);
            }

            // If any clip is missing audio, use video-only transitions
            var allHaveAudio = hasAudioStreams.All(h => h);
            
            _logger.LogInformation("Transition processing: {Count} clips, all have audio: {HasAudio}", 
                clipPaths.Count, allHaveAudio);

            // Build xfade filter chain for multiple clips
            var filterParts = new List<string>();
            var inputArgs = new List<string>();
            
            // Add all input files
            for (int i = 0; i < clipPaths.Count; i++)
            {
                var escapedPath = clipPaths[i].Replace("\\", "/");
                inputArgs.Add($"-i \"{escapedPath}\"");
            }

            // Calculate offsets and build xfade chain
            // FIXED: Properly accumulate offset based on effective duration after overlap
            double cumulativeOffset = 0;
            string lastVideoLabel = "[0:v]";
            
            for (int i = 1; i < clipPaths.Count; i++)
            {
                var prevDuration = clipDurations[i - 1];
                
                // For the first clip, offset = duration - transition
                // For subsequent clips, we need to account for the overlap
                double offset;
                if (i == 1)
                {
                    offset = prevDuration - config.TransitionDuration;
                }
                else
                {
                    // Each previous clip's effective contribution is: duration - transitionDuration
                    // So cumulative offset = sum of (duration_k - transitionDuration) for k = 0 to i-1
                    cumulativeOffset += (clipDurations[i - 1] - config.TransitionDuration);
                    offset = cumulativeOffset;
                }
                
                if (i == 1) cumulativeOffset = offset;
                
                var offsetStr = Math.Max(0, offset).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                
                var outputVideoLabel = i == clipPaths.Count - 1 ? "[vout]" : $"[v{i}]";
                
                // Video transition using xfade
                filterParts.Add($"{lastVideoLabel}[{i}:v]xfade=transition={transitionName}:duration={transitionDurationStr}:offset={offsetStr}{outputVideoLabel}");
                
                lastVideoLabel = outputVideoLabel;
            }

            // Build audio filter - handle cases where clips may not have audio
            string audioFilter;
            if (allHaveAudio)
            {
                // All clips have audio - use acrossfade chain
                var audioFilterParts = new List<string>();
                string lastAudioLabel = "[0:a]";
                cumulativeOffset = 0;
                
                for (int i = 1; i < clipPaths.Count; i++)
                {
                    var prevDuration = clipDurations[i - 1];
                    
                    double offset;
                    if (i == 1)
                    {
                        offset = prevDuration - config.TransitionDuration;
                    }
                    else
                    {
                        cumulativeOffset += (clipDurations[i - 1] - config.TransitionDuration);
                        offset = cumulativeOffset;
                    }
                    if (i == 1) cumulativeOffset = offset;
                    
                    var outputAudioLabel = i == clipPaths.Count - 1 ? "[aout]" : $"[a{i}]";
                    
                    audioFilterParts.Add($"{lastAudioLabel}[{i}:a]acrossfade=d={transitionDurationStr}:c1=tri:c2=tri{outputAudioLabel}");
                    
                    lastAudioLabel = outputAudioLabel;
                }
                
                audioFilter = string.Join(";", audioFilterParts);
            }
            else
            {
                // Some clips missing audio - generate silent audio for the final output
                // Calculate total duration accounting for overlaps
                double totalDuration = clipDurations[0];
                for (int i = 1; i < clipDurations.Count; i++)
                {
                    totalDuration += clipDurations[i] - config.TransitionDuration;
                }
                
                var totalDurationStr = totalDuration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                audioFilter = $"anullsrc=channel_layout=stereo:sample_rate=44100,atrim=0:{totalDurationStr}[aout]";
                
                _logger.LogInformation("Some clips missing audio, generating silent audio track ({Duration}s)", totalDuration);
            }

            var filterComplex = string.Join(";", filterParts) + ";" + audioFilter;
            
            _logger.LogInformation("Concatenating {Count} clips with {Transition} transition ({Duration}s)", 
                clipPaths.Count, config.Transition.GetDisplayName(), config.TransitionDuration);
            _logger.LogDebug("Transition filter: {Filter}", filterComplex);

            var arguments = $"-threads 0 {string.Join(" ", inputArgs)} " +
                           $"-filter_complex \"{filterComplex}\" " +
                           $"-map \"[vout]\" -map \"[aout]\" " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} -threads 0 " +
                           $"-c:a aac -b:a 128k " +
                           $"-movflags +faststart " +
                           $"-y \"{absoluteOutputPath}\"";

            _logger.LogDebug("FFmpeg transition command: {Cmd}", arguments);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            // Read output streams asynchronously to prevent deadlock
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            
            await process.WaitForExitAsync(cancellationToken);
            
            var stderr = await stderrTask;
            var stdout = await stdoutTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg xfade with audio failed (Exit: {Code}). Trying video-only transition...", 
                    process.ExitCode);
                _logger.LogDebug("FFmpeg error: {Error}", stderr.Length > 1000 ? stderr[..1000] : stderr);
                
                // Try again with video-only transitions (no audio crossfade)
                var videoOnlySuccess = await TryVideoOnlyTransitionsAsync(
                    ffmpegPath, clipPaths, clipDurations, absoluteOutputPath, 
                    transitionName, config.TransitionDuration, cancellationToken);
                
                if (videoOnlySuccess)
                {
                    return;
                }
                
                _logger.LogWarning("Video-only transitions also failed. Falling back to simple concatenation.");
                await ConcatenateClipsAsync(clipPaths, outputPath, cancellationToken);
                return;
            }

            if (!File.Exists(absoluteOutputPath))
            {
                throw new InvalidOperationException("Output file was not created after transition concatenation");
            }

            var fileInfo = new FileInfo(absoluteOutputPath);
            _logger.LogInformation("Successfully concatenated {Count} clips with transitions to: {Path} ({Size} bytes)", 
                clipPaths.Count, absoluteOutputPath, fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to concatenate clips with transitions, falling back to simple concat");
            await ConcatenateClipsAsync(clipPaths, outputPath, cancellationToken);
        }
    }

    /// <summary>
    /// Check if a video file has an audio stream.
    /// </summary>
    private async Task<bool> CheckHasAudioStreamAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(videoPath, cancellationToken);
            return mediaInfo.AudioStreams.Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check audio stream for {Path}, assuming no audio", videoPath);
            return false;
        }
    }

    /// <summary>
    /// Try to apply video-only transitions (without audio crossfade).
    /// </summary>
    private async Task<bool> TryVideoOnlyTransitionsAsync(
        string ffmpegPath,
        List<string> clipPaths,
        List<double> clipDurations,
        string outputPath,
        string transitionName,
        double transitionDuration,
        CancellationToken cancellationToken)
    {
        try
        {
            var transitionDurationStr = transitionDuration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            
            var filterParts = new List<string>();
            var inputArgs = new List<string>();
            
            for (int i = 0; i < clipPaths.Count; i++)
            {
                var escapedPath = clipPaths[i].Replace("\\", "/");
                inputArgs.Add($"-i \"{escapedPath}\"");
            }

            // Video xfade chain
            double cumulativeOffset = 0;
            string lastVideoLabel = "[0:v]";
            
            for (int i = 1; i < clipPaths.Count; i++)
            {
                var prevDuration = clipDurations[i - 1];
                
                double offset;
                if (i == 1)
                {
                    offset = prevDuration - transitionDuration;
                }
                else
                {
                    cumulativeOffset += (clipDurations[i - 1] - transitionDuration);
                    offset = cumulativeOffset;
                }
                if (i == 1) cumulativeOffset = offset;
                
                var offsetStr = Math.Max(0, offset).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                var outputVideoLabel = i == clipPaths.Count - 1 ? "[vout]" : $"[v{i}]";
                
                filterParts.Add($"{lastVideoLabel}[{i}:v]xfade=transition={transitionName}:duration={transitionDurationStr}:offset={offsetStr}{outputVideoLabel}");
                lastVideoLabel = outputVideoLabel;
            }

            // Generate silent audio for the total duration
            double totalDuration = clipDurations[0];
            for (int i = 1; i < clipDurations.Count; i++)
            {
                totalDuration += clipDurations[i] - transitionDuration;
            }
            var totalDurationStr = totalDuration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            filterParts.Add($"anullsrc=channel_layout=stereo:sample_rate=44100,atrim=0:{totalDurationStr}[aout]");

            var filterComplex = string.Join(";", filterParts);
            
            var arguments = $"{string.Join(" ", inputArgs)} " +
                           $"-filter_complex \"{filterComplex}\" " +
                           $"-map \"[vout]\" -map \"[aout]\" " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} " +
                           $"-c:a aac -b:a 128k " +
                           $"-movflags +faststart " +
                           $"-y \"{outputPath}\"";

            _logger.LogDebug("FFmpeg video-only transition command: {Cmd}", arguments);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                _logger.LogInformation("Successfully created video with video-only transitions: {Path} ({Size} bytes)", 
                    outputPath, fileInfo.Length);
                return true;
            }

            _logger.LogWarning("Video-only transition failed. Exit: {Code}", process.ExitCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video-only transition attempt failed");
            return false;
        }
    }

    private async Task ConcatenateClipsAsync(
        List<string> clipPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (clipPaths.Count == 0)
        {
            _logger.LogWarning("No clips to concatenate");
            return;
        }

        // If only one clip, just copy it
        if (clipPaths.Count == 1)
        {
            _logger.LogInformation("Only one clip, copying directly to output");
            File.Copy(clipPaths[0], outputPath, overwrite: true);
            return;
        }

        try
        {
            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg executable not found");
            }

            // Create concat file list with absolute paths
            var concatListPath = Path.Combine(_tempDirectory, $"concat_{Guid.NewGuid():N}.txt");
            
            // Ensure all paths are absolute and escaped for FFmpeg
            var absolutePaths = clipPaths.Select(p => Path.GetFullPath(p)).ToList();
            
            // Create concat content with properly escaped paths (use forward slashes for FFmpeg)
            var concatContent = string.Join("\n", absolutePaths.Select(p => 
                $"file '{p.Replace("\\", "/").Replace("'", "'\\''")}'"));
            
            _logger.LogInformation("Concatenating {Count} clips", clipPaths.Count);
            _logger.LogDebug("Concat file content:\n{Content}", concatContent);
            
            await File.WriteAllTextAsync(concatListPath, concatContent, cancellationToken);

            // Use concat demuxer with re-encoding for reliability
            var absoluteOutputPath = Path.GetFullPath(outputPath);
            
            // FFmpeg command: concat with re-encoding to ensure compatibility
            var arguments = $"-f concat -safe 0 -i \"{concatListPath}\" " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} " +
                           $"-c:a aac -b:a 128k " +
                           $"-movflags +faststart " +
                           $"-y \"{absoluteOutputPath}\"";

            _logger.LogDebug("FFmpeg concat command: {Cmd}", $"{ffmpegPath} {arguments}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            // Cleanup concat list
            if (File.Exists(concatListPath))
            {
                File.Delete(concatListPath);
            }

            if (process.ExitCode != 0)
            {
                _logger.LogError("FFmpeg concat failed. Exit code: {Code}. Error: {Error}", 
                    process.ExitCode, stderr);
                throw new InvalidOperationException($"FFmpeg concatenation failed: {stderr}");
            }

            if (!File.Exists(absoluteOutputPath))
            {
                throw new InvalidOperationException("Output file was not created after concatenation");
            }

            var fileInfo = new FileInfo(absoluteOutputPath);
            _logger.LogInformation("Successfully concatenated {Count} clips to: {Path} ({Size} bytes)", 
                clipPaths.Count, absoluteOutputPath, fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to concatenate clips");
            throw;
        }
    }
}
