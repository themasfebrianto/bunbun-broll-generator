using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using BunbunBroll.Services;
using BunbunBroll.Models;

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{
    // Expansion state
    private bool _isExpanding = false;
    private bool _isSlicing = false;
    private bool _isValidating = false;
    private string? _expansionError = null;
    private bool _showExpansionDetails = false;

    // File paths
    private string? _voFilePath = null;
    private string? _srtFilePath = null;
    private string? _stitchedVoUrl = null;

    // Expansion data
    private List<SrtEntry>? _expandedEntries = null;
    private Dictionary<int, double>? _pauseDurations = null;
    private ExpansionStats? _expansionStats = null;
    private List<VoSegment>? _voSegments = null;
    private VoSliceValidationResult? _validationResult = null;

    [Inject] private ISrtExpansionService SrtExpansionService { get; set; } = null!;
    [Inject] private IVoSlicingService VoSlicingService { get; set; } = null!;

    private void DetectExistingVoAndSrt()
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
            var expansionResult = await SrtExpansionService.ExpandCapCutSrtAsync(
                capCutSrtPath,
                _sessionId ?? Guid.NewGuid().ToString(),
                outputDir
            );

            if (!expansionResult.IsSuccess)
            {
                _expansionError = expansionResult.ErrorMessage ?? "Expansion failed";
                return;
            }

            _expandedEntries = expansionResult.ExpandedEntries;
            _pauseDurations = expansionResult.PauseDurations;
            _expansionStats = expansionResult.Statistics;

            if (_resultSession != null)
            {
                _resultSession.ExpandedSrtPath = expansionResult.ExpandedSrtPath;
                _resultSession.ExpandedAt = DateTime.UtcNow;
                _resultSession.ExpansionStatistics = expansionResult.Statistics;
            }

            // 2. Slice VO
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

            // 3. Stitch VO back together with pauses
            var stitchedPath = await VoSlicingService.StitchVoAsync(_voSegments, _pauseDurations, sliceResult.OutputDirectory);
            if (!string.IsNullOrEmpty(stitchedPath))
            {
                if (_resultSession != null)
                {
                    _resultSession.StitchedVoPath = stitchedPath;
                }
                
                // Set relative URL for player
                // Assuming "output" is mapped to "/project-assets"
                _stitchedVoUrl = $"/project-assets/{_sessionId}/stitched_vo.mp3";
            }
            else
            {
                _expansionError = "VO stitching failed.";
                return;
            }

            // 4. Validate
            await HandleValidateSlices();
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
}
