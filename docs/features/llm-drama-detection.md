# LLM-Based Drama Detection

## Overview

The VO expansion pipeline uses Gemini LLM to intelligently detect dramatic pause points and text overlay opportunities in script entries. This approach enhances the base rule-based logic (which only detects commas, dots, and explicit markers) with semantic understanding of the Indonesian narrative script context.

## How It Works

1. **SRT Expansion**: Original CapCut SRT entries are expanded into smaller segments.
2. **LLM Analysis**: Gemini analyzes the expanded entries for:
   - **Drama Pauses**: Moments needing strategic silence (contrasts, revelations, suspense)
   - **Text Overlays**: Quran verses, Hadith, key phrases, rhetorical questions
3. **Merge Logic & Gap Enforcement**: 
   - LLM-detected pauses are merged with rule-based pauses (taking the maximum value).
   - *CRITICAL GAP ENFORCEMENT*: The logic calculates minimum reading duration for text overlays and strictly enforces large gaps (1.5-2.0s minimum) anytime a Text Overlay is detected, even if the LLM forgets to pair a pause duration with the overlay in its JSON output.
4. **VO Processing**: Enhanced pauses/gaps are used when slicing and stitching VO audio without overlapping speech.

## Pause Duration Guidelines (Long Video Pacing)

| Trigger | Expected Silence | Purpose |
|---------|----------|---------|
| Paragraph Break | 1.5s | Natural breathing space between thought sections |
| Chapter Transition (Big Topic Shift) | 2.0s - 2.5s | Maximize emotional reflection and signal new topic |
| Suspense (ellipsis ...) | 0.8s | Build anticipation |
| Rhetorical question | ~0.5s | Give listener time to think |
| Overlays (`quran_verse`, `hadith`) | >= 2.0s | Provide ample time to parse Arabic + Translation |
| Long Text Overlays (> 10 words) | >= 2.0s | Enhance reading readability |

## Error Handling

- **Success**: Green banner in UI shows pause/overlay counts and tokens used.
- **Partial/LLM Failure**: Yellow banner with error message. The code seamlessly gracefully falls back to rule-based pauses.
- **No Silent Failures**: The user consistently sees exact detection status.

## Example

**Input:**
```
[0]: sejarah umat manusia
[1]: seringkali mencatat kemenangan gemilang
[2]: namun realita historis
[3]: yang dialami oleh seorang nabi agung
```

**LLM JSON:**
```json
{
  "pauseDurations": {
    "1": 1.5
  },
  "textOverlays": {
    "2": {
      "type": "key_phrase",
      "text": "namun realita historis"
    }
  }
}
```

Result: Adds a 1.5s pause after entry 1 (before "namun..") and marks entry 2 for text overlay rendering downstream. 
