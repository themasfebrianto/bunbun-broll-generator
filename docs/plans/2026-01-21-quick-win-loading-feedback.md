# Quick Win: Loading Feedback Improvements

**Date:** 2026-01-21
**Target User:** Content Creator (YouTuber/TikToker)
**Primary Pain Point:** Loading/Processing Time
**Status:** Design Complete

## Problem

Saat ini user hanya melihat:
- Progress counter: "Processing X of Y"
- Status text di satu tempat (mudah hilang dari viewport)
- Tidak ada estimasi waktu selesai
- Tidak jelas lagi di tahap apa (AI? API search? Download?)

## Solution: 5 Quick Wins

### 1. Progress Bar dengan % & ETA (Priority: 🔥 High)

**Tampilan:**
```
┌─────────────────────────────────────────┐
│ Lagi proses ya...                       │
│ ████████████░░░░░░░░░░░░░░  60%         │
│ 6 dari 10 kalimat udah kelar            │
│ ⏱️ Kurang lebih 15 detik lagi          │
└─────────────────────────────────────────┘
```

**Components:**
- Primary progress bar dengan persentase
- Counter konkret: "6 dari 10 kalimat udah kelar"
- ETA dihitung dari: `elapsedTime / percentComplete * (100 - percentComplete)`

**Stage Breakdown:**
- Stage 1 (AI Extraction): ~30% total time
- Stage 2 (Video Search): ~60% total time
- Stage 3 (Finalize): ~10% total time

### 2. Stage Indicators (Priority: 🔥 High)

**Tampilan Full:**
```
[ ✓ Mikirin Keyword ]  →  [ 🔄 Nyari Video ]  →  [ ⏸ Selesai ]
       30%                      60%                      100%
```

**Tampilan Compact (Mobile):**
```
[✓] → [🔄 60%] → [⏸]
 🧠     🔍        ✓
```

**States:**
- Active: Biru/primary, ada pulse animation
- Done: Hijau dengan centang
- Waiting: Abu-abu muted

**Copy per Stage:**

| Stage | Text Active | Text Done |
|-------|-------------|-----------|
| AI Keywords | "Lagi mikirin keyword..." | "Keyword udah ketemu ✓" |
| Video Search | "Nyari video yang cocok..." | "Video ketemu semua ✓" |
| Finalize | "Benerin hasil..." | "Selesai! ✓" |

### 3. Full Page Loading Screen (Priority: Medium)

**Tampilan:**
```
┌─────────────────────────────────────────┐
│                                         │
│         [ Bunny Loader Animation ]      │
│                                         │
│     Lagi proses script kamu...          │
│                                         │
│ ┌─────────────────────────────────┐    │
│ │ ████████░░░░░░░░░░░░░░  60%     │    │
│ └─────────────────────────────────┘    │
│                                         │
│    6 dari 10 kalimat udah kelar         │
│    ⏱️ Kurang lebih 15 detik lagi       │
│                                         │
│    💡 Tips sambil nunggu:               │
│    Semakin detail script,               │
│    makin akurat videonya!               │
│                                         │
└─────────────────────────────────────────┘
```

**Transitions:**
- Fade-in 200ms saat muncul
- Fade-out lalu results fade-in saat selesai

**Tips Rotation (optional):**
- "💡 Tips: Kamu bisa edit keyword kalau videonya kurang pas"
- "💡 Tips: Pakai mood 'Cinematic' buat vibe yang lebih sinematik"
- "💡 Tips: Aktifin Halal Mode buat filter konten"

### 4. Toast Notifications (Priority: Medium)

**Simplified - only for completion:**

| Trigger | Copy |
|---------|------|
| Mulai process | "Mulai nyari B-Roll..." |
| Selesai semua | "✓ Selesai! Cek hasilnya ↓" |
| Error | "Oops, gagal. Coba lagi?" |

**Behavior:**
- Muncul di bottom-right
- Slide-in animation dari bawah
- Auto-dismiss 5 detik
- Max 3 toasts di-stack

### 5. Mobile Sticky Progress (Priority: Medium)

**Tampilan:**
```
┌─────────────────────────────┐
│ [Content scrollable]         │
│                             │
├─────────────────────────────┤ ← Sticky
│ 🔄 60% • 6 dari 10 kelar    │
│ ████████░░░░░░░░░░░░░░      │
└─────────────────────────────┘
```

**Features:**
- Background blur
- Tap untuk expand (show stage detail)
- Pull-to-refresh gesture untuk retry

**Copy:**
- Processing: "🔄 Lagi proses ya... 60%"
- Done: "✓ Selesai! Lihat hasil →"

## Microcopy: Gojek Style

**Global Text Replacements:**

| Before | After |
|--------|-------|
| "Generate" | "Cari B-Roll" |
| "Processing X of Y" | "X dari Y udah kelar" |
| "Download ZIP" | "Download Semua" |
| "Export Links" | "Ekspor Link" |
| "Save Project" | "Simpen Project" |
| "Back to Editor" | "Balik ke Editor" |
| "Regenerate keywords" | "Cari Keyword Lagi" |
| "Confirm Choice" | "Pilih Video Ini" |
| "Research" / "Retry" | "Cari Lagi" |

**Error Messages (Gojek style):**
- Timeout: "Waduh, lama banget. Coba refresh ya?"
- API Error: "Oops, server lagi PMS. Coba lagi bentar?"
- Slow connection: "Koneksi lambat, sabar ya..."

## Technical Notes

### Progress Calculation
```
percentComplete = (processedCount / totalCount) * 100
etaSeconds = (elapsedTime / percentComplete) * (100 - percentComplete)
```

### Stage Detection
- `OnSentenceProgress` event sudah ada di `PipelineOrchestrator`
- Tinggal map ke 3 stage utama
- Update `StateHasChanged()` real-time

### Existing Events to Use
- `OnSentenceProgress` → update counter
- `OnJobProgress` → update stage
- `_statusMessage` → sudah ada, tinggal improve copy

## Implementation Priority

| Phase | Features | Effort |
|-------|----------|--------|
| 1 | Progress Bar + ETA + Stage Indicators | Medium |
| 2 | Full Page Loading Screen | Low |
| 3 | Toast Notifications | Low |
| 4 | Mobile Sticky Progress | Low |

## Out of Scope (Not Implemented)

- ❌ Smart Error Recovery (sudah ada di Polly backend)
- ❌ Micro-interactions (confetti, haptic, sound)
- ❌ Streaming Results per sentence (batch architecture)
- ❌ No Results Skeleton (fallback to script text)

## Success Metrics

- User tahu persis berapa lama lagi menunggu
- User mengerti lagi di tahap apa
- Tidak ada "bingung ini hang atau jalan?"
- Mobile experience tetap clear pas scroll
