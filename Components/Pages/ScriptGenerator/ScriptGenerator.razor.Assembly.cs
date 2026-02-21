using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{
    // Audio & Assembly State
    private string? _voPath;
    private string? _srtPath;
    private bool _isVoUploading;
    private bool _isSrtUploading;
    private bool _isComposingVideo;
    private string? _assemblyError;
    private string? _compositionProgress;
    private string? _finalVideoPath;

    private void HandleGoToAudioAssembly()
    {
        _currentView = "audio-assembly";
        AutoDetectVoAndSrt();
    }

    private void AutoDetectVoAndSrt()
    {
        if (string.IsNullOrEmpty(_sessionId)) return;

        var voDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId, "vo");
        if (Directory.Exists(voDir))
        {
            var voFiles = Directory.GetFiles(voDir, "*.*")
                .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
                            f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                            f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (voFiles.Any()) _voPath = voFiles.First();
        }

        var srtDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId, "srt");
        if (Directory.Exists(srtDir))
        {
            var srtFiles = Directory.GetFiles(srtDir, "*.*")
                .Where(f => f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) || 
                            f.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (srtFiles.Any()) _srtPath = srtFiles.First();
        }
    }


    private async Task HandleVoUpload(InputFileChangeEventArgs e)
    {
        _isVoUploading = true;
        _assemblyError = null;
        StateHasChanged();

        try
        {
            var file = e.File;
            var ext = Path.GetExtension(file.Name).ToLower();
            if (ext != ".mp3" && ext != ".wav" && ext != ".m4a")
            {
                _assemblyError = "Invalid voiceover format. Please upload MP3, WAV, or M4A.";
                return;
            }

            // Save to Session Directory
            var sessionDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId!,"vo");
            Directory.CreateDirectory(sessionDir);
            
            var safeFileName = $"vo_{DateTime.Now.Ticks}{ext}";
            var path = Path.Combine(sessionDir, safeFileName);

            // Max 50MB
            using var stream = file.OpenReadStream(1024 * 1024 * 50);
            using var fs = new FileStream(path, FileMode.Create);
            await stream.CopyToAsync(fs);

            _voPath = path;
        }
        catch (Exception ex)
        {
            _assemblyError = $"Error uploading VO: {ex.Message}";
        }
        finally
        {
            _isVoUploading = false;
            StateHasChanged();
        }
    }

    private async Task HandleSrtUpload(InputFileChangeEventArgs e)
    {
        _isSrtUploading = true;
        _assemblyError = null;
        StateHasChanged();

        try
        {
            var file = e.File;
            var ext = Path.GetExtension(file.Name).ToLower();
            if (ext != ".srt" && ext != ".lrc")
            {
                _assemblyError = "Invalid subtitle format. Please upload SRT or LRC.";
                return;
            }

            var sessionDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId!,"srt");
            Directory.CreateDirectory(sessionDir);
            
            var safeFileName = $"sub_{DateTime.Now.Ticks}{ext}";
            var path = Path.Combine(sessionDir, safeFileName);

            // Max 5MB
            using var stream = file.OpenReadStream(1024 * 1024 * 5);
            using var fs = new FileStream(path, FileMode.Create);
            await stream.CopyToAsync(fs);

            _srtPath = path;
        }
        catch (Exception ex)
        {
            _assemblyError = $"Error uploading subtitles: {ex.Message}";
        }
        finally
        {
            _isSrtUploading = false;
            StateHasChanged();
        }
    }

    private void HandleRemoveVo()
    {
        if (!string.IsNullOrEmpty(_voPath) && File.Exists(_voPath))
        {
            try { File.Delete(_voPath); } catch { }
        }
        _voPath = null;
    }

    private void HandleRemoveSrt()
    {
        if (!string.IsNullOrEmpty(_srtPath) && File.Exists(_srtPath))
        {
            try { File.Delete(_srtPath); } catch { }
        }
        _srtPath = null;
    }

    private async Task HandleComposeFinalVideo()
    {
        if (string.IsNullOrEmpty(_voPath) || string.IsNullOrEmpty(_srtPath)) return;
        
        _isComposingVideo = true;
        _assemblyError = null;
        _compositionProgress = "Starting composition...";
        StateHasChanged();

        try
        {
            // Create a config using the parameters from B-Roll section (or default)
            var config = new BunbunBroll.Models.VideoConfig
            {
                CapCutAudioPath = _voPath,
                CapCutSrtPath = _srtPath,
                // Add more config parameters here as needed
            };

            // Collect all clips that were finalized in B-Roll step
            var clips = new List<BunbunBroll.Models.VideoClip>();
            foreach(var item in _brollPromptItems)
            {
                if(item.MediaType == BunbunBroll.Models.BrollMediaType.BrollVideo && (!string.IsNullOrEmpty(item.FilteredVideoPath) || !string.IsNullOrEmpty(item.LocalVideoPath) || !string.IsNullOrEmpty(item.SelectedVideoUrl)))
                {
                    string finalPath = !string.IsNullOrEmpty(item.FilteredVideoPath) ? item.FilteredVideoPath :
                                       !string.IsNullOrEmpty(item.LocalVideoPath) ? item.LocalVideoPath : 
                                       ResolveLocalPath(item.SelectedVideoUrl!);

                    clips.Add(new BunbunBroll.Models.VideoClip 
                    { 
                        SourcePath = finalPath,
                        SourceUrl = item.SelectedVideoUrl,
                        AssociatedText = item.ScriptText
                    });
                }
                else if (item.MediaType == BunbunBroll.Models.BrollMediaType.ImageGeneration)
                {
                     // Use filtered video if available, then Ken Burns, then static image
                     if (!string.IsNullOrEmpty(item.FilteredVideoPath))
                     {
                          clips.Add(new BunbunBroll.Models.VideoClip 
                          { 
                              SourcePath = item.FilteredVideoPath,
                              AssociatedText = item.ScriptText
                          });
                     }
                     else if (!string.IsNullOrEmpty(item.WhiskVideoPath))
                     {
                          clips.Add(new BunbunBroll.Models.VideoClip 
                          { 
                              SourcePath = item.WhiskVideoPath,
                              AssociatedText = item.ScriptText
                          });
                     }
                     else if (!string.IsNullOrEmpty(item.WhiskImagePath))
                     {
                          clips.Add(BunbunBroll.Models.VideoClip.FromImage(item.WhiskImagePath, item.ScriptText, 3.0));
                     }
                }
            }

            var progressReporter = new Progress<BunbunBroll.Models.CompositionProgress>(p =>
            {
                _ = InvokeAsync(() =>
                {
                    _compositionProgress = $"[{p.Percent}%] {p.Stage}: {p.Message}";
                    StateHasChanged();
                });
            });

            var result = await VideoComposer.ComposeAsync(clips, config, _sessionId, progressReporter, CancellationToken.None);

            if (result.Success)
            {
                _finalVideoPath = result.OutputPath;
                _compositionProgress = "Video composed successfully!";
            }
            else
            {
                _assemblyError = result.ErrorMessage;
                _compositionProgress = null;
            }
        }
        catch (Exception ex)
        {
            _assemblyError = $"Composition failed: {ex.Message}";
            _compositionProgress = null;
        }
        finally
        {
            _isComposingVideo = false;
            StateHasChanged();
        }
    }
}
