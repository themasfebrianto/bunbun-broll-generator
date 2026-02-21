using BunbunBroll.Models;
using Xabe.FFmpeg;
using System.Diagnostics;

namespace BunbunBroll.Services;

/// <summary>
/// VideoComposer partial: Filter, texture, and style methods.
/// </summary>
public partial class VideoComposer
{
    /// <summary>
    /// Apply separate filter and texture to a video.
    /// </summary>
    public async Task<string?> ApplyFilterAndTextureToVideoAsync(
        string inputPath,
        VideoFilter filter,
        int filterIntensity,
        VideoTexture texture,
        int textureOpacity,
        VideoConfig config,
        CancellationToken cancellationToken = default,
        bool isPreview = false,
        string? sessionId = null)
    {
        try
        {
            if (!await EnsureFFmpegAsync()) return null;

            // Use session-scoped temp directory if sessionId is provided
            var tempDir = !string.IsNullOrEmpty(sessionId)
                ? Path.Combine(_tempDirectory, sessionId)
                : _tempDirectory;
            Directory.CreateDirectory(tempDir);
            var outputPath = Path.Combine(tempDir, $"styled_{Guid.NewGuid():N}.mp4");

            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath, cancellationToken);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();

            if (videoStream == null)
            {
                _logger.LogWarning("No video stream found in: {Path}", inputPath);
                return null;
            }

            var inputWidth = videoStream.Width;
            var inputHeight = videoStream.Height;
            var inputAspect = (double)inputWidth / inputHeight;
            
            // Preview Optimization: Downscale to 480p
            int targetWidth = config.Width;
            int targetHeight = config.Height;
            
            if (isPreview)
            {
                targetHeight = 480;
                targetWidth = (int)(targetHeight * inputAspect);
                targetWidth = (targetWidth / 2) * 2;
                if (targetWidth > 854) targetWidth = 854; 
            }

            var outputAspect = (double)targetWidth / targetHeight;

            // Get texture source (image or video)
            var textureSource = GetTexturePath(texture);
            bool hasTexture = textureSource != null && File.Exists(textureSource.Path);
            
            if (texture != VideoTexture.None)
            {
                _logger.LogInformation("Texture requested: {Texture}, Path: {Path}, IsVideo: {IsVideo}, Exists: {Exists}", 
                    texture, textureSource?.Path ?? "null", textureSource?.IsVideo ?? false, hasTexture);
            }

            // Build blur background filter
            var bgFilter = BuildBlurBackgroundFilter(
                inputWidth, inputHeight, 
                targetWidth, targetHeight,
                inputAspect, outputAspect
            );

            // Build artistic filter with separate filter + texture
            bool isVideoTexture = textureSource?.IsVideo ?? false;
            string filterComplex;
            var artFilter = BuildArtisticFilterComplex(filter, filterIntensity, texture, textureOpacity, "[vout]", "[styled]", hasTexture, isVideoTexture, targetWidth, targetHeight);
            
            filterComplex = bgFilter + ";" + artFilter;

            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath)) return null;

            var inputs = $"-threads 0 -i \"{inputPath}\"";
            if (hasTexture && textureSource != null)
            {
                if (textureSource.IsVideo)
                {
                    // For video textures: use stream_loop to repeat the video infinitely
                    inputs += $" -stream_loop -1 -i \"{textureSource.Path}\"";
                }
                else
                {
                    // For image textures: loop the image
                    inputs += $" -f image2 -loop 1 -i \"{textureSource.Path}\"";
                }
            }

            var durationLimit = "";
            if (isPreview && mediaInfo.Duration.TotalSeconds > 15)
            {
                durationLimit = "-t 15";
            }
            
            var preset = isPreview ? "ultrafast" : _preset;
            var crf = isPreview ? 28 : _crf;

            var arguments = $"{inputs} -filter_complex \"{filterComplex}\" " +
                           $"-map \"[styled]\" -map 0:a? " +
                           $"-c:v libx264 -preset {preset} -crf {crf} -threads 0 " +
                           $"-c:a aac -b:a 128k -ac 2 -ar 44100 " +
                           $"{durationLimit} -y \"{outputPath}\"";

            _logger.LogInformation("Applying filter {Filter} + texture {Texture} to {Path} -> {Output}", 
                filter, texture, inputPath, outputPath);
            _logger.LogDebug("FFmpeg filter_complex: {FilterComplex}", filterComplex);
            _logger.LogDebug("FFmpeg inputs: {Inputs}", inputs);

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
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                var partialStderr = await stderrTask;
                _logger.LogWarning("FFmpeg filter preview timed out after 60s.");
                throw new TimeoutException($"Filter timed out after 60 seconds.");
            }
            
            var stderr = await stderrTask;
            var stdout = await stdoutTask;

            if (process.ExitCode != 0)
            {
                var stderrLines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var errorSummary = string.Join(" | ", stderrLines.TakeLast(3)).Trim();
                _logger.LogWarning("FFmpeg style failed [{Filter}+{Texture}]. Error: {Error}", 
                    filter, texture, errorSummary);
                throw new InvalidOperationException($"FFmpeg filter failed: {errorSummary}");
            }

            if (File.Exists(outputPath))
            {
                _logger.LogInformation("Filter + Texture applied successfully -> {Output}", outputPath);
                return outputPath;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply filter/texture to video: {Path}", inputPath);
            return null;
        }
    }

    /// <summary>
    /// Represents a texture source - can be image or video
    /// </summary>
    private record TextureSource(string Path, bool IsVideo);

    private TextureSource? GetTexturePath(VideoTexture texture)
    {
        if (texture == VideoTexture.None)
            return null;

        // Cari texture di beberapa lokasi (dalam urutan prioritas)
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchPaths = new[]
        {
            // 1. Relative to executable (published/output folder)
            Path.Combine(baseDir, "wwwroot", "assets", "textures"),
            // 2. Project root folder (development)
            Path.Combine(baseDir, "..", "..", "..", "wwwroot", "assets", "textures"),
            // 3. Working directory
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "textures"),
            // 4. Absolute path from project root
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "wwwroot", "assets", "textures"),
        };

        string? textureDir = null;
        foreach (var path in searchPaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                textureDir = fullPath;
                _logger.LogDebug("Texture directory found: {Dir}", fullPath);
                break;
            }
        }

        if (textureDir == null)
        {
            _logger.LogWarning("Texture directory not found in any of these locations: {Paths}", 
                string.Join(", ", searchPaths.Select(p => Path.GetFullPath(p))));
            return null;
        }

        // Available video texture files:
        // filmgrain.mp4, filmgrain_2.mp4, filmgrain_colorfull.mp4, 
        // fire.mp4, grunge.mp4, harsh.mp4, scratches.mp4, surreal.mp4
        
        // Explicit filename mapping for reliability
        var preferredFiles = texture switch
        {
            VideoTexture.Canvas => new[] { "surreal.mp4", "harsh.mp4" },
            VideoTexture.Paper => new[] { "harsh.mp4", "filmgrain.mp4" },
            VideoTexture.Grunge => new[] { "grunge.mp4", "harsh.mp4" },
            VideoTexture.FilmGrain => new[] { "filmgrain.mp4", "filmgrain_2.mp4", "filmgrain_colorfull.mp4" },
            VideoTexture.Dust => new[] { "fire.mp4", "harsh.mp4" },
            VideoTexture.Scratches => new[] { "scratches.mp4" },
            _ => Array.Empty<string>()
        };

        // Try explicit filenames first
        foreach (var filename in preferredFiles)
        {
            var fullPath = Path.Combine(textureDir, filename);
            if (File.Exists(fullPath))
            {
                _logger.LogInformation("Video texture found for {Texture}: {Path}", texture, fullPath);
                return new TextureSource(fullPath, true);
            }
        }

        var videoExtensions = new[] { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".m4v" };
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".webp" };

        // List all files in texture dir for debugging
        var allFiles = Directory.GetFiles(textureDir, "*.*");
        _logger.LogDebug("Texture directory {Dir} contains {Count} files: {Files}", 
            textureDir, allFiles.Length, string.Join(", ", allFiles.Select(Path.GetFileName)));

        // Pattern fallback
        var patterns = texture switch
        {
            VideoTexture.Canvas => new[] { "surreal", "harsh", "grain", "canvas" },
            VideoTexture.Paper => new[] { "harsh", "grain", "paper" },
            VideoTexture.Grunge => new[] { "grunge", "harsh", "rough" },
            VideoTexture.FilmGrain => new[] { "filmgrain", "grain", "film" },
            VideoTexture.Dust => new[] { "fire", "harsh", "dust" },
            VideoTexture.Scratches => new[] { "scratches", "scratch" },
            _ => Array.Empty<string>()
        };

        // Try pattern matching on video files
        foreach (var pattern in patterns)
        {
            var searchPattern = $"*{pattern}*.*";
            var files = Directory.GetFiles(textureDir, searchPattern, SearchOption.TopDirectoryOnly)
                .Where(f => videoExtensions.Any(ext => 
                    f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            
            if (files.Length > 0)
            {
                var selected = files[Random.Shared.Next(files.Length)];
                _logger.LogInformation("Video texture found for {Texture}: {Path}", texture, selected);
                return new TextureSource(selected, true);
            }
        }

        // Fallback to image textures
        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(textureDir, pattern + ".*", SearchOption.TopDirectoryOnly)
                .Where(f => imageExtensions.Any(ext => 
                    f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            
            if (files.Length > 0)
            {
                var selected = files[Random.Shared.Next(files.Length)];
                _logger.LogInformation("Image texture found for {Texture}: {Path}", texture, selected);
                return new TextureSource(selected, false);
            }
        }

        // Fallback to any texture file
        var allTextures = Directory.GetFiles(textureDir, "*.*")
            .Where(f => videoExtensions.Concat(imageExtensions).Any(ext => 
                f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        
        if (allTextures.Length > 0)
        {
            var fallback = allTextures[Random.Shared.Next(allTextures.Length)];
            var isVideo = videoExtensions.Any(ext => 
                fallback.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            _logger.LogInformation("Using fallback texture: {Path} (Video: {IsVideo})", fallback, isVideo);
            return new TextureSource(fallback, isVideo);
        }
        
        _logger.LogWarning("No texture files found in: {Dir}", textureDir);
        return null;
    }

    public async Task<string?> ApplyStyleToVideoAsync(
        string inputPath,
        VideoStyle style,
        VideoConfig config,
        CancellationToken cancellationToken = default,
        bool isPreview = false,
        string? sessionId = null)
    {
        try
        {
            if (!await EnsureFFmpegAsync()) return null;

            // Use session-scoped temp directory if sessionId is provided
            var tempDir = !string.IsNullOrEmpty(sessionId)
                ? Path.Combine(_tempDirectory, sessionId)
                : _tempDirectory;
            Directory.CreateDirectory(tempDir);
            var outputPath = Path.Combine(tempDir, $"styled_{Guid.NewGuid():N}.mp4");

            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath, cancellationToken);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();

            if (videoStream == null)
            {
                _logger.LogWarning("No video stream found in: {Path}", inputPath);
                return null;
            }

            var inputWidth = videoStream.Width;
            var inputHeight = videoStream.Height;
            var inputAspect = (double)inputWidth / inputHeight;
            
            // Preview Optimization: Downscale to 480p
            int targetWidth = config.Width;
            int targetHeight = config.Height;
            
            if (isPreview)
            {
                targetHeight = 480;
                targetWidth = (int)(targetHeight * inputAspect);
                // Ensure even dimensions
                targetWidth = (targetWidth / 2) * 2;
                // Cap width if it gets too crazy (e.g. ultra-wide)
                if (targetWidth > 854) targetWidth = 854; 
            }

            var outputAspect = (double)targetWidth / targetHeight;

            // For legacy VideoStyle.Canvas, also try to find video textures
            TextureSource? textureSource = null;
            if (style == VideoStyle.Canvas)
            {
                textureSource = GetTexturePath(VideoTexture.Canvas);
            }
            
            bool hasTexture = textureSource != null;
            bool isVideoTexture = textureSource?.IsVideo ?? false;

            // reuse BlurBackground filter
            var bgFilter = BuildBlurBackgroundFilter(
                inputWidth, inputHeight, 
                targetWidth, targetHeight,
                inputAspect, outputAspect
            );

            string filterComplex;
            var artFilter = BuildArtisticFilter(style, "[vout]", "[styled]", hasTexture, isVideoTexture, targetWidth, targetHeight);
            
            if (hasTexture)
            {
                filterComplex = bgFilter + ";" + artFilter;
            }
            else
            {
                filterComplex = bgFilter + ";" + artFilter;
            }

            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath)) return null;

            var inputs = $"-threads 0 -i \"{inputPath}\"";
            if (hasTexture && textureSource != null)
            {
                if (textureSource.IsVideo)
                {
                    // For video textures: use stream_loop to repeat the video infinitely
                    inputs += $" -stream_loop -1 -i \"{textureSource.Path}\"";
                }
                else
                {
                    // For image textures: loop the image
                    inputs += $" -f image2 -loop 1 -i \"{textureSource.Path}\"";
                }
            }

            // Cap preview duration to 15s to avoid long FFmpeg processing
            var durationLimit = "";
            if (mediaInfo.Duration.TotalSeconds > 15)
            {
                durationLimit = "-t 15";
            }
            
            // Preview Optimization: Use ultrafast preset
            var preset = isPreview ? "ultrafast" : _preset;
            var crf = isPreview ? 28 : _crf;

            var arguments = $"{inputs} -filter_complex \"{filterComplex}\" " +
                           $"-map \"[styled]\" -map 0:a? " +
                           $"-c:v libx264 -preset {preset} -crf {crf} -threads 0 " +
                           $"-c:a aac -b:a 128k -ac 2 -ar 44100 " +
                           $"{durationLimit} -y \"{outputPath}\"";

            _logger.LogInformation("Applying style {Style} to {Path} -> {Output}", style, inputPath, outputPath);
            _logger.LogDebug("FFmpeg filter command: {Cmd}", arguments);

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
            
            // Timeout: 60 seconds max for preview filter
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout hit, not user cancellation
                try { process.Kill(entireProcessTree: true); } catch { }
                var partialStderr = await stderrTask;
                _logger.LogWarning("FFmpeg style preview timed out after 60s. Partial stderr: {Err}", 
                    partialStderr.Length > 500 ? partialStderr[^500..] : partialStderr);
                throw new TimeoutException($"Filter timed out after 60 seconds. The video may be too long or complex.");
            }
            
            var stderr = await stderrTask;
            var stdout = await stdoutTask;

            if (process.ExitCode != 0)
            {
                // Extract last few lines of stderr for a meaningful error
                var stderrLines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var errorSummary = string.Join(" | ", stderrLines.TakeLast(3)).Trim();
                _logger.LogWarning("FFmpeg style failed [{Style}]. Exit: {Code}. Error: {Error}", 
                    style, process.ExitCode, errorSummary);
                throw new InvalidOperationException($"FFmpeg filter failed: {errorSummary}");
            }

            if (File.Exists(outputPath))
            {
                _logger.LogInformation("Style {Style} applied successfully -> {Output}", style, outputPath);
                return outputPath;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply style to video: {Path}", inputPath);
            return null;
        }
    }

    /// <summary>
    /// Builds FFmpeg filter_complex string for blur background effect.
    /// This creates a blurred, scaled background with the sharp video centered on top.
    /// </summary>
    private string BuildBlurBackgroundFilter(
        int inputWidth, int inputHeight,
        int outputWidth, int outputHeight,
        double inputAspect, double outputAspect)
    {
        // If input is already close to output aspect ratio, just scale
        if (Math.Abs(inputAspect - outputAspect) < 0.1)
        {
            return $"[0:v]scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=decrease," +
                   $"pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2:black[vout]";
        }

        // Calculate foreground scale to fit within output while maintaining aspect ratio
        int fgWidth, fgHeight;
        if (inputAspect > outputAspect)
        {
            // Input is wider - fit to width
            fgWidth = outputWidth;
            fgHeight = (int)(outputWidth / inputAspect);
        }
        else
        {
            // Input is taller - fit to height
            fgHeight = outputHeight;
            fgWidth = (int)(outputHeight * inputAspect);
        }

        // Ensure even dimensions for video encoding
        fgWidth = (fgWidth / 2) * 2;
        fgHeight = (fgHeight / 2) * 2;

        // Build blur background filter (optimized for speed):
        // 1. Scale video to fill output (will crop) - using fast bilinear
        // 2. Apply lighter blur (radius 15 vs 25 for speed)
        // 3. Scale original video to fit (foreground)
        // 4. Overlay foreground centered on blurred background
        var filter = 
            // Background: scale to fill, crop to output size, then blur (lighter for speed)
            $"[0:v]scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=increase:flags=fast_bilinear," +
            $"crop={outputWidth}:{outputHeight}," +
            $"boxblur=luma_radius=15:luma_power=1[bg];" +
            // Foreground: scale to fit within output
            $"[0:v]scale={fgWidth}:{fgHeight}:flags=fast_bilinear[fg];" +
            // Overlay foreground centered on background
            $"[bg][fg]overlay=(W-w)/2:(H-h)/2[vout]";

        return filter;
    }

    /// <summary>
    /// Builds FFmpeg filter chain for artistic effects (legacy VideoStyle support).
    /// </summary>
    private string BuildArtisticFilter(VideoStyle style, string inputPad, string outputPad, bool hasTexture, bool isVideoTexture = false, int? width = null, int? height = null)
    {
        // Map VideoStyle to Filter + Texture combination
        var (filter, texture) = style switch
        {
            VideoStyle.Painting => (VideoFilter.Painting, VideoTexture.None),
            VideoStyle.Sepia => (VideoFilter.Sepia, VideoTexture.None),
            VideoStyle.Canvas => (VideoFilter.Painting, VideoTexture.Canvas),
            _ => (VideoFilter.None, VideoTexture.None)
        };

        return BuildArtisticFilterComplex(filter, 100, texture, 30, inputPad, outputPad, hasTexture, isVideoTexture, width, height);
    }

    /// <summary>
    /// Builds FFmpeg filter chain for separate filter and texture.
    /// Best practice: use lut for opacity (faster than colorchannelmixer),
    /// use format with fallback (yuva420p|yuva444p|rgba) for best compatibility,
    /// use overlay filter for blending (more efficient than blend for simple opacity).
    /// Supports both image and video textures.
    /// </summary>
    private string BuildArtisticFilterComplex(VideoFilter filter, int filterIntensity, VideoTexture texture, int textureOpacity, string inputPad, string outputPad, bool hasTexture, bool isVideoTexture = false, int? width = null, int? height = null)
    {
        // Build the base filter chain
        var filterChain = BuildFilterChain(filter, inputPad, "[filtered_raw]");
        
        // Intensity blend map
        // If intensity is < 100, we blend the filtered output with the raw input using the blend filter
        string blendFilter = "";
        string filteredNode = "[filtered_raw]";
        if (filter != VideoFilter.None && filterIntensity < 100)
        {
            var intensityFactor = (double)filterIntensity / 100.0;
            var c0Opacity = intensityFactor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            blendFilter = $";[filtered_raw]{inputPad}blend=all_expr='A*{c0Opacity}+B*(1-{c0Opacity})'[filtered_blended]";
            filteredNode = "[filtered_blended]";
        }
        else if (filter == VideoFilter.None)
        {
            // if no filter, the raw input pad is the active node
            filteredNode = inputPad;
            filterChain = ""; // clear so we don't append it
        }

        // Check global vignette setting
        var vignetteEnabled = _styleSettings.VignetteEnabled;
        var vignetteFilter = vignetteEnabled ? (filteredNode == inputPad && string.IsNullOrEmpty(filterChain) ? "" : ",") + "vignette=PI/6:aspect=1" : "";
        
        // If no texture, add vignette (if enabled) and return
        if (!hasTexture || texture == VideoTexture.None)
        {
            if (filter == VideoFilter.None && !vignetteEnabled)
                return $"{inputPad}null{outputPad}";
                
            if (filter == VideoFilter.None && vignetteEnabled)
                return $"{inputPad}vignette=PI/6:aspect=1{outputPad}";
                
            return filterChain + blendFilter + $"{filteredNode}{vignetteFilter}{outputPad}";
        }

        // With texture: apply texture overlay using best practices
        // 1. Scale texture to match video size
        // 2. Convert to format with alpha (yuva420p|yuva444p|rgba - let ffmpeg choose best)
        // 3. Apply opacity using lut filter (faster than colorchannelmixer)
        // 4. Overlay on top of video
        
        // Calculate dynamic opacity value (0-255 for lut filter) based on user's TextureOpacity (0-100)
        // Use the base texture factors as maximum multipliers
        var baseOpacityFactor = texture switch
        {
            VideoTexture.Canvas => 0.40,
            VideoTexture.Paper => 0.30,
            VideoTexture.Grunge => 0.35,
            VideoTexture.FilmGrain => 0.25,
            VideoTexture.Dust => 0.20,
            VideoTexture.Scratches => 0.15,
            _ => 0.30
        };
        var finalOpacityValue = (int)(255 * baseOpacityFactor * (textureOpacity / 100.0));
        var opacityValue = finalOpacityValue.ToString();

        // For image textures, use loop filter to hold the image; video textures use -stream_loop input option
        var loopFilter = !isVideoTexture && texture != VideoTexture.None ? "loop=loop=-1:size=1," : "";
        
        // Vignette filter for texture path (without comma prefix)
        var textureVignetteFilter = vignetteEnabled ? "vignette=PI/6:aspect=1" : "null";

        var combinedBaseFilter = string.IsNullOrEmpty(filterChain) ? "" : filterChain + blendFilter + ";";

        if (width.HasValue && height.HasValue)
        {
            // Best practice: format=yuva420p|yuva444p|rgba lets ffmpeg choose best available
            // lut=a={opacity} is faster than colorchannelmixer for setting alpha
            return $"{combinedBaseFilter}" +
                   $"[1:v]{loopFilter}scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}," +
                   $"format=yuva420p|yuva444p|rgba," +
                   $"lut=a={opacityValue}[tex];" + 
                   $"{filteredNode}[tex]overlay=format=auto:shortest=1[ovout];" +
                   $"[ovout]{textureVignetteFilter}{outputPad}";
        }
        else
        {
            // Fallback: use scale2ref to match texture to base video size
            return $"{combinedBaseFilter}" +
                   $"[1:v]{filteredNode}scale2ref[tex][base_ref];" + 
                   $"[tex]{loopFilter}setsar=1,format=yuva420p|yuva444p|rgba,lut=a={opacityValue}[tex_adj];" + 
                   $"[base_ref][tex_adj]overlay=format=auto:shortest=1[ovout];" +
                   $"[ovout]{textureVignetteFilter}{outputPad}";
        }
    }

    /// <summary>
    /// Builds the filter chain for a specific filter type.
    /// </summary>
    private string BuildFilterChain(VideoFilter filter, string inputPad, string outputPad)
    {
        return filter switch
        {
            VideoFilter.Painting => 
                $"{inputPad}fps=12," +
                $"smartblur=lr=2.0:ls=-0.5:lt=-5.0," +
                $"eq=saturation=0.6:contrast=1.2:brightness=-0.05," +
                $"colorbalance=rs=0.1:gs=0.05:bs=-0.2," +
                $"vignette=PI/4" +
                $"{outputPad}",

            VideoFilter.Sepia => 
                $"{inputPad}fps=15," +
                $"colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131," +
                $"smartblur=lr=1.5:ls=-0.8," +
                $"noise=alls=10:allf=t," +
                $"vignette=PI/5" +
                $"{outputPad}",

            VideoFilter.Vintage => 
                $"{inputPad}fps=15," +
                $"curves=r='0/0 0.5/0.6 1/0.9':g='0/0 0.5/0.55 1/0.85':b='0/0 0.5/0.5 1/0.8'," +
                $"smartblur=lr=1.0:ls=-0.3," +
                $"noise=alls=8:allf=t," +
                $"vignette=PI/5" +
                $"{outputPad}",

            VideoFilter.Cinematic => 
                $"{inputPad}fps=24," +
                $"eq=saturation=0.85:contrast=1.15:brightness=-0.02," +
                $"colorbalance=rs=0.05:gs=0:bs=-0.05," +
                $"vignette=PI/6" +
                $"{outputPad}",

            VideoFilter.Warm => 
                $"{inputPad}eq=saturation=1.1:contrast=1.05:brightness=0.02," +
                $"colorbalance=rs=0.1:gs=0.03:bs=-0.08," +
                $"smartblur=lr=0.5:ls=-0.2," +
                $"vignette=PI/6" +
                $"{outputPad}",

            VideoFilter.Cool => 
                $"{inputPad}eq=saturation=0.95:contrast=1.05," +
                $"colorbalance=rs=-0.05:gs=0.02:bs=0.1," +
                $"smartblur=lr=0.5:ls=-0.2," +
                $"vignette=PI/6" +
                $"{outputPad}",

            VideoFilter.Noir => 
                $"{inputPad}fps=18," +
                $"colorchannelmixer=.33:.33:.33:0:.33:.33:.33:0:.33:.33:.33," +
                $"eq=contrast=1.4:brightness=-0.1," +
                $"smartblur=lr=1.0:ls=-0.5," +
                $"noise=alls=12:allf=t," +
                $"vignette=PI/3" +
                $"{outputPad}",

            _ => $"{inputPad}null{outputPad}"
        };
    }
}
