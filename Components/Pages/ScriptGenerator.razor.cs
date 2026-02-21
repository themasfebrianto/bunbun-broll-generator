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

    // Query parameters for deep linking (e.g. from /video page)
    [SupplyParameterFromQuery(Name = "sessionId")]
    public string? QuerySessionId { get; set; }

    [SupplyParameterFromQuery(Name = "view")]
    public string? QueryView { get; set; }

    // Views: list, config, progress, results, broll-prompts
    private string _currentView = "list";
    private List<ScriptGenerationSession> _sessions = new();
    private bool _isLoadingSessions = true;
    private Dictionary<string, BRollSummary> _brollSummaries = new();

    // Event bus subscriptions
    private IDisposable? _progressSubscription;
    private IDisposable? _listSubscription;

    // Config
    private List<ScriptPattern> _availablePatterns = new();
    private List<string> _channelNames = new();
    private string _selectedPatternId = "";
    private ScriptPattern? _selectedPattern => _availablePatterns.FirstOrDefault(p => p.Id == _selectedPatternId);
    private string _channelName = "";
    private string _topic = "";
    private string? _outline;
    private string? _sourceReferences;
    private int _targetDuration = 30;
    private bool _isGenerating = false;
    private bool _isCancelling = false;
    private string? _errorMessage;
    private string? _saveError;

    // Progress
    private string? _sessionId;
    private string? _progressMessage;
    private double _progressPercent;
    private int _completedPhases;
    private int _totalPhases;
    private List<PhaseStatusItem> _phaseStatuses = new();

    // Results
    private ScriptGenerationSession? _resultSession;
    private List<ResultSection> _resultSections = new();
    private int _totalWords;
    private int _totalMinutes;
    private int _validatedCount;
    private bool _isRegeneratingAll;
    private bool _isExportingLrc;
    private string? _lrcExportPath;

    // Broll Prompts
    private List<BrollPromptItem> _brollPromptItems = new();
    private ImagePromptConfig _imagePromptConfig = new();
    private GlobalScriptContext? _globalContext;
    private bool _isClassifyingBroll;
    private int _classifyTotalSegments;
    private int _classifyCompletedSegments;
    private string? _classifyError;
    private bool _isSearchingBroll;
    private bool _isRegeneratingAllKeywords;
    private bool _isGeneratingWhisk;
    private int _whiskGeneratedCount;
    private int _whiskTotalCount;
    private bool _isGeneratingImagePrompts;
    private int _imagePromptGeneratedCount;
    private int _imagePromptTotalCount;
    private bool _isGeneratingKeywords;
    private int _keywordGeneratedCount;
    private int _keywordTotalCount;

    // Delete
    private bool _showDeleteConfirm;
    private ScriptGenerationSession? _deleteTarget;

    // Batch Gen-Configs
    private string _batchTheme = "";
    private string _batchChannelName = "";
    private int _batchCount = 10;
    private string _batchPatternId = "";
    private string? _batchSeed;
    private bool _isBatchGenerating;
    private string? _batchError;
    private List<BatchConfigView.GeneratedConfig> _batchResults = new();
    private bool _isDeleting;

    // Edit Config
    private bool _showEditConfig;
    private bool _isSavingConfig;
    private string _editTopic = "";
    private string? _editOutline;

    // Cookie Update Modal
    private bool _showCookieModal;
    private string _newWhiskCookie = "";
    private BrollPromptItem? _cookieUpdateTargetItem;
    private string? _editSourceReferences;
    private int _editTargetDuration;
    private string _editChannelName = "";

    // Confirmation Modal State
    private bool _showConfirmDialog;
    private string _confirmTitle = "";
    private string _confirmMessage = "";
    private Func<Task>? _pendingConfirmAction;

    private readonly List<IDisposable> _listSubscriptions = new();

    private bool CanGenerate => !string.IsNullOrWhiteSpace(_topic)
        && !string.IsNullOrWhiteSpace(_selectedPatternId)
        && !string.IsNullOrWhiteSpace(_channelName)
        && !_isGenerating;

    protected override async Task OnInitializedAsync()
    {
        _channelNames = Configuration.GetSection("Channels").Get<List<string>>() ?? new List<string>();

        await LoadSessionsAsync();
        _availablePatterns = await ScriptService.GetAvailablePatternsAsync();
        if (_availablePatterns.Count == 1)
            _selectedPatternId = _availablePatterns[0].Id;
        if (_channelNames.Count == 1)
        {
            _channelName = _channelNames[0];
            _batchChannelName = _channelNames[0];
        }
        if (_availablePatterns.Count == 1)
            _batchPatternId = _availablePatterns[0].Id;

        if (!string.IsNullOrEmpty(QuerySessionId))
        {
            var session = await ScriptService.GetSessionAsync(QuerySessionId);
            if (session != null && session.Status == SessionStatus.Completed)
            {
                _sessionId = session.Id;
                _resultSession = session;
                _totalPhases = session.Phases.Count;
                await LoadResultSections(session);

                if (QueryView == "broll-prompts")
                {
                    await LoadBrollPromptsFromDisk();
                    _currentView = "broll-prompts";
                    if (_brollPromptItems.Any(i => i.MediaType == BrollMediaType.BrollVideo && i.SearchResults.Count == 0))
                    {
                        _ = Task.Run(async () =>
                        {
                            await InvokeAsync(async () =>
                            {
                                await SearchBrollForAllSegmentsAsync();
                                StateHasChanged();
                            });
                        });
                    }
                }
                else
                {
                    await LoadBrollPromptsFromDisk();
                    _currentView = "broll-prompts";
                    _ = AutoSearchMissingBrollSegments();
                }
                return;
            }
        }

        SubscribeToRunningSessionsForList();
    }

    public void Dispose()
    {
        _progressSubscription?.Dispose();
        _listSubscription?.Dispose();
        foreach (var sub in _listSubscriptions)
            sub.Dispose();
        _listSubscriptions.Clear();
    }

}