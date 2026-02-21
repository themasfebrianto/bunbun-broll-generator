using BunbunBroll.Models;
using Xabe.FFmpeg;
using System.Diagnostics;

namespace BunbunBroll.Services;

/// <summary>
/// VideoComposer partial: Main composition pipeline, clip downloading, processing, and cleanup.
/// </summary>
public partial class VideoComposer
{
    public async Task<VideoResult> ComposeAsync(
        List<VideoClip> clips,
        VideoConfig config,
        string? sessionId = null,
        IProgress<CompositionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (clips.Count == 0)
        {
            return new VideoResult
            {
                Success = false,
                ErrorMessage = "No clips provided for composition"
            };
        }

        var composeSessionId = sessionId ?? Guid.NewGuid().ToString("N")[..8];
        var outputFileName = $"short_{composeSessionId}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
        // Use session-scoped output directory: output/<sessionId>/shorts
        var sessionOutputDir = Path.Combine(_outputDirectory, composeSessionId, "shorts");
        Directory.CreateDirectory(sessionOutputDir);
        var outputPath = Path.Combine(sessionOutputDir, outputFileName);

        try
        {
            // Step 1: Ensure FFmpeg is available
            progress?.Report(new CompositionProgress { Stage = "Initializing", Percent = 5, Message = "Checking FFmpeg..." });
            
            if (!await EnsureFFmpegAsync())
            {
                return new VideoResult
                {
                    Success = false,
                    ErrorMessage = "FFmpeg not available. Please install FFmpeg from https://ffmpeg.org/download.html"
                };
            }

            // Step 2: Download clips to temp directory if they are URLs
            progress?.Report(new CompositionProgress { Stage = "Downloading", Percent = 10, Message = "Downloading clips..." });
            var localClips = await DownloadClipsAsync(clips, progress, cancellationToken);

            if (localClips.Count == 0)
            {
                return new VideoResult
                {
                    Success = false,
                    ErrorMessage = "Failed to download video clips"
                };
            }

            // Step 3: Calculate clip durations
            progress?.Report(new CompositionProgress { Stage = "Preparing", Percent = 30, Message = "Calculating durations..." });
            var durationResult = CalculateClipDurations(localClips, config);
            var clipDurations = durationResult.Durations;
            bool isSrtSynced = durationResult.IsSrtSynced;

            // Step 4: Process clips in PARALLEL for speedup
            progress?.Report(new CompositionProgress { Stage = "Processing", Percent = 40, Message = $"Processing {localClips.Count} clips (parallel x{_parallelClips})..." });
            
            // Prepare clip processing tasks with session-scoped temp directory
            var sessionTempDir = Path.Combine(_tempDirectory, composeSessionId);
            Directory.CreateDirectory(sessionTempDir);
            var clipTasks = localClips.Zip(clipDurations).Select((pair, index) => new
            {
                Clip = pair.First,
                Duration = pair.Second.Duration,
                Index = index,
                OutputPath = Path.Combine(sessionTempDir, $"clip_{composeSessionId}_{index}.mp4")
            }).ToList();

            var processedClips = new string?[clipTasks.Count];
            var processedCount = 0;

            // Process clips in parallel with limited concurrency
            await Parallel.ForEachAsync(
                clipTasks,
                new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = _parallelClips,
                    CancellationToken = cancellationToken 
                },
                async (task, ct) =>
                {
                    string? inputPath = task.Clip.LocalPath;
                    string? tempImageVideo = null;

                    // Support image-to-video with Ken Burns if it's an image
                if (task.Clip.Original.IsImage)
                {
                    // If Draft Preview, skip complex KenBurns and just create a static video
                    if (config.IsDraftPreview)
                    {
                        tempImageVideo = Path.Combine(sessionTempDir, $"static_img_{composeSessionId}_{task.Index}.mp4");
                        var imgPath = string.IsNullOrEmpty(task.Clip.Original.ImagePath) ? task.Clip.LocalPath : task.Clip.Original.ImagePath;
                        var ffmpegPathStatic = await FindFFmpegExecutablePathAsync();
                        
                        // -loop 1 inputs the image infinitely, -t limits it. High speed encoding.
                        var staticArgs = $"-loop 1 -i \"{imgPath}\" -t {task.Duration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} -c:v libx264 -preset ultrafast -crf 35 -pix_fmt yuv420p -s 480x854 -y \"{tempImageVideo}\"";
                        var staticProcess = new Process { StartInfo = new ProcessStartInfo { FileName = ffmpegPathStatic, Arguments = staticArgs, UseShellExecute = false, CreateNoWindow = true } };
                        staticProcess.Start();
                        await staticProcess.WaitForExitAsync(ct);
                    }
                    else
                    {
                        tempImageVideo = await ConvertImageToVideoAsync(
                            task.Clip.Original.ImagePath,
                            task.Duration,
                            config,
                            task.Clip.Original.MotionType,
                            ct
                        );
                    }

                    if (string.IsNullOrEmpty(tempImageVideo) || !File.Exists(tempImageVideo))
                    {
                        _logger.LogWarning("Failed to convert image to video: {Path}", task.Clip.Original.ImagePath);
                        return;
                    }
                    inputPath = tempImageVideo;
                }
                    if (!string.IsNullOrEmpty(inputPath))
                    {
                        await ProcessSingleClipAsync(
                            inputPath,
                            task.OutputPath,
                            task.Duration,
                            config,
                            ct,
                            task.Clip.Original.IsImage,                             // Pass source type
                            task.Clip.Original.Style ?? VideoStyle.None,            // Pass per-clip style
                            task.Clip.Original.Filter,                              // Pass per-clip filter
                            task.Clip.Original.FilterIntensity,                     // Pass per-clip filter intensity
                            task.Clip.Original.Texture,                             // Pass per-clip texture
                            task.Clip.Original.TextureOpacity                       // Pass per-clip texture opacity
                        );

                        // Apply text overlay if present
                        if (File.Exists(task.OutputPath) && task.Clip.Original.HasTextOverlay)
                        {
                            var overlaidPath = await AddTextOverlayToVideoAsync(
                                task.OutputPath, task.Clip.Original.TextOverlay!, config, ct);
                            if (!string.IsNullOrEmpty(overlaidPath) && File.Exists(overlaidPath))
                            {
                                try { File.Delete(task.OutputPath); } catch { }
                                File.Move(overlaidPath, task.OutputPath);
                            }
                        }

                        if (File.Exists(task.OutputPath))
                        {
                            processedClips[task.Index] = task.OutputPath;
                        }
                    }

                    // Cleanup the temporary image-video if created
                    if (!string.IsNullOrEmpty(tempImageVideo) && File.Exists(tempImageVideo))
                    {
                        try { File.Delete(tempImageVideo); } catch { }
                    }

                    var processed = Interlocked.Increment(ref processedCount);
                    var percent = 40 + (int)(processed * 30.0 / clipTasks.Count);
                    progress?.Report(new CompositionProgress
                    {
                        Stage = "Processing",
                        Percent = percent,
                        Message = $"Processed clip {processed}/{clipTasks.Count}..."
                    });
                }
            );

            // Filter out nulls and maintain order
            var orderedClips = processedClips.Where(p => p != null).Select(p => p!).ToList();

            if (orderedClips.Count == 0)
            {
                return new VideoResult
                {
                    Success = false,
                    ErrorMessage = "No clips were successfully processed"
                };
            }

            // Step 5: Concatenate all processed clips with transitions
            progress?.Report(new CompositionProgress { Stage = "Concatenating", Percent = 75, Message = "Joining clips with transitions..." });
            await ConcatenateClipsWithTransitionsAsync(orderedClips, outputPath, config, cancellationToken);

            // Step 5.5: Voiceover Sync
            if (!string.IsNullOrEmpty(config.CapCutAudioPath) && File.Exists(config.CapCutAudioPath))
            {
                progress?.Report(new CompositionProgress { Stage = "Audio Sync", Percent = 85, Message = "Syncing Voiceover to Timeline..." });
                string finalOutputPath = Path.Combine(sessionOutputDir, $"final_vo_{composeSessionId}.mp4");
                string ffmpegPath = await FindFFmpegExecutablePathAsync();
                
                if (ffmpegPath != null)
                {
                    string targetVoPath = config.CapCutAudioPath;
                    
                    if (!isSrtSynced && !string.IsNullOrEmpty(config.CapCutSrtPath) && File.Exists(config.CapCutSrtPath))
                    {
                        // Fallback: Create app truth SRT and use VoSyncService to stretch audio
                        string truthSrtPath = Path.Combine(sessionOutputDir, "app_truth.srt");
                        var truthEntries = new List<BunbunBroll.Models.SrtEntry>();
                        TimeSpan current = TimeSpan.Zero;
                        for (int i = 0; i < localClips.Count; i++)
                        {
                            if (string.IsNullOrEmpty(localClips[i].Original.AssociatedText)) continue;
                            var dur = clipDurations[i].Duration;
                            truthEntries.Add(new BunbunBroll.Models.SrtEntry
                            {
                                Index = truthEntries.Count + 1,
                                StartTime = current,
                                EndTime = current + TimeSpan.FromSeconds(dur),
                                Text = localClips[i].Original.AssociatedText
                            });
                            current += TimeSpan.FromSeconds(dur);
                        }
                        
                        var sb = new System.Text.StringBuilder();
                        foreach (var item in truthEntries)
                        {
                            sb.AppendLine(item.Index.ToString());
                            sb.AppendLine($"{item.StartTime.ToString("hh\\:mm\\:ss\\,fff")} --> {item.EndTime.ToString("hh\\:mm\\:ss\\,fff")}");
                            sb.AppendLine(item.Text);
                            sb.AppendLine();
                        }
                        File.WriteAllText(truthSrtPath, sb.ToString());
                        
                        targetVoPath = await _voSyncService.SyncVoiceoverToAppTimeline(
                            config.CapCutAudioPath, 
                            config.CapCutSrtPath, 
                            truthSrtPath, 
                            sessionOutputDir);
                    }
                    
                    // Merge voiceover into the final concatenated video
                    string args = $"-i \"{outputPath}\" -i \"{targetVoPath}\" -c:v copy -c:a aac -map 0:v:0 -map 1:a:0 -shortest -y \"{finalOutputPath}\"";
                    var process = new Process { StartInfo = new ProcessStartInfo { FileName = ffmpegPath, Arguments = args, UseShellExecute = false, CreateNoWindow = true } };
                    process.Start();
                    await process.WaitForExitAsync(cancellationToken);
                    
                    if (File.Exists(finalOutputPath))
                    {
                        try { File.Delete(outputPath); } catch { }
                        outputPath = finalOutputPath;
                    }
                }
            }

            // Step 6: Cleanup temp files
            progress?.Report(new CompositionProgress { Stage = "Cleanup", Percent = 90, Message = "Cleaning up..." });
            CleanupTempFiles(orderedClips, localClips);

            if (!File.Exists(outputPath))
            {
                return new VideoResult
                {
                    Success = false,
                    ErrorMessage = "Failed to create output video"
                };
            }

            var fileInfo = new FileInfo(outputPath);
            var videoDuration = await GetVideoDurationAsync(outputPath, cancellationToken);

            progress?.Report(new CompositionProgress { Stage = "Complete", Percent = 100, Message = "Video ready!" });

            return new VideoResult
            {
                Success = true,
                OutputPath = outputPath,
                DurationSeconds = (int)videoDuration,
                ClipsUsed = clips.Count,
                FileSizeBytes = fileInfo.Length
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Video composition cancelled");
            return new VideoResult
            {
                Success = false,
                ErrorMessage = "Composition was cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video composition failed");
            return new VideoResult
            {
                Success = false,
                ErrorMessage = $"Composition error: {ex.Message}"
            };
        }
    }

    private async Task<List<(string LocalPath, VideoClip Original)>> DownloadClipsAsync(
        List<VideoClip> clips,
        IProgress<CompositionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new (string? LocalPath, VideoClip Original)?[clips.Count];
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        var downloadedCount = 0;

        progress?.Report(new CompositionProgress
        {
            Stage = "Downloading",
            Percent = 10,
            Message = $"Downloading {clips.Count} clips in parallel..."
        });

        // Download clips in parallel for speed
        await Parallel.ForEachAsync(
            clips.Select((clip, index) => (clip, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _parallelClips,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                var (clip, index) = item;
                try
                {
                    string localPath;

                    // Check if already a local file
                    if (!string.IsNullOrEmpty(clip.SourcePath) && File.Exists(clip.SourcePath))
                    {
                        localPath = clip.SourcePath;
                    }
                    else if (!string.IsNullOrEmpty(clip.SourceUrl))
                    {
                        // Download from URL
                        localPath = Path.Combine(_tempDirectory, $"download_{Guid.NewGuid():N}.mp4");
                        
                        var response = await httpClient.GetAsync(clip.SourceUrl, ct);
                        response.EnsureSuccessStatusCode();
                        
                        var content = await response.Content.ReadAsByteArrayAsync(ct);
                        await File.WriteAllBytesAsync(localPath, content, ct);
                        
                        _logger.LogDebug("Downloaded clip {Index}: {Path} ({Size} bytes)", 
                            index, Path.GetFileName(localPath), content.Length);
                    }
                    else
                    {
                        _logger.LogWarning("Clip {Index} has no source path or URL, skipping", index);
                        return;
                    }

                    results[index] = (localPath, clip);
                    
                    var count = Interlocked.Increment(ref downloadedCount);
                    var percent = 10 + (int)(count * 20.0 / clips.Count);
                    progress?.Report(new CompositionProgress
                    {
                        Stage = "Downloading",
                        Percent = percent,
                        Message = $"Downloaded {count}/{clips.Count} clips..."
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download clip {Index}: {Url}", index, clip.SourceUrl);
                }
            }
        );

        // Filter nulls and maintain order
        return results
            .Where(r => r.HasValue && r.Value.LocalPath != null)
            .Select(r => (r!.Value.LocalPath!, r.Value.Original))
            .ToList();
    }

    private (List<(int ClipIndex, double Start, double Duration)> Durations, bool IsSrtSynced) CalculateClipDurations(
        List<(string LocalPath, VideoClip Original)> clips,
        VideoConfig config)
    {
        // Try reading SRT first 
        if (!string.IsNullOrEmpty(config.CapCutSrtPath) && File.Exists(config.CapCutSrtPath))
        {
            try 
            {
                var srtContent = File.ReadAllText(config.CapCutSrtPath);
                var capCutSubtitles = _srtService.ParseSrt(srtContent);
                
                if (capCutSubtitles.Count > 0)
                {
                    var appSubtitles = clips.Select((c, i) => new BunbunBroll.Models.SrtEntry { Index = i, Text = c.Original.AssociatedText ?? "" }).ToList();
                    var alignedSubtitles = _voSyncService.AlignTimestamps(appSubtitles, capCutSubtitles);
                    
                    var result = new List<(int, double, double)>();
                    TimeSpan timelineEnd = capCutSubtitles.Last().EndTime;
                    
                    bool alignmentSuccess = true;
                    for (int i = 0; i < clips.Count; i++)
                    {
                        if (alignedSubtitles[i] == null || (alignedSubtitles[i].StartTime == TimeSpan.Zero && alignedSubtitles[i].EndTime == TimeSpan.Zero))
                        {
                            _logger.LogWarning($"Visual sync failed mapping text for clip {i}. Falling back to even division.");
                            alignmentSuccess = false;
                            break;
                        }
                    }

                    if (alignmentSuccess && clips.Count > 0) 
                    {
                        for (int i = 0; i < clips.Count; i++)
                        {
                            TimeSpan start = (i == 0) ? TimeSpan.Zero : alignedSubtitles[i].StartTime;
                            TimeSpan end = (i == clips.Count - 1) ? timelineEnd : alignedSubtitles[i + 1].StartTime;
                            
                            double duration = (end - start).TotalSeconds;
                            duration = Math.Max(1.0, duration); // minimum 1s for FFmpeg safety
                            
                            result.Add((i, 0, duration));
                        }
                        
                        _logger.LogInformation("Successfully mapped visual durations perfectly to CapCut VO timeline!");
                        return (result, true);
                    }
                }
            }
            catch(Exception ex)
            {
                _logger.LogWarning(ex, "Failed to align visual clips to CapCut SRT. Falling back to even division.");
            }
        }

        // Even division fallback
        // Reserve time for hook if enabled
        var hookDuration = config.AddTextOverlay && !string.IsNullOrEmpty(config.HookText)
            ? config.HookDurationMs / 1000.0
            : 0;

        var availableTime = config.TargetDurationSeconds - hookDuration;
        var perClipDuration = availableTime / Math.Max(1, clips.Count);

        // Ensure minimum duration per clip (3 seconds)
        perClipDuration = Math.Max(3, perClipDuration);

        var fallbackResult = new List<(int, double, double)>();

        for (int i = 0; i < clips.Count; i++)
        {
            var clip = clips[i].Original;
            var clipDuration = clip.DurationSeconds > 0
                ? Math.Min(perClipDuration, clip.DurationSeconds)
                : perClipDuration;

            fallbackResult.Add((i, 0, clipDuration));
        }

        return (fallbackResult, false);
    }

    private async Task ProcessSingleClipAsync(
        string inputPath,
        string outputPath,
        double targetDuration,
        VideoConfig config,
        CancellationToken cancellationToken,
        bool isImageSource = false,
        VideoStyle overrideStyle = VideoStyle.None,
        VideoFilter filter = VideoFilter.None,
        int filterIntensity = 100,
        VideoTexture texture = VideoTexture.None,
        int textureOpacity = 30)
    {
        try
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath, cancellationToken);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();

            if (videoStream == null)
            {
                _logger.LogWarning("No video stream found in: {Path}", inputPath);
                return;
            }

            // Determine effective style: override takes precedence, otherwise fallback to config
        var effectiveStyle = overrideStyle != VideoStyle.None ? overrideStyle : config.Style;

        // Skip all filters and styles if drafting
        var effectiveFilter = config.IsDraftPreview ? VideoFilter.None : filter;
        var effectiveTexture = config.IsDraftPreview ? VideoTexture.None : texture;
        if (config.IsDraftPreview) effectiveStyle = VideoStyle.None;
        
        // If filter is None but style is set, convert style to filter/texture
        if (effectiveFilter == VideoFilter.None && effectiveTexture == VideoTexture.None && effectiveStyle != VideoStyle.None)
            {
                (effectiveFilter, effectiveTexture) = effectiveStyle switch
                {
                    VideoStyle.Painting => (VideoFilter.Painting, VideoTexture.None),
                    VideoStyle.Sepia => (VideoFilter.Sepia, VideoTexture.None),
                    VideoStyle.Canvas => (VideoFilter.Painting, VideoTexture.Canvas),
                    _ => (VideoFilter.None, VideoTexture.None)
                };
            }

            // Determine if we should apply artistic style
            // NOW: Apply to BOTH B-roll videos AND Ken Burns (image) clips if filter/texture is set
            var applyFilter = effectiveFilter != VideoFilter.None || effectiveTexture != VideoTexture.None;
            
            // Check for texture file if texture is specified
            string? texturePath = null;
            bool isVideoTexture = false;
            if (effectiveTexture != VideoTexture.None)
            {
                var textureSource = GetTexturePath(effectiveTexture);
                if (textureSource != null)
                {
                    texturePath = textureSource.Path;
                    isVideoTexture = textureSource.IsVideo;
                }
            }
            // Legacy Canvas style texture lookup (only if no explicit filter/texture)
            else if (effectiveStyle == VideoStyle.Canvas && effectiveTexture == VideoTexture.None)
            {
                var textureSource = GetTexturePath(VideoTexture.Canvas);
                if (textureSource != null)
                {
                    texturePath = textureSource.Path;
                    isVideoTexture = textureSource.IsVideo;
                }
            }

            var inputWidth = videoStream.Width;
            var inputHeight = videoStream.Height;
            var outputWidth = config.IsDraftPreview ? 480 : config.Width;
        var outputHeight = config.IsDraftPreview ? 854 : config.Height;
        
        var inputAspect = (double)inputWidth / inputHeight;
        var outputAspect = (double)outputWidth / outputHeight;

        // Build FFmpeg filter for blur background effect
        var bgFilter = BuildBlurBackgroundFilter(
            inputWidth, inputHeight, 
            outputWidth, outputHeight,
            inputAspect, outputAspect
        );
            
            // Integrate Artistic Style if enabled
        string filterComplex;
        if (applyFilter)
        {
            // artistic filter takes [vout] from bgFilter and outputs [styled]
            var artFilter = BuildArtisticFilterComplex(effectiveFilter, filterIntensity, effectiveTexture, textureOpacity, "[vout]", "[styled]", texturePath != null, isVideoTexture, outputWidth, outputHeight);
            
            filterComplex = bgFilter + ";" + artFilter;
        }
        else if (effectiveStyle != VideoStyle.None)
        {
            // Legacy style support
            var artFilter = BuildArtisticFilter(effectiveStyle, "[vout]", "[styled]", texturePath != null, isVideoTexture, outputWidth, outputHeight);
            filterComplex = bgFilter + ";" + artFilter;
        }
        else
        {
            filterComplex = bgFilter;
        }

            // Build duration filter - use InvariantCulture to ensure dot as decimal separator
            var durationArg = targetDuration > 0 && targetDuration < mediaInfo.Duration.TotalSeconds
                ? $"-t {targetDuration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}"
                : "";

        // Use raw FFmpeg command for complex filter
        var ffmpegPath = await FindFFmpegExecutablePathAsync();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            _logger.LogError("FFmpeg executable not found");
            return;
        }
        
        string arguments;
        if (config.IsDraftPreview)
        {
            // Draft mode: extremely fast, low quality, forced small resolution, no complex encoding
            arguments = $"-y -i \"{inputPath}\" {durationArg} -filter_complex \"{filterComplex}\" -map \"[styled]\" -c:v {config.VideoCodec} -preset ultrafast -crf 35 -pix_fmt yuv420p -r 15 \"{outputPath}\"";
        }
        else 
        {
            // Production Mode
            var inputs = $"-threads 0 -i \"{inputPath}\"";

            if (texturePath != null)
            {
                if (isVideoTexture)
                {
                    // For video textures: use stream_loop to repeat the video infinitely
                    inputs += $" -stream_loop -1 -i \"{texturePath}\"";
                }
                else
                {
                    // For image textures: loop the image
                    inputs += $" -f image2 -loop 1 -i \"{texturePath}\"";
                }
            }
            
            // Map output
            var mapOutput = applyFilter || effectiveStyle != VideoStyle.None
                ? "-map \"[styled]\"" 
                : "-map \"[vout]\"";

            arguments = $"{inputs} -filter_complex \"{filterComplex}\" " +
                           $"{mapOutput} -map 0:a? " +
                           $"-c:v {config.VideoCodec} -b:v {config.VideoBitrate}k -preset {(config.VideoCodec == "libx264" ? "fast" : "default")} -pix_fmt yuv420p -r {config.Fps} {durationArg} " +
                           $"-y \"{outputPath}\"";
        }

        _logger.LogDebug("FFmpeg command for clip processing: {Args}", arguments);

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
                _logger.LogWarning("FFmpeg processing failed for {Path}. Exit code: {Code}. Error: {Error}", 
                    inputPath, process.ExitCode, stderr);
            }
            else if (File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                _logger.LogInformation("Processed clip: {Input} -> {Output} ({Size} bytes)", 
                    Path.GetFileName(inputPath), Path.GetFileName(outputPath), fileInfo.Length);
            }
            else
            {
                _logger.LogWarning("Output file not created for clip: {Path}", inputPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process clip: {Path}", inputPath);
        }
    }

    private void CleanupTempFiles(
        List<string> processedClips,
        List<(string LocalPath, VideoClip Original)> downloadedClips)
    {
        // Delete processed clips
        foreach (var path in processedClips)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp file: {Path}", path);
            }
        }

        // Delete downloaded clips (only if they were downloaded, not original local files)
        foreach (var (localPath, original) in downloadedClips)
        {
            // Only delete if this was a downloaded file (not from SourcePath)
            if (localPath != original.SourcePath && localPath.Contains(_tempDirectory))
            {
                try
                {
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete downloaded file: {Path}", localPath);
                }
            }
        }
    }
}
