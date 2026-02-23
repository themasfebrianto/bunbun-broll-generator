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
    private BrollSessionMetadata? _storedMetadata;
    private string? _brollWarning;

    private async Task HandleSendToBroll()
    {
        if (_resultSession == null || _resultSections.Count == 0) return;

        if (_brollPromptItems.Count == 0)
        {
            await LoadBrollPromptsFromDisk();
            await LoadImageConfigFromDisk();
            await LoadGlobalContextFromDisk();
        }

        if (_brollPromptItems.Count > 0)
        {
            _canProceedToStep3 = true;
        }

        GoToStepDirect(1);
    }

    private async Task ResetAndInitializeBrollFromSrt()
    {
        if (_expandedEntries == null || _expandedEntries.Count == 0) return;

        // 1. Wipe existing progress (disk + memory)
        BrollPersistence.InvalidateBrollClassification(_brollPromptItems, _resultSession, _sessionId);
        _brollPromptItems.Clear();
        _classifyTotalSegments = 0;
        _classifyCompletedSegments = 0;

        // 2. Merge micro-segments (~500) into logical scenes (~50-80)
        var mergedSegments = SrtService.MergeToSegments(_expandedEntries, maxDurationSeconds: 20.0);

        // 3. Build overlay lookup from expansion result
        var overlayLookup = _expansionResult?.DetectedOverlays ?? new Dictionary<int, TextOverlayDto>();

        // 3a. Inject overlay markers back into expanded entries so MergeToSegments preserves them
        foreach (var (entryIndex, dto) in overlayLookup)
        {
            if (entryIndex < 0 || entryIndex >= _expandedEntries.Count) continue;
            var entry = _expandedEntries[entryIndex];
            // Only inject if not already present
            if (!entry.Text.Contains("[OVERLAY:", StringComparison.OrdinalIgnoreCase))
            {
                var marker = $"[OVERLAY:{dto.Type}]";
                if (!string.IsNullOrWhiteSpace(dto.Arabic))
                    marker += $" [ARABIC]{dto.Arabic}";
                if (!string.IsNullOrWhiteSpace(dto.Reference))
                    marker += $" [REF]{dto.Reference}";
                entry.Text = $"{marker} {entry.Text}";
            }
        }

        // 4. Convert merged segments to BrollPromptItems
        int idx = 0;
        foreach (var (startTime, endTime, timestamp, text) in mergedSegments)
        {
            string textForParsing = text;
            var overlay = ScriptProcessor.ExtractTextOverlay(ref textForParsing);

            if (overlay != null && string.IsNullOrWhiteSpace(overlay.Text))
            {
                overlay.Text = overlay.Type == TextOverlayType.KeyPhrase
                    ? BunbunBroll.Services.ScriptProcessor.TruncateForKeyPhrase(textForParsing)
                    : textForParsing;
            }

            var duration = (endTime - startTime).TotalSeconds;

            _brollPromptItems.Add(new BrollPromptItem
            {
                Index = idx++,
                Timestamp = timestamp,
                ScriptText = textForParsing,
                TextOverlay = overlay,
                MediaType = overlay != null ? BrollMediaType.BrollVideo : BrollMediaType.ImageGeneration,
                EstimatedDurationSeconds = duration,
                StartTimeSeconds = startTime.TotalSeconds,
                EndTimeSeconds = endTime.TotalSeconds
            });
        }

        // 5. Save SRT metadata for change detection
        var (entryCount, totalDuration) = ComputeSrtFingerprint();
        var metadata = new BrollSessionMetadata
        {
            SrtEntryCount = entryCount,
            SrtTotalDuration = totalDuration,
            SrtFilePath = _srtPath, // _srtPath is available based on context, let's verify. Or _srtFilePath depending on properties
            GeneratedAt = DateTime.UtcNow
        };
        await BrollPersistence.SaveBrollMetadata(metadata, _resultSession, _sessionId);
        _storedMetadata = metadata; // Cache in memory

        // 6. Save to disk
        await SaveBrollPromptsToDisk();
        await SaveImageConfigToDisk();

        _classifyTotalSegments = _brollPromptItems.Count;
        _classifyCompletedSegments = _brollPromptItems.Count;
        StateHasChanged();
    }

    /// <summary>
    /// Computes a lightweight fingerprint of the current SRT structure.
    /// Used to detect if SRT has changed since B-Roll prompts were generated.
    /// </summary>
    private (int count, double duration) ComputeSrtFingerprint()
    {
        if (_expandedEntries == null || _expandedEntries.Count == 0)
            return (0, 0);

        var totalDuration = _expandedEntries
            .Sum(e => (e.EndTime - e.StartTime).TotalSeconds);

        return (_expandedEntries.Count, totalDuration);
    }

    // ParseScriptToBrollItemsAsync removed — Step 3 now only receives data from Step 2's expanded SRT via ResetAndInitializeBrollFromSrt()

    private List<string> SplitTextIntoSegments(string text, int targetDurationSeconds)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        int targetWords = (int)(targetDurationSeconds * 2.33);
        if (targetWords < 10) targetWords = 10;
        
        var sentences = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?\n])\s+");
        var currentSegment = new System.Text.StringBuilder();
        int currentWordCount = 0;

        foreach (var sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence)) continue;
            var wordCount = sentence.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            
            if (currentWordCount > 0 && currentWordCount + wordCount > targetWords)
            {
                result.Add(currentSegment.ToString().Trim());
                currentSegment.Clear();
                currentWordCount = 0;
            }
            
            currentSegment.Append(sentence).Append(" ");
            currentWordCount += wordCount;
        }

        if (currentSegment.Length > 0)
        {
            result.Add(currentSegment.ToString().Trim());
        }

        return result;
    }

    private bool _isDownloadingAllVideos = false;
    private bool _isFilteringAllVideos = false;

    // ===== Classification (kept in component — tightly coupled to UI state) =====

    private async Task RunClassifyOnly()
    {
        if (_resultSession == null) return;

        _isClassifyingBroll = true;
        _classifyError = null;
        _classifyTotalSegments = 0;
        _classifyCompletedSegments = 0;
        StateHasChanged();

        try
        {
            if (_brollPromptItems.Count == 0)
            {
                _classifyError = "Tidak ada text yang bisa diklasifikasikan dari script.";
                _isClassifyingBroll = false;
                StateHasChanged();
                return;
            }

            _classifyTotalSegments = _brollPromptItems.Count;
            await InvokeAsync(StateHasChanged);

            foreach (var item in _brollPromptItems)
            {
                item.MediaType = item.TextOverlay != null ? BrollMediaType.BrollVideo : BrollMediaType.ImageGeneration;
                item.Prompt = string.Empty;
            }

            _classifyCompletedSegments = _brollPromptItems.Count;
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            _classifyError = $"Gagal mengklasifikasi: {ex.Message}";
        }
        finally
        {
            _isClassifyingBroll = false;
            StateHasChanged();
        }

        await SaveBrollPromptsToDisk();
        await SaveImageConfigToDisk();
    }

    // ===== Prompt Generation (kept — tightly coupled to UI progress) =====

    private async Task RunGenerateImagePrompts()
    {
        if (_resultSession == null || _brollPromptItems.Count == 0) return;

        var imageItems = _brollPromptItems.Where(i => i.MediaType == BrollMediaType.ImageGeneration).ToList();
        if (imageItems.Count == 0) return;

        _isGeneratingImagePrompts = true;
        _imagePromptTotalCount = imageItems.Count;
        _imagePromptGeneratedCount = 0;
        StateHasChanged();

        try
        {
            if (_globalContext == null || _globalContext.Topic != _resultSession.Topic)
            {
                _classifyError = null;
                StateHasChanged();
                _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
                    _brollPromptItems, _resultSession.Topic);
                if (_globalContext != null) await SaveGlobalContextToDisk();
            }

            if (_globalContext != null)
            {
                await IntelligenceService.GeneratePromptsWithContextAsync(
                    _brollPromptItems, BrollMediaType.ImageGeneration,
                    _resultSession.Topic, _globalContext, _imagePromptConfig,
                    onProgress: async count =>
                    {
                        _imagePromptGeneratedCount = count;
                        await InvokeAsync(StateHasChanged);
                        await SaveBrollPromptsToDisk();
                    },
                    windowSize: 2);
            }
            else
            {
                await IntelligenceService.GeneratePromptsForTypeBatchAsync(
                    _brollPromptItems, BrollMediaType.ImageGeneration,
                    _resultSession.Topic, _imagePromptConfig,
                    onProgress: async count =>
                    {
                        _imagePromptGeneratedCount = count;
                        await InvokeAsync(StateHasChanged);
                        await SaveBrollPromptsToDisk();
                    });
            }
        }
        catch (Exception ex)
        {
            _classifyError = $"Gagal generate image prompts: {ex.Message}";
        }
        finally
        {
            _isGeneratingImagePrompts = false;
            StateHasChanged();
        }

        await SaveBrollPromptsToDisk();
        await SaveImageConfigToDisk();
    }

    private async Task RunResumeImagePrompts()
    {
        if (_resultSession == null || _brollPromptItems.Count == 0) return;

        var emptyPromptItems = _brollPromptItems
            .Where(i => i.MediaType == BrollMediaType.ImageGeneration && string.IsNullOrWhiteSpace(i.Prompt))
            .ToList();
        if (emptyPromptItems.Count == 0) return;

        _isGeneratingImagePrompts = true;
        _imagePromptTotalCount = emptyPromptItems.Count;
        _imagePromptGeneratedCount = 0;
        StateHasChanged();

        try
        {
            if (_globalContext == null || _globalContext.Topic != _resultSession.Topic)
            {
                _classifyError = null;
                StateHasChanged();
                _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
                    _brollPromptItems, _resultSession.Topic);
                if (_globalContext != null) await SaveGlobalContextToDisk();
            }

            if (_globalContext != null)
            {
                await IntelligenceService.GeneratePromptsWithContextAsync(
                    _brollPromptItems, BrollMediaType.ImageGeneration,
                    _resultSession.Topic, _globalContext, _imagePromptConfig,
                    onProgress: async count =>
                    {
                        _imagePromptGeneratedCount = count;
                        await InvokeAsync(StateHasChanged);
                        await SaveBrollPromptsToDisk();
                    },
                    windowSize: 2, resumeOnly: true);
            }
            else
            {
                await IntelligenceService.GeneratePromptsForTypeBatchAsync(
                    _brollPromptItems, BrollMediaType.ImageGeneration,
                    _resultSession.Topic, _imagePromptConfig,
                    onProgress: async count =>
                    {
                        _imagePromptGeneratedCount = count;
                        await InvokeAsync(StateHasChanged);
                        await SaveBrollPromptsToDisk();
                    },
                    resumeOnly: true);
            }
        }
        catch (Exception ex)
        {
            _classifyError = $"Resume gagal: {ex.Message}";
        }
        finally
        {
            _isGeneratingImagePrompts = false;
            StateHasChanged();
        }

        await SaveBrollPromptsToDisk();
        await SaveImageConfigToDisk();
    }

    private async Task RunGenerateKeywords()
    {
        if (_resultSession == null || _brollPromptItems.Count == 0) return;

        var brollItems = _brollPromptItems.Where(i => i.MediaType == BrollMediaType.BrollVideo).ToList();
        if (brollItems.Count == 0) return;

        _isGeneratingKeywords = true;
        _keywordTotalCount = brollItems.Count;
        _keywordGeneratedCount = 0;
        StateHasChanged();

        try
        {
            if (_globalContext == null || _globalContext.Topic != _resultSession.Topic)
            {
                _classifyError = null;
                StateHasChanged();
                _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
                    _brollPromptItems, _resultSession.Topic);
                if (_globalContext != null) await SaveGlobalContextToDisk();
            }

            if (_globalContext != null)
            {
                await IntelligenceService.GeneratePromptsWithContextAsync(
                    _brollPromptItems, BrollMediaType.BrollVideo,
                    _resultSession.Topic, _globalContext, _imagePromptConfig,
                    onProgress: async count =>
                    {
                        _keywordGeneratedCount = count;
                        await InvokeAsync(StateHasChanged);
                    },
                    windowSize: 2);
            }
            else
            {
                await IntelligenceService.GeneratePromptsForTypeBatchAsync(
                    _brollPromptItems, BrollMediaType.BrollVideo,
                    _resultSession.Topic, _imagePromptConfig,
                    onProgress: async count =>
                    {
                        _keywordGeneratedCount = count;
                        await InvokeAsync(StateHasChanged);
                    });
            }
        }
        catch (Exception ex)
        {
            _classifyError = $"Gagal generate keywords: {ex.Message}";
        }
        finally
        {
            _isGeneratingKeywords = false;
            StateHasChanged();
        }

        await SaveBrollPromptsToDisk();

        if (_brollPromptItems.Any(i => i.MediaType == BrollMediaType.BrollVideo && !string.IsNullOrEmpty(i.Prompt)))
        {
            await SearchBrollForAllSegmentsAsync();
        }
    }

    // ===== Reclassification & Item Handlers =====

    private async Task HandleReclassifyBroll()
    {
        if (_brollPromptItems.Count == 0) return;

        // Re-initialize from expanded SRT (same as proceeding from Step 2)
        await ResetAndInitializeBrollFromSrt();
    }

    private async Task HandleSelectVideo((BrollPromptItem item, VideoAsset video) args)
    {
        args.item.SelectedVideoUrl = args.video.DownloadUrl;
        await SaveBrollPromptsToDisk();
        StateHasChanged();
    }

    // ===== Search (delegates to BrollVideoService) =====

    private async Task SearchBrollForAllSegmentsAsync()
    {
        _isSearchingBroll = true;
        StateHasChanged();

        var brollItems = _brollPromptItems.Where(i => i.MediaType == BrollMediaType.BrollVideo).ToList();
        foreach (var item in brollItems)
        {
            await BrollVideo.SearchBrollForSegmentAsync(item, AssetBroker, forceRefresh: true);
            StateHasChanged();
        }

        _isSearchingBroll = false;
        StateHasChanged();
    }

    private async Task AutoSearchMissingBrollSegments()
    {
        var missingItems = _brollPromptItems
            .Where(i => i.MediaType == BrollMediaType.BrollVideo 
                     && (i.AllSearchResults == null || i.AllSearchResults.Count == 0)
                     && !i.IsSearching)
            .ToList();

        if (missingItems.Count == 0) return;

        await InvokeAsync(() =>
        {
            _isSearchingBroll = true;
            StateHasChanged();
        });

        foreach (var item in missingItems)
        {
            await InvokeAsync(async () =>
            {
                await BrollVideo.SearchBrollForSegmentAsync(item, AssetBroker, forceRefresh: true);
                StateHasChanged();
            });
            await Task.Delay(500);
        }

        await InvokeAsync(() =>
        {
            _isSearchingBroll = false;
            StateHasChanged();
        });
    }

    private async Task HandleSearchSingleSegment(BrollPromptItem item)
    {
        await BrollVideo.SearchBrollForSegmentAsync(item, AssetBroker);
        StateHasChanged();
    }

    private async Task HandleToggleMediaType(BrollPromptItem item)
    {
        if (_resultSession == null) return;

        var newType = item.MediaType == BrollMediaType.BrollVideo ? BrollMediaType.ImageGeneration : BrollMediaType.BrollVideo;
        item.MediaType = newType;
        item.SearchResults.Clear();
        item.SearchError = null;
        item.SelectedVideoUrl = null;
        item.WhiskStatus = WhiskGenerationStatus.Pending;
        item.WhiskImagePath = null;
        item.WhiskError = null;
        item.IsSearching = true;
        StateHasChanged();

        try
        {
            var singleResult = await IntelligenceService.ClassifyAndGeneratePromptsAsync(
                new List<(string, string, TextOverlay?)> { (item.Timestamp, item.ScriptText, item.TextOverlay) },
                _resultSession.Topic);

            if (singleResult.Count > 0)
            {
                item.Prompt = singleResult[0].Prompt;
                item.Reasoning = singleResult[0].Reasoning;
                item.MediaType = newType;
            }

            if (newType == BrollMediaType.BrollVideo)
            {
                await BrollVideo.SearchBrollForSegmentAsync(item, AssetBroker, forceRefresh: true);
            }
        }
        catch (Exception ex)
        {
            item.SearchError = $"Gagal generate prompt: {ex.Message}";
        }
        finally
        {
            item.IsSearching = false;
            await SaveBrollPromptsToDisk();
            StateHasChanged();
        }
    }

    private async Task HandleRegenerateGlobalContext()
    {
        if (_resultSession == null || _brollPromptItems.Count == 0) return;

        _classifyError = null;
        StateHasChanged();

        try
        {
            _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
                _brollPromptItems, _resultSession.Topic);

            if (_globalContext == null)
                _classifyError = "Failed to extract global context. Please try again.";
            else
                await SaveGlobalContextToDisk();
        }
        catch (Exception ex)
        {
            _classifyError = $"Context extraction failed: {ex.Message}";
        }
        finally
        {
            StateHasChanged();
        }
    }

    private async Task HandleRegenSegmentKeywords(BrollPromptItem item)
    {
        if (_resultSession == null) return;

        item.IsSearching = true;
        StateHasChanged();

        try
        {
            string? brollKeywords;

            if (_globalContext == null || _globalContext.Topic != _resultSession.Topic)
            {
                _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
                    _brollPromptItems, _resultSession.Topic);
                if (_globalContext != null) await SaveGlobalContextToDisk();
            }

            if (_globalContext != null)
            {
                brollKeywords = await IntelligenceService.GeneratePromptWithContextAsync(
                    item, _brollPromptItems, _resultSession.Topic,
                    _globalContext, _imagePromptConfig, windowSize: 2);
            }
            else
            {
                brollKeywords = await IntelligenceService.GeneratePromptForTypeAsync(
                    item.ScriptText, BrollMediaType.BrollVideo, _resultSession.Topic, config: _imagePromptConfig);
            }

            if (!string.IsNullOrWhiteSpace(brollKeywords))
            {
                item.Prompt = brollKeywords;
                item.Reasoning = "Regenerated keywords" + (_globalContext != null ? " (with context)" : "");
            }
            else
            {
                throw new Exception("AI failed to generate keywords. Please try again.");
            }

            if (item.MediaType == BrollMediaType.BrollVideo)
            {
                await BrollVideo.SearchBrollForSegmentAsync(item, AssetBroker, forceRefresh: true);
            }
        }
        catch (Exception ex)
        {
            item.SearchError = $"Regen gagal: {ex.Message}";
        }
        finally
        {
            item.IsSearching = false;
            StateHasChanged();
        }
    }

    // ===== Cookie Modal =====

    private void HandleCookieUpdateRequested(BrollPromptItem item)
    {
        _cookieUpdateTargetItem = item;
        _newWhiskCookie = "";
        _showCookieModal = true;
    }

    private void CloseCookieModal()
    {
        _showCookieModal = false;
        _newWhiskCookie = "";
        _cookieUpdateTargetItem = null;
    }

    private void SubmitCookieUpdate()
    {
        if (string.IsNullOrWhiteSpace(_newWhiskCookie)) return;

        WhiskGenerator.UpdateCookie(_newWhiskCookie);
        
        var target = _cookieUpdateTargetItem;
        CloseCookieModal();

        if (target != null)
        {
            _ = Task.Run(async () =>
            {
                await InvokeAsync(() =>
                {
                    target.IsGenerating = true;
                    StateHasChanged();
                });

                await BrollImage.GenerateWhiskImageForItem(target, WhiskGenerator, _resultSession?.OutputDirectory, _sessionId);

                await InvokeAsync(() =>
                {
                    target.IsGenerating = false;
                    StateHasChanged();
                    _ = SaveBrollPromptsToDisk();
                });
            });
        }
    }

    // ===== Bulk Keyword Regeneration =====

    private async Task HandleRegenAllVideoKeywords()
    {
        if (_resultSession == null) return;

        var brollItems = _brollPromptItems.Where(i => i.MediaType == BrollMediaType.BrollVideo).ToList();
        if (brollItems.Count == 0) return;

        _isRegeneratingAllKeywords = true;
        StateHasChanged();

        try
        {
            var segments = brollItems.Select(i => (i.Timestamp, i.ScriptText, i.TextOverlay)).ToList();

            var results = await IntelligenceService.ClassifyAndGeneratePromptsAsync(
                segments, _resultSession.Topic,
                onBatchComplete: async batchResults =>
                {
                    foreach (var result in batchResults)
                    {
                        var matchingItem = brollItems.ElementAtOrDefault(result.Index);
                        if (matchingItem != null)
                        {
                            matchingItem.Prompt = result.Prompt;
                            matchingItem.Reasoning = result.Reasoning;
                            matchingItem.AllSearchResults.Clear();
                            matchingItem.SearchResults.Clear();
                        }
                    }
                    _classifyCompletedSegments = batchResults.Count;
                    await InvokeAsync(StateHasChanged);
                });

            for (int i = 0; i < results.Count && i < brollItems.Count; i++)
            {
                var item = brollItems[i];
                var result = results[i];
                
                item.Prompt = result.Prompt;
                item.Reasoning = result.Reasoning;
                
                if (item.MediaType != result.MediaType)
                {
                    item.MediaType = result.MediaType;
                    
                    if (item.MediaType == BrollMediaType.ImageGeneration)
                    {
                        item.WhiskStatus = WhiskGenerationStatus.Pending;
                        item.WhiskImagePath = null;
                        item.WhiskError = null;
                        item.WhiskVideoStatus = WhiskGenerationStatus.Pending;
                        item.WhiskVideoPath = null;
                        item.WhiskVideoError = null;
                        item.IsGenerating = false;
                        item.IsConvertingVideo = false;
                        item.KenBurnsMotion = BrollPromptItem.GetRandomMotion();
                    }
                }

                item.AllSearchResults.Clear();
                item.SearchResults.Clear();
            }

            await SaveBrollPromptsToDisk();
            await SearchBrollForAllSegmentsAsync();
        }
        catch (Exception ex)
        {
            _classifyError = $"Regen all keywords gagal: {ex.Message}";
        }
        finally
        {
            _isRegeneratingAllKeywords = false;
            StateHasChanged();
        }
    }

    // ===== Prompt & Image Regen (delegates to BrollImageService) =====

    private async Task HandleRegenPromptOnly(BrollPromptItem item)
    {
        if (_resultSession == null) return;

        item.IsGenerating = true;
        item.CombinedRegenProgress = 10;
        StateHasChanged();

        try
        {
            item.CombinedRegenProgress = 30;
            StateHasChanged();

            string? imagePrompt;

            if (_globalContext == null || _globalContext.Topic != _resultSession.Topic)
            {
                item.CombinedRegenProgress = 40;
                StateHasChanged();
                _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
                    _brollPromptItems, _resultSession.Topic);
                if (_globalContext != null) await SaveGlobalContextToDisk();
            }

            item.CombinedRegenProgress = 60;
            StateHasChanged();

            if (_globalContext != null)
            {
                imagePrompt = await IntelligenceService.GeneratePromptWithContextAsync(
                    item, _brollPromptItems, _resultSession.Topic,
                    _globalContext, _imagePromptConfig, windowSize: 2);
            }
            else
            {
                imagePrompt = await IntelligenceService.GeneratePromptForTypeAsync(
                    item.ScriptText, BrollMediaType.ImageGeneration, _resultSession.Topic, config: _imagePromptConfig);
            }

            if (!string.IsNullOrWhiteSpace(imagePrompt))
            {
                item.Prompt = imagePrompt;
                item.Reasoning = "Regenerated image prompt" + (_globalContext != null ? " (with context)" : "");
                item.KenBurnsMotion = BrollPromptItem.GetRandomMotion();
                item.CombinedRegenProgress = 100;
            }
            else
            {
                throw new Exception("AI disconnected or failed to generate a new prompt. Please try again or check logs.");
            }
        }
        catch (Exception ex)
        {
            item.WhiskError = $"Regen Prompt Failed: {ex.Message}";
        }
        finally
        {
            item.CombinedRegenProgress = 0;
            item.IsGenerating = false;
            StateHasChanged();
        }
    }

    private async Task HandleRegenImageOnly(BrollPromptItem item)
    {
        if (_resultSession == null || string.IsNullOrWhiteSpace(item.Prompt)) return;

        item.IsGenerating = true;
        item.CombinedRegenProgress = 10;
        item.WhiskError = null;
        item.WhiskVideoStatus = WhiskGenerationStatus.Pending;
        item.WhiskVideoPath = null;
        item.WhiskVideoError = null;
        StateHasChanged();

        try
        {
            item.CombinedRegenProgress = 50;
            StateHasChanged();

            await BrollImage.GenerateWhiskImageForItem(item, WhiskGenerator, _resultSession?.OutputDirectory, _sessionId);
            item.CombinedRegenProgress = 100;
        }
        catch (Exception ex)
        {
            item.WhiskError = $"Generate Image Failed: {ex.Message}";
        }
        finally
        {
            await SaveBrollPromptsToDisk();
            item.CombinedRegenProgress = 0;
            item.IsGenerating = false;
            StateHasChanged();
        }
    }

    // ===== Bulk Image Generation (delegates to BrollImageService) =====

    private async Task HandleGenerateAllWhiskImages()
    {
        var imageGenItems = _brollPromptItems
            .Where(i => i.MediaType == BrollMediaType.ImageGeneration
                     && i.WhiskStatus != WhiskGenerationStatus.Done
                     && !string.IsNullOrWhiteSpace(i.Prompt))
            .OrderBy(i => i.Index)
            .ToList();

        if (imageGenItems.Count == 0) return;

        _isGeneratingWhisk = true;
        _whiskTotalCount = imageGenItems.Count;
        _whiskGeneratedCount = 0;
        StateHasChanged();

        foreach (var item in imageGenItems)
        {
            Console.WriteLine($"[WHISK] Generating image for segment #{item.Index} (prefix: seg-{item.Index:D3})");
            Console.WriteLine($"[WHISK] Prompt: {item.Prompt[..Math.Min(80, item.Prompt.Length)]}...");

            item.IsGenerating = true;
            StateHasChanged();

            try
            {
                await BrollImage.GenerateWhiskImageForItem(item, WhiskGenerator, _resultSession?.OutputDirectory, _sessionId);
                Console.WriteLine($"[WHISK] Segment #{item.Index} => {item.WhiskImagePath ?? "FAILED"} (Status: {item.WhiskStatus})");
            }
            finally
            {
                item.IsGenerating = false;
                _whiskGeneratedCount++;
                StateHasChanged();

                if (item.WhiskStatus == WhiskGenerationStatus.Done)
                {
                    await SaveBrollPromptsToDisk();
                }
            }
        }

        _isGeneratingWhisk = false;
        await SaveBrollPromptsToDisk();
        StateHasChanged();
    }

    // ===== Ken Burns Video (delegates to BrollImageService) =====

    private async Task HandleGenerateKenBurnsVideo(BrollPromptItem item)
    {
        await BrollImage.GenerateKenBurnsVideo(item, KenBurnsService, () => StateHasChanged());
        await SaveBrollPromptsToDisk();

        if (item.WhiskVideoStatus == WhiskGenerationStatus.Done && item.HasVisualEffect)
        {
            await Task.Delay(50);
            await HandleApplyFilterToVideo(item);
        }
    }

    // ===== Clear/Reset Handlers =====

    private async Task HandleClearAllPrompts()
    {
        foreach (var item in _brollPromptItems.Where(i => i.MediaType == BrollMediaType.ImageGeneration))
        {
            item.Prompt = string.Empty;
        }

        await SaveBrollPromptsToDisk();
        StateHasChanged();
    }

    private async Task HandleResetAllImageStates()
    {
        foreach (var item in _brollPromptItems.Where(i => i.MediaType == BrollMediaType.ImageGeneration))
        {
            if (!string.IsNullOrEmpty(item.WhiskImagePath) && System.IO.File.Exists(item.WhiskImagePath))
            {
                try { System.IO.File.Delete(item.WhiskImagePath); } catch { }
                try { if (item.WhiskVideoPath != null) System.IO.File.Delete(item.WhiskVideoPath); } catch { }
            }

            item.WhiskStatus = WhiskGenerationStatus.Pending;
            item.WhiskImagePath = null;
            item.WhiskError = null;
            item.WhiskVideoStatus = WhiskGenerationStatus.Pending;
            item.WhiskVideoPath = null;
            item.WhiskVideoError = null;
            item.IsGenerating = false;
            item.IsConvertingVideo = false;
        }

        await SaveBrollPromptsToDisk();
        StateHasChanged();
    }

    private async Task HandleResetAllVideoStates()
    {
        foreach (var item in _brollPromptItems.Where(i => i.MediaType == BrollMediaType.ImageGeneration))
        {
            if (!string.IsNullOrEmpty(item.WhiskVideoPath) && System.IO.File.Exists(item.WhiskVideoPath))
            {
                try { System.IO.File.Delete(item.WhiskVideoPath); } catch { }
            }

            item.WhiskVideoStatus = WhiskGenerationStatus.Pending;
            item.WhiskVideoPath = null;
            item.WhiskVideoError = null;
            item.IsConvertingVideo = false;
        }

        await SaveBrollPromptsToDisk();
        StateHasChanged();
    }

    private async Task HandleResetVideoStateForSegment(BrollPromptItem item)
    {
        if (!string.IsNullOrEmpty(item.WhiskVideoPath) && System.IO.File.Exists(item.WhiskVideoPath))
        {
            try { System.IO.File.Delete(item.WhiskVideoPath); } catch { }
        }

        item.WhiskVideoStatus = WhiskGenerationStatus.Pending;
        item.WhiskVideoPath = null;
        item.WhiskVideoError = null;
        item.IsConvertingVideo = false;
        item.FilteredVideoPath = null;

        await SaveBrollPromptsToDisk();
        StateHasChanged();
    }

    private async Task HandleGenerateAllKenBurnsVideos()
    {
        var pending = _brollPromptItems
            .Where(i => i.MediaType == BrollMediaType.ImageGeneration && 
                        i.WhiskStatus == WhiskGenerationStatus.Done && 
                        i.WhiskVideoStatus != WhiskGenerationStatus.Done &&
                        !i.IsConvertingVideo)
            .ToList();

        if (pending.Count == 0) return;

        foreach (var item in pending)
        {
            await HandleGenerateKenBurnsVideo(item);
        }
    }

    // ===== Persistence (delegates to BrollPersistenceService) =====

    private void InvalidateBrollClassification()
    {
        BrollPersistence.InvalidateBrollClassification(_brollPromptItems, _resultSession, _sessionId);
        _brollPromptItems.Clear();
        _classifyTotalSegments = 0;
        _classifyCompletedSegments = 0;
    }

    private async Task SaveBrollPromptsToDisk()
    {
        try
        {
            await BrollPersistence.SaveBrollPromptsToDisk(_brollPromptItems, _resultSession, _sessionId);
        }
        catch (Exception ex)
        {
            _saveError = $"Gagal menyimpan broll prompts: {ex.Message}";
        }
    }

    private async Task LoadBrollPromptsFromDisk()
    {
        _brollPromptItems = await BrollPersistence.LoadBrollPromptsFromDisk(_resultSession, _sessionId);
        if (_brollPromptItems.Count > 0)
        {
            _canProceedToStep3 = true;
            
            // Backfill Index and Time info for older saved sessions
            bool hasOldData = false;
            for (int i = 0; i < _brollPromptItems.Count; i++)
            {
                var item = _brollPromptItems[i];
                // Fix 1-based indexing from older saves to 0-based
                if (item.Index == i + 1)
                {
                    item.Index = i;
                }
                
                // Track missing time data for older saved projects
                if (item.EstimatedDurationSeconds == 0 && item.EndTimeSeconds == 0 && !string.IsNullOrWhiteSpace(item.Timestamp))
                {
                    hasOldData = true;
                }
            }
            
            if (hasOldData)
            {
                _brollWarning = "Project versi lama (waktu video 0s) terdeteksi. Silakan kembali ke Step 2 dan lakukan 'Looks Good, Proceed' ulang untuk kalkulasi durasi yang akurat dari SRT.";
            }
            else
            {
                _brollWarning = null;
            }
        }
        else
        {
            _brollWarning = null;
        }
    }

    private async Task LoadImageConfigFromDisk()
    {
        _imagePromptConfig = await BrollPersistence.LoadImageConfigFromDisk(_resultSession, _sessionId);
    }

    private async Task SaveImageConfigToDisk()
    {
        await BrollPersistence.SaveImageConfigToDisk(_imagePromptConfig, _resultSession, _sessionId);
    }

    private async Task SaveGlobalContextToDisk()
    {
        if (_globalContext != null)
            await BrollPersistence.SaveGlobalContextToDisk(_globalContext, _resultSession, _sessionId);
    }

    private async Task LoadGlobalContextFromDisk()
    {
        _globalContext = await BrollPersistence.LoadGlobalContextFromDisk(_resultSession, _sessionId);
    }

    private async Task HandleDeleteBrollCache()
    {
        await BrollPersistence.HandleDeleteBrollCache();
    }

    // ===== Video Download (delegates to BrollVideoService) =====

    private async Task HandleDownloadVideo((BrollPromptItem Item, VideoAsset Video) args)
    {
        await BrollVideo.DownloadVideoAsync(args.Item, args.Video, DownloaderService, _resultSession?.OutputDirectory, _sessionId);
        await SaveBrollPromptsToDisk();
        StateHasChanged();
    }

    private async Task HandleDownloadAllVideos()
    {
        if (_isDownloadingAllVideos) return;

        try
        {
            _isDownloadingAllVideos = true;
            StateHasChanged();

            await BrollVideo.DownloadAllVideosAsync(_brollPromptItems, DownloaderService, _resultSession?.OutputDirectory, _sessionId, () => StateHasChanged());
            await SaveBrollPromptsToDisk();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in download all: {ex.Message}");
        }
        finally
        {
            _isDownloadingAllVideos = false;
            StateHasChanged();
        }
    }

    // ===== Filter (delegates to BrollVideoService) =====

    private async Task HandleApplyFilterToVideo(BrollPromptItem item)
    {
        await BrollVideo.ApplyFilterToVideoAsync(item, VideoComposer, DownloaderService, _resultSession?.OutputDirectory, _sessionId, () => StateHasChanged());
        await SaveBrollPromptsToDisk();
    }

    private async Task HandleApplyFilterAllVideos()
    {
        if (_isFilteringAllVideos) return;

        try
        {
            _isFilteringAllVideos = true;
            StateHasChanged();

            await BrollVideo.ApplyFilterAllVideosAsync(_brollPromptItems, VideoComposer, DownloaderService, _resultSession?.OutputDirectory, _sessionId, () => StateHasChanged());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error applying all filters: {ex.Message}");
        }
        finally
        {
            _isFilteringAllVideos = false;
            StateHasChanged();
        }
    }

    // ===== Path Resolution Utilities =====

    private string GetAssetUrl(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return "";
        if (absolutePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return absolutePath;

        var resolved = ResolveLocalPath(absolutePath);
        if (string.IsNullOrEmpty(resolved)) return "";

        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
        try
        {
            var relative = Path.GetRelativePath(baseDir, resolved).Replace("\\", "/");
            return $"/project-assets/{relative}";
        }
        catch
        {
            var normalized = resolved.Replace("\\", "/");
            var markerIndex = normalized.IndexOf("output/", StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                return $"/project-assets/{normalized.Substring(markerIndex + "output/".Length)}";
            }
            return absolutePath;
        }
    }

    private string ResolveLocalPath(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return "";
        
        var normalized = absolutePath.Replace("\\", "/");
        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
        
        string relative = "";
        var markerIndex = normalized.IndexOf("output/", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            relative = normalized.Substring(markerIndex + "output/".Length);
        }
        else
        {
            try { relative = Path.GetRelativePath(baseDir, absolutePath); } catch { relative = absolutePath; }
        }

        if (relative.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = relative.Split('/');
            if (parts.Length > 2 && parts[1].Length == 8)
            {
                relative = string.Join("/", parts.Skip(1));
            }
        }

        return Path.Combine(baseDir, relative.Replace("/", Path.DirectorySeparatorChar.ToString()));
    }

    // ===== B-Roll Direct Merging =====
    
    private bool _isMergingBrollVideo;
    private string? _mergeBrollProgress;
    private string? _mergedBrollPath;

    private async Task HandleMergeBrollPreview() => await ComposeBrollVideo(isPreview: true);
    private async Task HandleMergeBrollReal() => await ComposeBrollVideo(isPreview: false);

    private async Task ComposeBrollVideo(bool isPreview)
    {
        if (_brollPromptItems.Count == 0 || _resultSession == null) return;
        
        _isMergingBrollVideo = true;
        _mergeBrollProgress = "Starting composition...";
        _mergedBrollPath = null;
        StateHasChanged();

        try
        {
            // Auto detect VO and SRT so they can be included if available
            AutoDetectVoAndSrt();

            var config = new BunbunBroll.Models.VideoConfig
            {
                CapCutAudioPath = _voPath,
                CapCutSrtPath = _srtPath,
                IsDraftPreview = isPreview
            };

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
                        AssociatedText = item.ScriptText,
                        TextOverlay = item.TextOverlay
                    });
                }
                else if (item.MediaType == BunbunBroll.Models.BrollMediaType.ImageGeneration)
                {
                     if (!string.IsNullOrEmpty(item.FilteredVideoPath))
                     {
                          clips.Add(new BunbunBroll.Models.VideoClip 
                          { 
                              SourcePath = item.FilteredVideoPath,
                              AssociatedText = item.ScriptText,
                              TextOverlay = item.TextOverlay
                          });
                     }
                     else if (!string.IsNullOrEmpty(item.WhiskVideoPath))
                     {
                          clips.Add(new BunbunBroll.Models.VideoClip 
                          { 
                              SourcePath = item.WhiskVideoPath,
                              AssociatedText = item.ScriptText,
                              TextOverlay = item.TextOverlay
                          });
                     }
                     else if (!string.IsNullOrEmpty(item.WhiskImagePath))
                     {
                          clips.Add(BunbunBroll.Models.VideoClip.FromImage(item.WhiskImagePath, item.ScriptText, 3.0, textOverlay: item.TextOverlay));
                     }
                }
            }

            var progressReporter = new Progress<BunbunBroll.Models.CompositionProgress>(p =>
            {
                _ = InvokeAsync(() =>
                {
                    _mergeBrollProgress = $"[{p.Percent}%] {p.Stage}: {p.Message}";
                    StateHasChanged();
                });
            });

            var result = await VideoComposer.ComposeAsync(clips, config, _sessionId, progressReporter, CancellationToken.None);

            if (result.Success)
            {
                _mergedBrollPath = result.OutputPath;
                _mergeBrollProgress = "Video composed successfully!";
            }
            else
            {
                _classifyError = result.ErrorMessage; // show in Broll Prompts view
                _mergeBrollProgress = null;
            }
        }
        catch (Exception ex)
        {
            _classifyError = $"Composition failed: {ex.Message}";
            _mergeBrollProgress = null;
        }
        finally
        {
            _isMergingBrollVideo = false;
            StateHasChanged();
        }
    }

    // ===== Confirmation Dialog =====

    private void RequestConfirmation(string title, string message, Func<Task> action)
    {
        _confirmTitle = title;
        _confirmMessage = message;
        _pendingConfirmAction = action;
        _showConfirmDialog = true;
        StateHasChanged();
    }

    private void CancelConfirm()
    {
        _showConfirmDialog = false;
        _pendingConfirmAction = null;
    }

    private async Task ExecuteConfirm()
    {
        _showConfirmDialog = false;
        if (_pendingConfirmAction != null)
        {
            await _pendingConfirmAction();
        }
        _pendingConfirmAction = null;
        StateHasChanged();
    }

}
