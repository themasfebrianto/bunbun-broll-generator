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

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{

    private async Task HandleSendToBroll()
    {
        if (_resultSession == null || _resultSections.Count == 0) return;

        if (_brollPromptItems.Count == 0)
        {
            await LoadBrollPromptsFromDisk();
            await LoadImageConfigFromDisk();
            await LoadGlobalContextFromDisk();
        }

        // If it's STILL empty (no file on disk), parse from the current script
        if (_brollPromptItems.Count == 0)
        {
            await ParseScriptToBrollItemsAsync();
            await RunClassifyOnly();
        }

        _currentView = "broll-prompts";
        StateHasChanged();
    }

    private async Task ParseScriptToBrollItemsAsync()
    {
        if (_resultSections.Count == 0) return;
        _brollPromptItems.Clear();

        var timestampPattern = new System.Text.RegularExpressions.Regex(@"\[(\d{1,3}):(\d{2})\]", System.Text.RegularExpressions.RegexOptions.Compiled);
        var globalOffset = TimeSpan.Zero;
        int idx = 1;

        foreach (var section in _resultSections.OrderBy(s => s.Order))
        {
            if (string.IsNullOrWhiteSpace(section.Content)) continue;
            var entries = ParseTimestampedEntries(section.Content, timestampPattern);

            if (entries.Count > 0)
            {
                var phaseBase = entries[0].Timestamp;
                for (int e = 0; e < entries.Count; e++)
                {
                    var entry = entries[e];
                    var normalizedTime = entry.Timestamp - phaseBase;
                    if (normalizedTime < TimeSpan.Zero) normalizedTime = TimeSpan.Zero;
                    var absoluteTime = globalOffset.Add(normalizedTime);
                    var cleaned = CleanSubtitleText(entry.Text);
                    if (string.IsNullOrWhiteSpace(cleaned)) continue;
                    
                    var segments = SplitTextIntoSegments(cleaned, 15);
                    var entryOffset = absoluteTime;

                    foreach (var segment in segments)
                    {
                        var mins = (int)entryOffset.TotalMinutes;
                        var secs = entryOffset.Seconds;
                        
                        string textForParsing = segment;
                        var overlay = ScriptProcessor.ExtractTextOverlay(ref textForParsing);
                        
                        if (overlay != null)
                        {
                            if (string.IsNullOrWhiteSpace(overlay.Text) && 
                                (overlay.Type == TextOverlayType.QuranVerse || overlay.Type == TextOverlayType.Hadith) &&
                                e + 1 < entries.Count)
                            {
                                var nextEntry = entries[e + 1];
                                var nextCleaned = CleanSubtitleText(nextEntry.Text);
                                if (!string.IsNullOrWhiteSpace(nextCleaned))
                                {
                                    overlay.Text = nextCleaned;
                                    textForParsing = nextCleaned;
                                    e++;
                                }
                            }
                            else if (string.IsNullOrWhiteSpace(overlay.Text) && overlay.Type == TextOverlayType.KeyPhrase)
                            {
                                overlay.Text = BunbunBroll.Services.ScriptProcessor.TruncateForKeyPhrase(textForParsing);
                            }
                            else if (string.IsNullOrWhiteSpace(overlay.Text))
                            {
                                overlay.Text = textForParsing;
                            }
                        }

                        var words = textForParsing.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        var duration = Math.Max(3.0, words / 2.5);
                        var startSeconds = entryOffset.TotalSeconds;
                        var endSeconds = entryOffset.TotalSeconds + duration;

                        _brollPromptItems.Add(new BrollPromptItem
                        {
                            Index = idx++,
                            Timestamp = $"[{mins:D2}:{secs:D2}]",
                            ScriptText = textForParsing,
                            TextOverlay = overlay,
                            MediaType = overlay != null ? BrollMediaType.BrollVideo : BrollMediaType.ImageGeneration,
                            EstimatedDurationSeconds = duration,
                            StartTimeSeconds = startSeconds,
                            EndTimeSeconds = endSeconds
                        });

                        entryOffset = entryOffset.Add(TimeSpan.FromSeconds(duration));
                    }
                }
                var lastEntry = entries.Last();
                var normalizedLastTime = lastEntry.Timestamp - phaseBase;
                if (normalizedLastTime < TimeSpan.Zero) normalizedLastTime = TimeSpan.Zero;
                var lastDuration = EstimateDuration(lastEntry.Text);
                globalOffset = globalOffset.Add(normalizedLastTime).Add(TimeSpan.FromSeconds(lastDuration));
            }
            else
            {
                var cleaned = CleanSubtitleText(section.Content);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    var segments = SplitTextIntoSegments(cleaned, 15);
                    foreach (var segment in segments)
                    {
                        var mins = (int)globalOffset.TotalMinutes;
                        var secs = globalOffset.Seconds;
                        
                        string textForParsing = segment;
                        var overlay = ScriptProcessor.ExtractTextOverlay(ref textForParsing);
                        if (overlay != null && string.IsNullOrWhiteSpace(overlay.Text))
                        {
                            overlay.Text = overlay.Type == TextOverlayType.KeyPhrase
                                ? BunbunBroll.Services.ScriptProcessor.TruncateForKeyPhrase(textForParsing)
                                : textForParsing;
                        }

                        var words = textForParsing.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        var duration = Math.Max(3.0, words / 2.5);
                        var startSeconds = globalOffset.TotalSeconds;
                        var endSeconds = globalOffset.TotalSeconds + duration;

                        _brollPromptItems.Add(new BrollPromptItem
                        {
                            Index = idx++,
                            Timestamp = $"[{mins:D2}:{secs:D2}]",
                            ScriptText = textForParsing,
                            TextOverlay = overlay,
                            MediaType = overlay != null ? BrollMediaType.BrollVideo : BrollMediaType.ImageGeneration,
                            EstimatedDurationSeconds = duration,
                            StartTimeSeconds = startSeconds,
                            EndTimeSeconds = endSeconds
                        });
                        
                        globalOffset = globalOffset.Add(TimeSpan.FromSeconds(duration));
                    }
                }
            }
        }
    }

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
                await ParseScriptToBrollItemsAsync();
            }

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

        // Delete associated media files via persistence service
        BrollPersistence.InvalidateBrollClassification(_brollPromptItems, _resultSession, _sessionId);

        // Re-parse from original script sections
        await ParseScriptToBrollItemsAsync();

        _classifyTotalSegments = _brollPromptItems.Count;
        _classifyCompletedSegments = _brollPromptItems.Count;

        await SaveBrollPromptsToDisk();
        await SaveImageConfigToDisk();
        StateHasChanged();
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

    private async Task SubmitCookieUpdate()
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
                try { System.IO.File.Delete(item.WhiskVideoPath); } catch { }
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