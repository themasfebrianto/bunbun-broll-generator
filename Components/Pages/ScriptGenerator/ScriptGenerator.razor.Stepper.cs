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

    private void GoToStep(int step)
    {
        if (step < 0 || step >= _stepperSteps.Length) return;

        if (step == 1 && !_canProceedToStep2) return;
        if (step == 2 && !_canProceedToStep3) return;
        if (step == 3 && !_canProceedToStep4) return;
        if (step == 4 && !_canProceedToStep5) return;

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
            DetectExistingVoAndSrt();
        }
        
        StateHasChanged();
    }

    private void NextStep() => GoToStep(_currentStep + 1);
    private void PreviousStep() => GoToStep(_currentStep - 1);

    private void OnScriptGenerationComplete()
    {
        _canProceedToStep2 = true;
        StateHasChanged();
    }

    private void OnExpansionComplete()
    {
        _canProceedToStep3 = true;
        StateHasChanged();
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
