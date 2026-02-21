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

    private async Task HandleSendToBroll()
    {
        if (_resultSession == null || _resultSections.Count == 0) return;

        if (_brollPromptItems.Count == 0)
        {
            await LoadBrollPromptsFromDisk();
            await LoadImageConfigFromDisk();
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
                            // For QuranVerse/Hadith: if Text is empty, peek ahead to get translation from next entry
                            if (string.IsNullOrWhiteSpace(overlay.Text) && 
                                (overlay.Type == TextOverlayType.QuranVerse || overlay.Type == TextOverlayType.Hadith) &&
                                e + 1 < entries.Count)
                            {
                                var nextEntry = entries[e + 1];
                                var nextCleaned = CleanSubtitleText(nextEntry.Text);
                                if (!string.IsNullOrWhiteSpace(nextCleaned))
                                {
                                    overlay.Text = nextCleaned;
                                    textForParsing = nextCleaned; // Use translation as the narration text
                                    e++; // Skip the next entry since we absorbed it
                                }
                            }
                            // For KeyPhrase: if Text is still empty, truncate the narration for a punchy display
                            else if (string.IsNullOrWhiteSpace(overlay.Text) && overlay.Type == TextOverlayType.KeyPhrase)
                            {
                                overlay.Text = BunbunBroll.Services.ScriptProcessor.TruncateForKeyPhrase(textForParsing);
                            }
                            // For other types: fallback to full text if empty
                            else if (string.IsNullOrWhiteSpace(overlay.Text))
                            {
                                overlay.Text = textForParsing;
                            }
                        }

                        _brollPromptItems.Add(new BrollPromptItem
                        {
                            Index = idx++,
                            Timestamp = $"[{mins:D2}:{secs:D2}]",
                            ScriptText = textForParsing,
                            TextOverlay = overlay,
                            MediaType = overlay != null ? BrollMediaType.BrollVideo : BrollMediaType.ImageGeneration
                        });

                        entryOffset = entryOffset.Add(TimeSpan.FromSeconds(EstimateDuration(textForParsing)));
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

                        _brollPromptItems.Add(new BrollPromptItem
                        {
                            Index = idx++,
                            Timestamp = $"[{mins:D2}:{secs:D2}]",
                            ScriptText = textForParsing,
                            TextOverlay = overlay,
                            MediaType = overlay != null ? BrollMediaType.BrollVideo : BrollMediaType.ImageGeneration
                        });
                        
                        globalOffset = globalOffset.Add(TimeSpan.FromSeconds(EstimateDuration(textForParsing)));
                    }
                }
            }
        }
    }

    private List<string> SplitTextIntoSegments(string text, int targetDurationSeconds)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        int targetWords = (int)(targetDurationSeconds * 2.33); // ~ 35 words for 15 secs
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
            // Parse from script if empty
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

            // Deterministic classification (No LLM)
            foreach (var item in _brollPromptItems)
            {
                if (item.TextOverlay != null)
                {
                    item.MediaType = BrollMediaType.BrollVideo;
                }
                else
                {
                    item.MediaType = BrollMediaType.ImageGeneration;
                }
                
                // Clear prompts if any since this is a reclassification
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
            // Pass 1: Extract global context if needed
            if (_globalContext == null || _globalContext.Topic != _resultSession.Topic)
            {
                _classifyError = null;
                StateHasChanged();

                _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
                    _brollPromptItems, _resultSession.Topic);
            }

            if (_globalContext != null)
            {
                // Pass 2: Context-aware generation
                await IntelligenceService.GeneratePromptsWithContextAsync(
                    _brollPromptItems, BrollMediaType.ImageGeneration,
                    _resultSession.Topic, _globalContext, _imagePromptConfig,
                    onProgress: async count =>
                    {
                        _imagePromptGeneratedCount = count;
                        await InvokeAsync(StateHasChanged);
                    },
                    windowSize: 2);
            }
            else
            {
                // Fallback: non-context-aware generation
                await IntelligenceService.GeneratePromptsForTypeBatchAsync(
                    _brollPromptItems, BrollMediaType.ImageGeneration,
                    _resultSession.Topic, _imagePromptConfig,
                    onProgress: async count =>
                    {
                        _imagePromptGeneratedCount = count;
                        await InvokeAsync(StateHasChanged);
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
            // Pass 1: Extract global context if needed
            if (_globalContext == null || _globalContext.Topic != _resultSession.Topic)
            {
                _classifyError = null;
                StateHasChanged();

                _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
                    _brollPromptItems, _resultSession.Topic);
            }

            if (_globalContext != null)
            {
                // Pass 2: Context-aware generation
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
                // Fallback: non-context-aware generation
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

        // Auto-search for B-Roll videos after keywords are generated
        if (_brollPromptItems.Any(i => i.MediaType == BrollMediaType.BrollVideo && !string.IsNullOrEmpty(i.Prompt)))
        {
            await SearchBrollForAllSegmentsAsync();
        }
    }

    private async Task HandleReclassifyBroll()
    {
        if (_brollPromptItems.Count == 0) return;

        // Delete associated media files before clearing items
        foreach (var item in _brollPromptItems)
        {
            if (!string.IsNullOrEmpty(item.WhiskImagePath) && File.Exists(item.WhiskImagePath))
                try { File.Delete(item.WhiskImagePath); } catch { }
            if (!string.IsNullOrEmpty(item.WhiskVideoPath) && File.Exists(item.WhiskVideoPath))
                try { File.Delete(item.WhiskVideoPath); } catch { }
            if (!string.IsNullOrEmpty(item.FilteredVideoPath) && File.Exists(item.FilteredVideoPath))
                try { File.Delete(item.FilteredVideoPath); } catch { }
            foreach (var video in item.SearchResults)
            {
                if (!string.IsNullOrEmpty(video.LocalPath) && File.Exists(video.LocalPath))
                    try { File.Delete(video.LocalPath); } catch { }
            }
        }

        // Clear items and re-parse from original script sections.
        // This is critical because ScriptText has already been stripped of overlay tags
        // (e.g. [OVERLAY:QuranVerse], [ARABIC], [REF]) during the initial parse.
        // Re-parsing from _resultSections ensures ExtractTextOverlay correctly detects
        // overlay tags → BrollVideo, no overlay → ImageGeneration.
        _brollPromptItems.Clear();
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

    private async Task SearchBrollForAllSegmentsAsync()
    {
        _isSearchingBroll = true;
        StateHasChanged();

        var brollItems = _brollPromptItems.Where(i => i.MediaType == BrollMediaType.BrollVideo).ToList();
        foreach (var item in brollItems)
        {
            await SearchBrollForSegmentAsync(item, forceRefresh: true);
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
                await SearchBrollForSegmentAsync(item, forceRefresh: true);
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

    private async Task SearchBrollForSegmentAsync(BrollPromptItem item, bool forceRefresh = false)
    {
        item.IsSearching = true;
        item.SearchError = null;
        
        try
        {
            const int pageSize = 4;
            
            if (!forceRefresh && item.AllSearchResults.Count > 0)
            {
                var totalPages = (int)Math.Ceiling((double)item.AllSearchResults.Count / pageSize);
                item.SearchPage = (item.SearchPage + 1) % totalPages;
            }
            else
            {
                item.SearchPage = 0;
                var keywords = new List<string> { item.Prompt };
                var results = await AssetBroker.SearchVideosAsync(keywords, maxResults: 12);
                item.AllSearchResults = results;
            }
            
            item.SearchResults = item.AllSearchResults
                .Skip(item.SearchPage * pageSize)
                .Take(pageSize)
                .ToList();
        }
        catch (Exception ex)
        {
            item.SearchError = $"Search gagal: {ex.Message}";
        }
        finally
        {
            item.IsSearching = false;
        }
    }

    private async Task HandleSearchSingleSegment(BrollPromptItem item)
    {
        await SearchBrollForSegmentAsync(item);
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
                await SearchBrollForSegmentAsync(item, forceRefresh: true);
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
            {
                _classifyError = "Failed to extract global context. Please try again.";
            }
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
                await SearchBrollForSegmentAsync(item, forceRefresh: true);
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
            // Retry generating the image
            _ = Task.Run(async () =>
            {
                await InvokeAsync(() =>
                {
                    target.IsGenerating = true;
                    StateHasChanged();
                });

                await GenerateWhiskImageForItem(target);

                await InvokeAsync(() =>
                {
                    target.IsGenerating = false;
                    StateHasChanged();
                    _ = SaveBrollPromptsToDisk();
                });
            });
        }
    }

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

    private async Task HandleRegenPromptAndImage(BrollPromptItem item)
    {
        if (_resultSession == null) return;

        item.IsGenerating = true;
        item.CombinedRegenProgress = 10;
        item.WhiskError = null;
        item.WhiskVideoStatus = WhiskGenerationStatus.Pending;
        item.WhiskVideoPath = null;
        item.WhiskVideoError = null;
        StateHasChanged();

        try
        {
            item.CombinedRegenProgress = 20;
            StateHasChanged();

            string? imagePrompt;

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
            }
            else
            {
                throw new Exception("AI disconnected or failed to generate a new prompt. Please try again or check logs.");
            }

            item.CombinedRegenProgress = 50;
            StateHasChanged();

            item.CombinedRegenProgress = 70;
            StateHasChanged();

            await GenerateWhiskImageForItem(item);
            item.CombinedRegenProgress = 100;
        }
        catch (Exception ex)
        {
            item.WhiskStatus = WhiskGenerationStatus.Failed;
            item.WhiskError = ex.Message;
        }
        finally
        {
            item.IsGenerating = false;
            await SaveBrollPromptsToDisk();
            StateHasChanged();
        }
    }

    private async Task HandleGenerateAllWhiskImages()
    {
        var imageGenItems = _brollPromptItems
            .Where(i => i.MediaType == BrollMediaType.ImageGeneration && i.WhiskStatus != WhiskGenerationStatus.Done)
            .ToList();

        if (imageGenItems.Count == 0) return;

        _isGeneratingWhisk = true;
        _whiskTotalCount = imageGenItems.Count;
        _whiskGeneratedCount = 0;
        StateHasChanged();

        foreach (var item in imageGenItems)
        {
            item.IsGenerating = true;
            StateHasChanged();

            try
            {
                await GenerateWhiskImageForItem(item);
            }
            finally
            {
                item.IsGenerating = false;
                _whiskGeneratedCount++;
                StateHasChanged();
            }
        }

        _isGeneratingWhisk = false;
        await SaveBrollPromptsToDisk();
        StateHasChanged();
    }

    private async Task HandleGenerateKenBurnsVideo(BrollPromptItem item)
    {
        if (string.IsNullOrEmpty(item.WhiskImagePath) || !File.Exists(item.WhiskImagePath)) return;

        item.IsConvertingVideo = true;
        item.WhiskVideoError = null;
        item.WhiskVideoStatus = WhiskGenerationStatus.Generating;
        StateHasChanged();

        bool shouldApplyFilter = false;

        try
        {
            var duration = EstimateDuration(item.ScriptText);
            var outputDir = Path.GetDirectoryName(item.WhiskImagePath)!;
            var fileName = Path.GetFileNameWithoutExtension(item.WhiskImagePath) + "_kb.mp4";
            var outputPath = Path.Combine(outputDir, fileName);

            var success = await KenBurnsService.ConvertImageToVideoAsync(
                item.WhiskImagePath, outputPath, duration, 1920, 1080, item.KenBurnsMotion);

            if (success)
            {
                item.WhiskVideoPath = outputPath;
                item.WhiskVideoStatus = WhiskGenerationStatus.Done;
                shouldApplyFilter = item.HasVisualEffect;
            }
            else
            {
                item.WhiskVideoStatus = WhiskGenerationStatus.Failed;
                item.WhiskVideoError = "FFmpeg conversion failed — check logs";
            }
        }
        catch (Exception ex)
        {
            item.WhiskVideoStatus = WhiskGenerationStatus.Failed;
            item.WhiskVideoError = ex.Message;
        }
        finally
        {
            item.IsConvertingVideo = false;
            await SaveBrollPromptsToDisk();
            StateHasChanged();
        }

        if (shouldApplyFilter)
        {
            await Task.Delay(50);
            await HandleApplyFilterToVideo(item);
        }
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

    private async Task GenerateWhiskImageForItem(BrollPromptItem item)
    {
        item.WhiskStatus = WhiskGenerationStatus.Generating;
        item.WhiskError = null;
        item.WhiskVideoStatus = WhiskGenerationStatus.Pending;
        item.WhiskVideoPath = null;
        item.WhiskVideoError = null;
        StateHasChanged();

        try
        {
            // Use session's output directory for Whisk images
            var outputDir = !string.IsNullOrEmpty(_resultSession?.OutputDirectory)
                ? Path.Combine(_resultSession.OutputDirectory, "whisks_images")
                : Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? "temp", "whisks_images");
            Directory.CreateDirectory(outputDir);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var result = await WhiskGenerator.GenerateImageAsync(item.Prompt, outputDir, cancellationToken: cts.Token);
            if (result.Success)
            {
                item.WhiskStatus = WhiskGenerationStatus.Done;
                item.WhiskImagePath = result.ImagePath;
            }
            else
            {
                item.WhiskStatus = WhiskGenerationStatus.Failed;
                item.WhiskError = result.Error ?? "Unknown error";
            }
        }
        catch (OperationCanceledException)
        {
            item.WhiskStatus = WhiskGenerationStatus.Failed;
            item.WhiskError = "Timeout: generasi gambar melebihi 120 detik";
        }
        catch (Exception ex)
        {
            item.WhiskStatus = WhiskGenerationStatus.Failed;
            item.WhiskError = ex.Message;
        }
    }

    private string? GetBrollPromptsFilePath()
    {
        if (_resultSession == null) return null;

        if (!string.IsNullOrEmpty(_resultSession.OutputDirectory))
        {
            var directPath = Path.Combine(_resultSession.OutputDirectory, "broll-prompts.json");
            if (File.Exists(directPath)) return directPath;
        }

        if (!string.IsNullOrEmpty(_resultSession.OutputDirectory))
        {
            var relativePath = Path.Combine(Directory.GetCurrentDirectory(), _resultSession.OutputDirectory, "broll-prompts.json");
            if (File.Exists(relativePath)) return relativePath;
        }
        
        var constructedPath = Path.Combine(Directory.GetCurrentDirectory(), "output", _resultSession.Id, "broll-prompts.json");
        if (File.Exists(constructedPath)) return constructedPath;

        return constructedPath;
    }

    private void InvalidateBrollClassification()
    {
        // Delete all associated media files before clearing the list
        foreach (var item in _brollPromptItems)
        {
            // Delete Whisk image
            if (!string.IsNullOrEmpty(item.WhiskImagePath) && File.Exists(item.WhiskImagePath))
            {
                try { File.Delete(item.WhiskImagePath); } catch { }
            }
            
            // Delete Whisk video (Ken Burns)
            if (!string.IsNullOrEmpty(item.WhiskVideoPath) && File.Exists(item.WhiskVideoPath))
            {
                try { File.Delete(item.WhiskVideoPath); } catch { }
            }
            
            // Delete filtered video
            if (!string.IsNullOrEmpty(item.FilteredVideoPath) && File.Exists(item.FilteredVideoPath))
            {
                try { File.Delete(item.FilteredVideoPath); } catch { }
            }
            
            // Delete downloaded b-roll videos
            foreach (var video in item.SearchResults)
            {
                if (!string.IsNullOrEmpty(video.LocalPath) && File.Exists(video.LocalPath))
                {
                    try { File.Delete(video.LocalPath); } catch { }
                }
            }
        }

        _brollPromptItems.Clear();
        _classifyTotalSegments = 0;
        _classifyCompletedSegments = 0;

        var filePath = GetBrollPromptsFilePath();
        if (filePath != null && File.Exists(filePath))
        {
            try { File.Delete(filePath); }
            catch { }
        }
    }

    private async Task SaveBrollPromptsToDisk()
    {
        var filePath = GetBrollPromptsFilePath();
        if (filePath == null || _brollPromptItems.Count == 0) return;

        try
        {
            var saveData = _brollPromptItems.Select(i => new BrollPromptSaveItem
            {
                Index = i.Index, Timestamp = i.Timestamp, ScriptText = i.ScriptText,
                MediaType = i.MediaType, Prompt = i.Prompt, Reasoning = i.Reasoning,
                WhiskStatus = i.WhiskStatus, WhiskImagePath = i.WhiskImagePath, WhiskError = i.WhiskError,
                SelectedVideoUrl = i.SelectedVideoUrl, KenBurnsMotion = i.KenBurnsMotion,
                WhiskVideoStatus = i.WhiskVideoStatus, WhiskVideoPath = i.WhiskVideoPath, WhiskVideoError = i.WhiskVideoError,
                Style = i.Style, Filter = i.Filter, Texture = i.Texture, FilteredVideoPath = i.FilteredVideoPath,
                TextOverlay = i.TextOverlay
            }).ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(saveData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            _saveError = $"Gagal menyimpan broll prompts: {ex.Message}";
            Console.Error.WriteLine($"Failed to save broll prompts: {ex.Message}");
        }
    }

    private async Task SaveImageConfigToDisk()
    {
        var brollPath = GetBrollPromptsFilePath();
        if (brollPath == null) return;

        var configPath = Path.Combine(Path.GetDirectoryName(brollPath)!, "image-config.json");
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_imagePromptConfig, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            await File.WriteAllTextAsync(configPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save image config: {ex.Message}");
        }
    }

    private async Task LoadImageConfigFromDisk()
    {
        var brollPath = GetBrollPromptsFilePath();
        if (brollPath == null) return;

        var configPath = Path.Combine(Path.GetDirectoryName(brollPath)!, "image-config.json");
        if (!File.Exists(configPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<ImagePromptConfig>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            if (loaded != null)
            {
                _imagePromptConfig = loaded;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load image config: {ex.Message}");
        }
    }

    private async Task LoadBrollPromptsFromDisk()
    {
        var filePath = GetBrollPromptsFilePath();
        if (filePath == null || !File.Exists(filePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var saveItems = System.Text.Json.JsonSerializer.Deserialize<List<BrollPromptSaveItem>>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            if (saveItems != null)
            {
                _brollPromptItems = saveItems.Select(s => new BrollPromptItem
                {
                    Index = s.Index, Timestamp = s.Timestamp, ScriptText = s.ScriptText,
                    MediaType = s.MediaType, Prompt = s.Prompt, Reasoning = s.Reasoning,
                    WhiskStatus = s.WhiskStatus, WhiskImagePath = s.WhiskImagePath, WhiskError = s.WhiskError,
                    SelectedVideoUrl = s.SelectedVideoUrl,
                    KenBurnsMotion = (s.MediaType == BrollMediaType.ImageGeneration && s.KenBurnsMotion == KenBurnsMotionType.None)
                        ? BrollPromptItem.GetRandomMotion() : s.KenBurnsMotion,
                    WhiskVideoStatus = s.WhiskVideoStatus, WhiskVideoPath = s.WhiskVideoPath, WhiskVideoError = s.WhiskVideoError,
                    Style = s.Style, Filter = s.Filter, Texture = s.Texture, FilteredVideoPath = s.FilteredVideoPath,
                    TextOverlay = s.TextOverlay
                }).ToList();

                // Sanitize paths on load
                bool changed = false;
                foreach (var item in _brollPromptItems)
                {
                    if (!string.IsNullOrEmpty(item.WhiskImagePath) && item.WhiskImagePath.Contains("output\\scripts\\"))
                    {
                        // Fix absolute paths that incorrectly include 'scripts'
                        item.WhiskImagePath = item.WhiskImagePath.Replace("output\\scripts\\", "output\\");
                        changed = true;
                    }
                }
                if (changed) await SaveBrollPromptsToDisk();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load broll prompts: {ex.Message}");
        }
    }

    private async Task HandleApplyFilterToVideo(BrollPromptItem item)
    {
        if (item.IsFilteringVideo) return;
        
        try
        {
            item.IsFilteringVideo = true;
            item.FilterError = null;
            item.FilterProgress = 0;
            item.FilterStatus = "Initializing...";
            StateHasChanged();

            string? localPath = null;
            VideoAsset? selectedVideo = null;

            if (item.MediaType == BrollMediaType.ImageGeneration)
            {
                if (!string.IsNullOrEmpty(item.WhiskVideoPath) && File.Exists(item.WhiskVideoPath))
                {
                    localPath = item.WhiskVideoPath;
                }
                else
                {
                    throw new InvalidOperationException("No generated video available to filter. Convert image to video first.");
                }
            }
            else // BrollMediaType.BrollVideo
            {
                if (item.SearchResults.Count > 0)
                {
                    selectedVideo = item.SearchResults.FirstOrDefault(v => v.DownloadUrl == item.SelectedVideoUrl)
                                        ?? item.SearchResults.First();
                    
                    if (!string.IsNullOrEmpty(selectedVideo.LocalPath) && File.Exists(selectedVideo.LocalPath))
                    {
                        localPath = selectedVideo.LocalPath;
                    }
                }
                else
                {
                    throw new InvalidOperationException("No video selected to filter.");
                }

                if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
                {
                    item.FilterProgress = 10;
                    item.FilterStatus = "Downloading source...";
                    StateHasChanged();

                    // Download video to session's output directory
                    var videosDir = !string.IsNullOrEmpty(_resultSession?.OutputDirectory)
                        ? Path.Combine(_resultSession.OutputDirectory, "videos")
                        : Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? "temp", "videos");
                        
                    localPath = await DownloaderService.DownloadVideoToDirectoryAsync(
                        selectedVideo, videosDir, item.Index, "preview-source", CancellationToken.None);
                    
                    selectedVideo.LocalPath = localPath;
                }

                if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
                {
                    throw new Exception("Could not download source video.");
                }
            }

            item.FilterProgress = 40;
            item.FilterStatus = $"Applying {item.EffectiveFilter} + {item.EffectiveTexture}...";
            StateHasChanged();

            var videoConfig = new ShortVideoConfig { Ratio = AspectRatio.Landscape_16x9 };

            var filteredPath = await VideoComposer.ApplyFilterAndTextureToVideoAsync(
                localPath, item.EffectiveFilter, item.FilterIntensity, item.EffectiveTexture, item.TextureOpacity, videoConfig, CancellationToken.None, isPreview: true);

            if (!string.IsNullOrEmpty(filteredPath))
            {
                // Save filtered video to session's output directory
                var outputDir = !string.IsNullOrEmpty(_resultSession?.OutputDirectory)
                    ? Path.Combine(_resultSession.OutputDirectory, "filtered")
                    : Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? "temp", "filtered");
                Directory.CreateDirectory(outputDir);
                var finalFileName = $"filtered_{item.Index:D2}_{Guid.NewGuid().ToString("N")[..8]}.mp4";
                var finalPath = Path.Combine(outputDir, finalFileName);
                
                File.Move(filteredPath, finalPath, overwrite: true);
                item.FilteredVideoPath = finalPath;
                
                item.FilterProgress = 100;
                item.FilterStatus = "Done!";
                StateHasChanged();
                await Task.Delay(500);
            }
            else
            {
                throw new Exception("Filter application returned null path.");
            }

            await SaveBrollPromptsToDisk();
        }
        catch (Exception ex)
        {
            item.FilterError = $"Filter failed: {ex.Message}";
            Console.Error.WriteLine($"Filter error: {ex}");
        }
        finally
        {
            item.IsFilteringVideo = false;
            StateHasChanged();
        }
    }

    private string GetAssetUrl(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return "";
        if (absolutePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return absolutePath;

        // Resilient resolution
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
            // Fallback to basic string parsing if GetRelativePath fails
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
        
        // 1. Normalize and identify relative part after 'output/'
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
            // If it's already a relative path or something else, try to make it relative to output
            try { relative = Path.GetRelativePath(baseDir, absolutePath); } catch { relative = absolutePath; }
        }

        // 2. SMART RECOVERY: Strip incorrect 'scripts/' prefix if it's there (often accidental double nesting)
        if (relative.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = relative.Split('/');
            if (parts.Length > 2 && parts[1].Length == 8) // Looks like scripts/<sessionId>/...
            {
                relative = string.Join("/", parts.Skip(1)); // Remove 'scripts/'
            }
        }

        // 3. Return the resolved path directly
        return Path.Combine(baseDir, relative.Replace("/", Path.DirectorySeparatorChar.ToString()));
    }

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