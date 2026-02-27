using Microsoft.AspNetCore.Components;

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{
    // Stepper steps definition
    private readonly string[] _stepperSteps = new[]
    {
        "1. Generate Script",
        "2. Expand & Slice VO",  // NEW: Expansion + VO slicing step
        "3. B-Roll Prompts",
        "4. Generate Media",
        "5. Compose Video"
    };

    private int _currentStep = 0;
    private bool _canProceedToStep2 = false;
    private bool _canProceedToStep3 = false;
    private bool _canProceedToStep4 = false;
    private bool _canProceedToStep5 = false;

    /// <summary>
    /// Tracks whether SRT has changed since B-Roll prompts were generated.
    /// </summary>
    private enum SrtChangeStatus
    {
        NoBrollData,
        Unchanged,
        Changed,
        LegacyNeedsUpgrade
    }

    /// <summary>
    /// Checks if SRT has changed since B-Roll prompts were generated.
    /// </summary>
    private async Task<SrtChangeStatus> CheckSrtChangeStatus()
    {
        if (_brollPromptItems.Count == 0)
            return SrtChangeStatus.NoBrollData;

        var metadata = await BrollPersistence.LoadBrollMetadata(_resultSession, _sessionId);
        if (metadata == null)
            return SrtChangeStatus.NoBrollData;

        _storedMetadata = metadata;

        if (_expandedEntries == null || _expandedEntries.Count == 0)
        {
            // If we navigated here from list view, _expandedEntries might not be loaded yet
            await DetectExistingVoAndSrt();
        }

        var (currentCount, currentDuration) = ComputeSrtFingerprint();

        bool countChanged = currentCount != metadata.SrtEntryCount;
        bool durationChanged = Math.Abs(currentDuration - metadata.SrtTotalDuration) > 0.5;

        if (!string.IsNullOrEmpty(_brollWarning))
            return SrtChangeStatus.LegacyNeedsUpgrade;

        if (countChanged || durationChanged)
            return SrtChangeStatus.Changed;

        return SrtChangeStatus.Unchanged;
    }

    /// <summary>
    /// Direct navigation to a step without triggering smart navigation logic.
    /// Used after validation has already been performed.
    /// </summary>
    private async Task GoToStepDirect(int step)
    {
        if (step < 0 || step >= _stepperSteps.Length) return;

        _currentStep = step;
        _currentView = step switch
        {
            0 => "results",
            1 => "expand-vo",
            2 => "broll-prompts",
            3 => "generate-media",
            4 => "audio-assembly",
            _ => "results"
        };

        if (step == 2 && _brollPromptItems.Count == 0)
        {
            await LoadBrollPromptsFromDisk();
            await LoadImageConfigFromDisk();
            await LoadGlobalContextFromDisk();
        }

        StateHasChanged();
    }

    /// <summary>
    /// Smart navigation handler for Step 3 (B-Roll Prompts).
    /// Detects SRT changes and handles accordingly.
    /// </summary>
    private async Task HandleNavigateToBrollPrompts()
    {
        var status = await CheckSrtChangeStatus();

        switch (status)
        {
            case SrtChangeStatus.NoBrollData:
                RequestConfirmation(
                    "Initialize B-Roll Prompts",
                    "Ready to generate B-Roll prompts from expanded SRT. Proceed?",
                    async () =>
                    {
                        await ResetAndInitializeBrollFromSrt();
                        _canProceedToStep3 = true;
                        await GoToStepDirect(2);
                    });
                break;

            case SrtChangeStatus.Unchanged:
                _canProceedToStep3 = true;
                await GoToStepDirect(2);
                break;

            case SrtChangeStatus.Changed:
                var (currentCount, currentDuration) = ComputeSrtFingerprint();
                RequestConfirmation(
                    "SRT Has Changed",
                    $"⚠️ SRT content has changed since B-Roll prompts were generated.\n\n" +
                    $"Old: {_storedMetadata!.SrtEntryCount} entries, {_storedMetadata.SrtTotalDuration:F1}s\n" +
                    $"New: {currentCount} entries, {currentDuration:F1}s\n\n" +
                    "This will reset ALL B-Roll prompts. Continue?",
                    async () =>
                    {
                        await ResetAndInitializeBrollFromSrt();
                        _canProceedToStep3 = true;
                        await GoToStepDirect(2);
                    });
                break;
                
            case SrtChangeStatus.LegacyNeedsUpgrade:
                RequestConfirmation(
                    "Upgrade Project Lama",
                    "⚠️ Data project versi lama terdeteksi (durasi video 0s).\n\nMengklik Continue akan mereset segment B-Roll untuk mengkalkulasi durasi yang akurat dari SRT. Lanjutkan?",
                    async () =>
                    {
                        await ResetAndInitializeBrollFromSrt();
                        _brollWarning = null;
                        _canProceedToStep3 = true;
                        await GoToStepDirect(2);
                    });
                break;
        }
    }

    private async Task GoToStep(int step)
    {
        if (step < 0 || step >= _stepperSteps.Length) return;

        if (step == 1 && !_canProceedToStep2) return;
        if (step == 2 && !_canProceedToStep3) return;
        if (step == 3 && !_canProceedToStep4) return;
        if (step == 4 && !_canProceedToStep5) return;

        if (step == 2 && _currentView == "expand-vo")
        {
            await HandleNavigateToBrollPrompts();
            return;
        }

        _currentStep = step;
        
        // Sync with existing views
        _currentView = step switch
        {
            0 => "results",
            1 => "expand-vo",
            2 => "broll-prompts",
            3 => "generate-media",
            4 => "audio-assembly",
            _ => "results"
        };

        // Auto-detect existing files when entering Step 2
        if (step == 1)
        {
            await DetectExistingVoAndSrt();
        }
        
        StateHasChanged();
    }

    private async Task NextStep() => await GoToStep(_currentStep + 1);
    private async Task PreviousStep() => await GoToStep(_currentStep - 1);

    private void OnScriptGenerationComplete()
    {
        _canProceedToStep2 = true;
        StateHasChanged();
    }

    private async Task OnExpansionComplete()
    {
        // Unconditionally regenerate B-roll from SRT when proceeding via the Step 2 button
        await ResetAndInitializeBrollFromSrt();
        _canProceedToStep3 = true;
        await GoToStepDirect(2);
    }

    private void OnBrollPromptsComplete()
    {
        _canProceedToStep4 = true;
        StateHasChanged();
    }

    private void OnMediaGenerationComplete()
    {
        _canProceedToStep5 = true;
        StateHasChanged();
    }

    private bool IsNextStepDisabled()
    {
        return _currentStep switch
        {
            0 => !_canProceedToStep2,
            1 => !_canProceedToStep3,
            2 => !_canProceedToStep4,
            3 => !_canProceedToStep5,
            _ => true
        };
    }
}
