# Non-Destructive Stepper Navigation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable safe navigation between Step 2 (Expand & Slice VO) and Step 3 (B-Roll Prompts) without destroying existing work when the SRT hasn't changed.

**Architecture:** Smart SRT fingerprinting (entry count + total duration) stored as metadata. Navigation checks fingerprint before deciding whether to preserve existing B-Roll data or warn about SRT changes.

**Tech Stack:** Blazor Server, C#, System.Text.Json, BunbunBroll.Services

---

## Overview of Changes

1. Add `BrollSessionMetadata` class to store SRT fingerprint
2. Add persistence methods to save/load metadata
3. Add SRT change detection logic
4. Update stepper navigation to be smart/non-destructive
5. Update "Proceed" button to use smart detection

---

## Task 1: Add BrollSessionMetadata Model

**Files:**
- Create: `Models/BrollSessionMetadata.cs`

**Step 1: Create the metadata model class**

```csharp
namespace BunbunBroll.Models;

/// <summary>
/// Stores fingerprint of SRT state when B-Roll prompts were generated.
/// Used to detect if SRT has changed, avoiding destructive re-initialization.
/// </summary>
public class BrollSessionMetadata
{
    public int SrtEntryCount { get; set; }
    public double SrtTotalDuration { get; set; }  // seconds
    public string? SrtFilePath { get; set; }
    public DateTime GeneratedAt { get; set; }

    public BrollSessionMetadata()
    {
        GeneratedAt = DateTime.UtcNow;
    }
}
```

**Step 2: Commit**

```bash
git add Models/BrollSessionMetadata.cs
git commit -m "feat: add BrollSessionMetadata model for SRT fingerprint tracking"
```

---

## Task 2: Add Metadata Persistence Methods

**Files:**
- Modify: `Services/BrollPersistenceService.cs`

**Step 1: Add metadata persistence methods to IBrollPersistenceService interface**

First, read the interface to find the right location:

```bash
grep -n "interface IBrollPersistenceService" Services/BrollPersistenceService.cs
```

Add these method signatures to the interface:

```csharp
Task SaveBrollMetadata(BrollSessionMetadata metadata, ScriptGenerationSession? session, string? sessionId);
Task<BrollSessionMetadata?> LoadBrollMetadata(ScriptGenerationSession? session, string? sessionId);
```

**Step 2: Implement the methods in BrollPersistenceService class**

```csharp
public async Task SaveBrollMetadata(BrollSessionMetadata metadata, ScriptGenerationSession? session, string? sessionId)
{
    if (session == null || string.IsNullOrEmpty(sessionId)) return;

    var dir = Path.Combine("output", sessionId);
    Directory.CreateDirectory(dir);

    var path = Path.Combine(dir, "broll-metadata.json");
    var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(path, json);
}

public async Task<BrollSessionMetadata?> LoadBrollMetadata(ScriptGenerationSession? session, string? sessionId)
{
    if (session == null || string.IsNullOrEmpty(sessionId)) return null;

    var path = Path.Combine("output", sessionId, "broll-metadata.json");
    if (!File.Exists(path)) return null;

    var json = await File.ReadAllTextAsync(path);
    try
    {
        return JsonSerializer.Deserialize<BrollSessionMetadata>(json);
    }
    catch (JsonException)
    {
        return null;
    }
}
```

**Step 3: Add using statement at top of file if not present**

```csharp
using System.Text.Json;
```

**Step 4: Commit**

```bash
git add Services/BrollPersistenceService.cs
git commit -m "feat: add B-Roll metadata persistence (save/load)"
```

---

## Task 3: Add SRT Fingerprint Computation

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs`

**Step 1: Add SRT fingerprint computation method**

Add this method at the end of the partial class:

```csharp
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
```

**Step 2: Add field to store loaded metadata**

Add at the top of the class with other fields:

```csharp
private BrollSessionMetadata? _storedMetadata;
```

**Step 3: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs
git commit -m "feat: add SRT fingerprint computation"
```

---

## Task 4: Modify ResetAndInitializeBrollFromSrt to Save Metadata

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs`

**Step 1: Update ResetAndInitializeBrollFromSrt to save metadata**

Find the existing method and modify it. Before the `await SaveBrollPromptsToDisk();` call, add:

```csharp
// 5. Save SRT metadata for change detection
var (entryCount, totalDuration) = ComputeSrtFingerprint();
var metadata = new BrollSessionMetadata
{
    SrtEntryCount = entryCount,
    SrtTotalDuration = totalDuration,
    SrtFilePath = _srtFilePath,
    GeneratedAt = DateTime.UtcNow
};
await BrollPersistence.SaveBrollMetadata(metadata, _resultSession, _sessionId);
_storedMetadata = metadata; // Cache in memory
```

The method should now save metadata before saving prompts. This enables future comparisons to detect SRT changes.

**Step 2: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs
git commit -m "feat: save SRT metadata when initializing B-Roll prompts"
```

---

## Task 5: Add SRT Change Detection Enum and Method

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`

**Step 1: Add SrtChangeStatus enum**

Add at the top of the file, after the field declarations:

```csharp
/// <summary>
/// Tracks whether SRT has changed since B-Roll prompts were generated.
/// </summary>
private enum SrtChangeStatus
{
    /// <summary>No B-Roll prompts exist yet</summary>
    NoBrollData,
    /// <summary>SRT fingerprint matches - safe to navigate</summary>
    Unchanged,
    /// <summary>SRT fingerprint differs - user should be warned</summary>
    Changed
}
```

**Step 2: Add CheckSrtChangeStatus method**

```csharp
/// <summary>
/// Checks if SRT has changed since B-Roll prompts were generated.
/// </summary>
private async Task<SrtChangeStatus> CheckSrtChangeStatus()
{
    // No existing B-Roll data
    if (_brollPromptItems.Count == 0)
        return SrtChangeStatus.NoBrollData;

    // Load stored metadata
    var metadata = await BrollPersistence.LoadBrollMetadata(_resultSession, _sessionId);
    if (metadata == null)
        return SrtChangeStatus.NoBrollData;

    // Cache for display in warnings
    _storedMetadata = metadata;

    // Compute current fingerprint
    var (currentCount, currentDuration) = ComputeSrtFingerprint();

    // Compare with stored (allow small floating point tolerance)
    bool countChanged = currentCount != metadata.SrtEntryCount;
    bool durationChanged = Math.Abs(currentDuration - metadata.SrtTotalDuration) > 0.5;

    if (countChanged || durationChanged)
    {
        return SrtChangeStatus.Changed;
    }

    return SrtChangeStatus.Unchanged;
}
```

**Step 3: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs
git commit -m "feat: add SRT change detection logic"
```

---

## Task 6: Add Direct Navigation Helper

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`

**Step 1: Add GoToStepDirect helper method**

This bypasses the smart navigation check for internal use:

```csharp
/// <summary>
/// Direct navigation to a step without triggering smart navigation logic.
/// Used after validation has already been performed.
/// </summary>
private void GoToStepDirect(int step)
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

    // Auto-load prompts if needed
    if (step == 2 && _brollPromptItems.Count == 0)
    {
        _ = LoadBrollPromptsFromDisk();
    }

    StateHasChanged();
}
```

**Step 2: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs
git commit -m "feat: add GoToStepDirect helper for bypassing smart navigation"
```

---

## Task 7: Add Smart Navigation Handler for Step 3

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`

**Step 1: Add HandleNavigateToBrollPrompts method**

```csharp
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
            // First time - initialize from SRT
            RequestConfirmation(
                "Initialize B-Roll Prompts",
                "Ready to generate B-Roll prompts from expanded SRT. Proceed?",
                async () =>
                {
                    await ResetAndInitializeBrollFromSrt();
                    _canProceedToStep3 = true;
                    GoToStepDirect(2);
                });
            break;

        case SrtChangeStatus.Unchanged:
            // SRT hasn't changed - just navigate
            _canProceedToStep3 = true;
            GoToStepDirect(2);
            break;

        case SrtChangeStatus.Changed:
            // SRT changed - warn user with details
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
                    GoToStepDirect(2);
                });
            break;
    }
}
```

**Step 2: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs
git commit -m "feat: add smart navigation handler for B-Roll Prompts step"
```

---

## Task 8: Update GoToStep to Use Smart Navigation

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`

**Step 1: Modify GoToStep signature to be async**

Change:
```csharp
private void GoToStep(int step)
```

To:
```csharp
private async Task GoToStep(int step)
```

**Step 2: Update GoToStep to use smart navigation for Step 3**

Replace the entire method body with:

```csharp
private async Task GoToStep(int step)
{
    if (step < 0 || step >= _stepperSteps.Length) return;

    // Validation checks
    if (step == 1 && !_canProceedToStep2) return;
    if (step == 2 && !_canProceedToStep3) return;
    if (step == 3 && !_canProceedToStep4) return;
    if (step == 4 && !_canProceedToStep5) return;

    // Smart navigation to Step 3 (B-Roll Prompts)
    if (step == 2 && _currentView == "expand-vo")
    {
        await HandleNavigateToBrollPrompts();
        return;
    }

    // Standard navigation for other steps
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

    if (step == 1) DetectExistingVoAndSrt();
    StateHasChanged();
}
```

**Step 3: Update stepper button onclick to handle async**

In `ScriptGenerator.razor`, find the stepper button (around line 88):
```razor
@onclick="() => GoToStep(stepIndex)"
```

Change to:
```razor
@onclick="async () => await GoToStep(stepIndex)"
```

**Step 4: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs Components/Pages/ScriptGenerator/ScriptGenerator.razor
git commit -m "feat: make GoToStep async with smart navigation for Step 3"
```

---

## Task 9: Update OnExpansionComplete to Use Smart Detection

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`

**Step 1: Replace OnExpansionComplete with smart version**

Replace the existing method with:

```csharp
/// <summary>
/// Called when user clicks "Proceed" from Step 2 (Expand & Slice VO).
/// Now smart - only resets if SRT has changed or no data exists.
/// </summary>
private async Task OnExpansionComplete()
{
    _canProceedToStep3 = true;

    var status = await CheckSrtChangeStatus();

    if (status == SrtChangeStatus.NoBrollData)
    {
        // First time - initialize
        RequestConfirmation(
            "Proceed to B-Roll Prompts",
            "Ready to generate B-Roll prompts from expanded SRT. Proceed?",
            async () =>
            {
                await ResetAndInitializeBrollFromSrt();
                GoToStepDirect(2);
            });
    }
    else if (status == SrtChangeStatus.Changed)
    {
        // SRT changed - warn about reset
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
                GoToStepDirect(2);
            });
    }
    else
    {
        // Unchanged - navigate directly, preserving work
        GoToStepDirect(2);
    }
}
```

**Step 2: Update the OnComplete callback to be async**

In `ScriptGenerator.razor` (around line 249), find:
```razor
OnComplete="OnExpansionComplete"
```

The parameter type needs to support async. The EventCallback should already support this, but verify the handler signature matches.

**Step 3: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs
git commit -m "feat: make OnExpansionComplete smart/non-destructive"
```

---

## Task 10: Update NextStep and PreviousStep to be async

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`

**Step 1: Update helper methods to async**

Change:
```csharp
private void NextStep() => GoToStep(_currentStep + 1);
private void PreviousStep() => GoToStep(_currentStep - 1);
```

To:
```csharp
private async Task NextStep() => await GoToStep(_currentStep + 1);
private async Task PreviousStep() => await GoToStep(_currentStep - 1);
```

**Step 2: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs
git commit -m "feat: make NextStep/PreviousStep async"
```

---

## Task 11: Handle SendToBroll Navigation

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor`
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs`

**Step 1: Update OnSendToBroll callback**

Find line 225 in ScriptGenerator.razor:
```razor
OnSendToBroll="() => GoToStep(1)"
```

Change to:
```razor
OnSendToBroll="async () => await GoToStep(1)"
```

**Step 2: Update HandleSendToBroll to load data and navigate**

In `ScriptGenerator.razor.Broll.cs`, the method currently auto-navigates. We should unlock the stepper and navigate directly:

```csharp
private async Task HandleSendToBroll()
{
    if (_resultSession == null || _resultSections.Count == 0) return;

    if (_brollPromptItems.Count == 0)
    {
        await LoadBrollPromptsFromDisk();
        await LoadImageConfigFromDisk();
        await LoadGlobalContextFromDisk();
    }

    // Unlock Step 3 if we loaded prompts successfully
    if (_brollPromptItems.Count > 0)
    {
        _canProceedToStep3 = true;
    }

    // Navigate to Step 2 (Expand VO) first, user can click "Proceed" or stepper if unlocked
    GoToStepDirect(1); 
}
```

**Step 3: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs
git commit -m "feat: update SendToBroll to unlock Stepper 3 if data exists"
```

---

## Task 12: Initialize Stepper State on Session Load

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs` (or where LoadBrollPromptsFromDisk is called)

**Step 1: Unlock Stepper 3 when B-Roll prompts are loaded**

Ensure that whenever we load an existing session that already has B-Roll prompts, we unlock Step 3 so the user can easily click the stepper without being forced to click "Proceed".

Modify `LoadBrollPromptsFromDisk()` or the caller methods to set:
```csharp
if (_brollPromptItems.Count > 0)
{
    _canProceedToStep3 = true;
}
```

This is crucial for the "easy stepper navigation" requirement. If they have data, the stepper is unlocked. If they don't, the stepper is locked and they MUST click "Proceed".

**Step 2: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs
git commit -m "feat: unlock Stepper 3 when loading existing B-Roll prompts"
```

---

## Task 13: Testing and Verification

**Files:**
- Test: Manual testing in browser

**Step 1: Test Scenario 1 - First time navigation**

1. Start a new session
2. Generate script (Step 1)
3. Expand & Slice VO (Step 2)
4. Click "Proceed" button
5. Expected: Confirmation dialog shown, then B-Roll prompts initialized
6. Verify `broll-metadata.json` was created in output directory

**Step 2: Test Scenario 2 - Navigate back without changes**

1. From Step 3 (B-Roll Prompts), click stepper to go to Step 2
2. Make NO changes to VO/SRT
3. Click stepper from Step 2 to Step 3
4. Expected: Direct navigation, NO confirmation dialog, existing prompts preserved

**Step 3: Test Scenario 3 - Navigate after SRT change**

1. From Step 2, re-process VO with different settings (different pad cap, etc.)
2. Click stepper from Step 2 to Step 3
3. Expected: Warning dialog showing old vs new entry counts/durations
4. If confirmed: B-Roll prompts are reset

**Step 4: Test Scenario 4 - Existing prompts, new SRT in same session**

1. Complete Steps 1-3 with some data
2. Go back to Step 2
3. Upload a NEW SRT file
4. Process it
5. Click "Proceed"
6. Expected: Warning about SRT change

**Step 5: Verify metadata file format**

Check `output/{sessionId}/broll-metadata.json`:
```json
{
  "SrtEntryCount": 123,
  "SrtTotalDuration": 456.7,
  "SrtFilePath": "path/to/srt",
  "GeneratedAt": "2025-02-23T12:34:56Z"
}
```

---

## Task 14: Update IsNextStepDisabled if Needed

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`

**Step 1: Review IsNextStepDisabled method**

The method returns `true` if next step is disabled. Currently uses `_canProceedToStepX` flags.

Verify this still works correctly with smart navigation. The flags are set when steps are completed, so this should remain unchanged.

**Step 2: No changes needed**

The `_canProceedToStep3` flag is now set in `HandleNavigateToBrollPrompts`, `OnExpansionComplete`, and upon `Session Load`, so the existing logic works perfectly.

---

## Task 15: Final Polish and Edge Cases

**Files:**
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`

**Step 1: Handle edge case - _expandedEntries is null**

In `CheckSrtChangeStatus()`, ensure null safety:

```csharp
if (_expandedEntries == null || _expandedEntries.Count == 0)
    return SrtChangeStatus.NoBrollData;
```

This is already handled by `ComputeSrtFingerprint()` returning `(0, 0)`.

**Step 2: Add tolerance for floating point comparison**

Already added: `Math.Abs(currentDuration - metadata.SrtTotalDuration) > 0.5`

**Step 3: Verify StateHasChanged is called appropriately**

All navigation paths should call `StateHasChanged()`:
- `GoToStepDirect()` - Yes
- `HandleNavigateToBrollPrompts()` - Via GoToStepDirect
- `OnExpansionComplete()` - Via GoToStepDirect

**Step 4: Commit any final adjustments**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs
git commit -m "polish: add null safety and floating point tolerance"
```

---

## Summary

This plan implements non-destructive stepper navigation by:

1. **Tracking SRT state** - Store entry count + duration as metadata
2. **Detecting changes** - Compare current SRT fingerprint with stored metadata
3. **Smart navigation** - Only reset if SRT changed; otherwise preserve work
4. **Clear warnings** - Show old vs new values when SRT has changed

The key insight is that most navigations are "just checking something" or "continuing work" - only actual SRT changes require destructive reset.

---

## Files Modified

| File | Change |
|------|--------|
| `Models/BrollSessionMetadata.cs` | NEW - SRT fingerprint model |
| `Services/BrollPersistenceService.cs` | Add SaveBrollMetadata, LoadBrollMetadata |
| `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs` | Add smart navigation logic |
| `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Broll.cs` | Add fingerprint computation, save metadata |
| `Components/Pages/ScriptGenerator/ScriptGenerator.razor` | Update onclick handlers to async |

---

## Testing Checklist

- [ ] First time navigation prompts for initialization
- [ ] Navigate back/forth without changes preserves work
- [ ] SRT changes trigger warning with old/new comparison
- [ ] Metadata file created correctly
- [ ] All stepper buttons work correctly
- [ ] "Proceed" button behavior is smart
- [ ] Existing sessions (without metadata) still work
