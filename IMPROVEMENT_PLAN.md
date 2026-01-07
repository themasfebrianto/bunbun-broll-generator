# BunBun B-Roll Generator - Improvement Plan

> **Focus:** Logic Improvements + UI Polish (Vercel-style SaaS UI)
> **Date:** January 7, 2026

---

## Executive Summary

This plan outlines improvements for the BunBun B-Roll Generator application, focusing on two key areas: **logical improvements** to enhance functionality, performance, and user experience flow, and **UI polish** following a **Vercel-style SaaS design** — clean, minimal, high-contrast, with solid backgrounds and sharp typography.

---

## Part 1: Logic & Architecture Improvements

### 1.1 Error Handling & Resilience

| Priority | Task | Description | Impact |
|----------|------|-------------|--------|
| 🔴 High | **Graceful Error Recovery** | Currently, if one sentence fails (AI or API), processing continues but user gets no actionable feedback. Add structured error states with clear "Retry" options per sentence. | Better UX |
| 🔴 High | **AI Timeout Handling** | Add explicit timeout handling (currently 30s) with fallback to local keyword extraction if AI is unavailable. | Reliability |
| 🟡 Medium | **Circuit Breaker Pattern** | Implement circuit breaker for both Pexels/Pixabay APIs to prevent cascading failures. | Stability |

**Implementation:**
```csharp
// Add to ScriptSentence model
public string? ErrorMessage { get; set; }
public int RetryCount { get; set; }
public bool CanRetry => RetryCount < 3;
```

---

### 1.2 Processing Workflow Improvements

| Priority | Task | Description | Impact |
|----------|------|-------------|--------|
| 🔴 High | **Progress Percentage Display** | Show real progress (e.g., "3/8 sentences processed") instead of just "Processing..." | UX Clarity |
| 🔴 High | **Cancellation Support** | The `_cts` exists but pressing "Back to Editor" doesn't cancel active operations. Implement proper cancellation. | UX Control |
| 🟡 Medium | **Parallel Processing Option** | Currently sequential. Add option for parallel (faster) vs sequential (rate-limit safe) processing. | Performance |
| 🟡 Medium | **Queue System** | For large scripts (10+ segments), show a visual queue with pending/active/complete states. | UX Transparency |

**Implementation:**
```razor
<!-- Progress bar with percentage -->
<div class="progress-container">
    <div class="progress-bar" style="width: @(_completedCount * 100 / _totalCount)%"></div>
    <span class="progress-text">@_completedCount / @_totalCount sentences</span>
</div>
```

---

### 1.3 Keyword & Search Quality

| Priority | Task | Description | Impact |
|----------|------|-------------|--------|
| 🔴 High | **Keyword Editing** | Allow users to manually edit/add keywords before searching. AI suggestions as starting point. | Control |
| 🟡 Medium | **Search Preview** | Show generated keywords before triggering video search, so user can approve/modify. | Quality |
| 🟡 Medium | **Keyword Memory** | Cache successful keyword → video mappings to speed up future similar searches. | Performance |
| 🟢 Low | **Smart Keyword Fallback** | If specific keywords yield 0 results, automatically try progressively broader terms. | Reliability |

---

### 1.4 Video Selection & Management

| Priority | Task | Description | Impact |
|----------|------|-------------|--------|
| 🔴 High | **Video Preview Quality** | Add thumbnail preview grid before loading full videos (saves bandwidth). | Performance |
| 🔴 High | **Alternative Limit** | Currently shows ALL search results in research mode. Limit to 6-8 for usability. | UX |
| 🟡 Medium | **Favorite/Bookmark** | Allow marking videos as "favorites" for reuse across projects. | Productivity |
| 🟡 Medium | **Duration Display** | Show video duration on preview card. | Information |
| 🟢 Low | **Video Deduplication** | Detect if the same stock video is suggested for multiple sentences. | Quality |

---

### 1.5 Project Management

| Priority | Task | Description | Impact |
|----------|------|-------------|--------|
| 🔴 High | **Delete Confirmation** | Add confirmation modal before project deletion. | Safety |
| 🟡 Medium | **Project Duplicate** | Allow cloning a project as starting point for variations. | Productivity |
| 🟡 Medium | **Project Search/Filter** | Add search and filter by date on Projects page. | Usability |
| 🟢 Low | **Export/Import** | Allow exporting project as JSON for backup/sharing. | Portability |

---

## Part 2: UI Polish — Vercel-Style SaaS Design

### 2.1 Design Philosophy

**Vercel's Design Principles:**
- ⬛ **High Contrast** — Pure black (#000) and white (#fff) as primary colors
- 📐 **Clean Lines** — Subtle 1px borders, no heavy shadows
- 🔤 **Typography First** — Strong hierarchy, Inter/Geist font, monospace for code/IDs
- 🎯 **Minimal Decoration** — Solid backgrounds, no gradients/glassmorphism
- ⚡ **Purposeful Animation** — Fast, subtle transitions (150-200ms)
- 🖱️ **Clear Affordances** — Obvious hover states, focus rings

---

### 2.2 Color System (Vercel-Style)

```css
/* Light Mode - Clean & Minimal */
:root {
  --background: 0 0% 100%;           /* Pure white */
  --foreground: 0 0% 0%;             /* Pure black */
  --card: 0 0% 100%;                 /* White cards */
  --card-foreground: 0 0% 0%;
  
  --muted: 0 0% 96%;                 /* Light gray bg */
  --muted-foreground: 0 0% 45%;      /* Gray text */
  
  --border: 0 0% 90%;                /* Subtle borders */
  --input: 0 0% 90%;
  
  --primary: 0 0% 0%;                /* Black primary */
  --primary-foreground: 0 0% 100%;   /* White on black */
  
  --accent: 0 0% 96%;                /* Gray accent */
  --accent-foreground: 0 0% 0%;
  
  --destructive: 0 84% 60%;          /* Red for errors */
  --success: 142 76% 36%;            /* Green for success */
  --warning: 38 92% 50%;             /* Amber for warnings */
  
  --radius: 8px;                     /* Consistent radius */
}

/* Dark Mode - Vercel Dark */
.dark {
  --background: 0 0% 0%;             /* Pure black */
  --foreground: 0 0% 100%;           /* Pure white */
  --card: 0 0% 7%;                   /* Near-black cards */
  --card-foreground: 0 0% 100%;
  
  --muted: 0 0% 12%;                 /* Dark gray bg */
  --muted-foreground: 0 0% 65%;      /* Lighter gray text */
  
  --border: 0 0% 18%;                /* Subtle dark borders */
  --input: 0 0% 18%;
  
  --primary: 0 0% 100%;              /* White primary */
  --primary-foreground: 0 0% 0%;     /* Black on white */
  
  --accent: 0 0% 12%;                /* Dark accent */
  --accent-foreground: 0 0% 100%;
}
```

---

### 2.3 Typography System

```css
/* Font Stack - Vercel uses Geist, we'll use Inter */
:root {
  --font-sans: "Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  --font-mono: "Geist Mono", "JetBrains Mono", "Fira Code", monospace;
}

/* Typography Scale */
.text-xs    { font-size: 12px; line-height: 16px; }
.text-sm    { font-size: 14px; line-height: 20px; }
.text-base  { font-size: 16px; line-height: 24px; }
.text-lg    { font-size: 18px; line-height: 28px; }
.text-xl    { font-size: 20px; line-height: 28px; }
.text-2xl   { font-size: 24px; line-height: 32px; }
.text-3xl   { font-size: 30px; line-height: 36px; }

/* Monospace for IDs, codes, technical info */
.font-mono {
  font-family: var(--font-mono);
  font-feature-settings: "ss01", "ss02";
}
```

---

### 2.4 Component Styles (Vercel-Style)

#### Buttons
```css
/* Primary Button - Solid Black/White */
.btn-primary {
  background: hsl(var(--primary));
  color: hsl(var(--primary-foreground));
  border: none;
  border-radius: var(--radius);
  padding: 0 16px;
  height: 40px;
  font-size: 14px;
  font-weight: 500;
  transition: opacity 0.15s ease;
}
.btn-primary:hover:not(:disabled) {
  opacity: 0.9;
}
.btn-primary:focus-visible {
  outline: 2px solid hsl(var(--foreground));
  outline-offset: 2px;
}

/* Secondary Button - Outlined */
.btn-secondary {
  background: transparent;
  color: hsl(var(--foreground));
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  padding: 0 16px;
  height: 40px;
  font-size: 14px;
  font-weight: 500;
  transition: background 0.15s ease, border-color 0.15s ease;
}
.btn-secondary:hover:not(:disabled) {
  background: hsl(var(--muted));
  border-color: hsl(var(--foreground) / 0.2);
}
```

#### Cards
```css
/* Card - Clean with subtle border */
.card {
  background: hsl(var(--card));
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  padding: 24px;
  transition: border-color 0.15s ease;
}
.card:hover {
  border-color: hsl(var(--foreground) / 0.2);
}

/* No shadows by default - Vercel uses minimal shadows */
```

#### Inputs
```css
/* Input - Clean border style */
.input {
  background: transparent;
  border: 1px solid hsl(var(--border));
  border-radius: var(--radius);
  padding: 0 12px;
  height: 40px;
  font-size: 14px;
  transition: border-color 0.15s ease;
}
.input:hover {
  border-color: hsl(var(--foreground) / 0.3);
}
.input:focus {
  outline: none;
  border-color: hsl(var(--foreground));
}
```

#### Badges/Tags
```css
/* Badge - Minimal pill style */
.badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  font-size: 12px;
  font-weight: 500;
  border-radius: 9999px;
  background: hsl(var(--muted));
  color: hsl(var(--muted-foreground));
}

/* Monospace badge for IDs */
.badge-mono {
  font-family: var(--font-mono);
  font-size: 11px;
  letter-spacing: -0.02em;
}
```

---

### 2.5 Animation Guidelines (Vercel-Style)

```css
/* Fast, subtle transitions - no bouncy effects */
:root {
  --transition-fast: 0.15s ease;
  --transition-normal: 0.2s ease;
}

/* Hover state - subtle background change, NOT transforms */
.interactive:hover {
  background: hsl(var(--muted));
}

/* Loading spinner - simple rotation */
@keyframes spin {
  to { transform: rotate(360deg); }
}
.spinner {
  animation: spin 1s linear infinite;
}

/* Skeleton - subtle pulse, NOT shimmer */
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}
.skeleton {
  background: hsl(var(--muted));
  animation: pulse 2s ease-in-out infinite;
}

/* Page enter - simple fade */
@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}
.animate-in {
  animation: fadeIn 0.2s ease;
}
```

---

### 2.6 Component Redesigns

#### 2.6.1 Header (Vercel-Style)
- Solid background (black in dark mode, white in light mode)
- 1px bottom border
- Logo: Clean wordmark, no icon animation
- Nav links: Simple text, underline on hover
- Active state: Bold weight or subtle background pill

```css
.header {
  position: sticky;
  top: 0;
  z-index: 50;
  background: hsl(var(--background));
  border-bottom: 1px solid hsl(var(--border));
  height: 64px;
}

.nav-link {
  color: hsl(var(--muted-foreground));
  font-size: 14px;
  font-weight: 400;
  transition: color 0.15s ease;
}
.nav-link:hover {
  color: hsl(var(--foreground));
}
.nav-link.active {
  color: hsl(var(--foreground));
  font-weight: 500;
}
```

#### 2.6.2 Home Page - Composer View
| Element | Current | Vercel-Style |
|---------|---------|--------------|
| Title | Plain text | Large, bold, no gradient |
| Subtitle | Gray text | Muted foreground, clean |
| Card | Border + shadow | 1px border only, no shadow |
| Mood Buttons | Icon buttons | Text + icon, clear active state |
| Generate Button | Basic primary | Solid black/white, opacity hover |

#### 2.6.3 Results View - Sentence Cards
| Element | Current | Vercel-Style |
|---------|---------|--------------|
| Sentence Number | Basic badge | Monospace pill `#01` |
| Keywords | Colored tags | Gray pills, simple |
| Video Preview | Rounded player | Sharp corners (4-8px radius) |
| Action Buttons | Text buttons | Icon buttons with border |

#### 2.6.4 Projects Page
| Element | Improvement |
|---------|-------------|
| Project Card | 1px border, hover border darkens |
| Date | Monospace, muted color |
| Status | Simple dot indicator (green/gray) |
| Actions | Icon buttons, outlined style |

---

### 2.7 Empty & Error States

| State | Vercel-Style Implementation |
|-------|---------------------------|
| No Projects | Centered text, muted icon, single CTA button |
| No Results | Simple message, "No videos found for these keywords" |
| API Error | Inline error message with red border, retry button |
| Loading | Simple spinner or pulsing skeleton, no shimmer |

---

### 2.8 Interactive Feedback

| Priority | Task | Vercel-Style Approach |
|----------|------|----------------------|
| 🔴 High | **Toast Notifications** | Bottom-right, minimal style, auto-dismiss |
| 🔴 High | **Confirmation Modals** | Centered, simple, clear primary/secondary actions |
| 🟡 Medium | **Loading States** | Inline spinners, skeleton placeholders |
| 🟡 Medium | **Focus States** | 2px outline offset, high contrast |

---

## Part 3: Implementation Phases

### Phase 1: Foundation (Week 1)
1. ⬜ Vercel color system & CSS variables
2. ⬜ Typography scale with Inter/Geist Mono
3. ⬜ Button & input component styles
4. ⬜ Progress percentage display
5. ⬜ Delete confirmation modal

### Phase 2: Core UX (Week 2)
1. ⬜ Keyword editing before search
2. ⬜ Video duration display
3. ⬜ Alternative video limit (6-8)
4. ⬜ Error recovery per sentence
5. ⬜ Toast notification system

### Phase 3: Polish (Week 3)
1. ⬜ Header with active nav indicator
2. ⬜ Card hover states (border only)
3. ⬜ Monospace styling for IDs/technical info
4. ⬜ Projects page enhancements
5. ⬜ Clean empty states

### Phase 4: Advanced (Week 4+)
1. ⬜ Project duplication
2. ⬜ Favorites system
3. ⬜ Keyboard shortcuts
4. ⬜ Export/Import projects
5. ⬜ Proper cancellation handling

---

## Part 4: Quick Wins (Can Do Today)

High impact, minimal effort changes for Vercel-style UI:

1. **Pure Black/White Colors** - Update CSS variables to true black/white
2. **Remove Shadows** - Replace box-shadows with 1px borders
3. **Monospace IDs** - Add `font-mono` class to sentence numbers, project IDs
4. **Button Opacity Hover** - Replace transform with opacity change
5. **Skeleton Pulse** - Replace shimmer with simple pulse
6. **Border Hover States** - Cards/buttons darken border on hover
7. **Progress Counter** - Add "3 of 8" text during processing
8. **Delete Confirmation** - Simple modal with outlined Cancel, solid Delete

---

## Appendix: File Modification Map

| File | Changes Needed |
|------|----------------|
| `App.razor` | Vercel color variables, typography, animations |
| `wwwroot/app.css` | Clean button/card/input styles |
| `Home.razor` | Progress display, simplified card style |
| `Projects.razor` | Delete confirmation, monospace dates |
| `MainLayout.razor` | Clean header with active nav |
| `Models/ScriptSentence.cs` | Error message, retry count fields |
| `PipelineOrchestrator.cs` | Progress events with counts |

---

## Design Reference

**Vercel Elements to Emulate:**
- [vercel.com](https://vercel.com) - Homepage layout, typography
- [vercel.com/dashboard](https://vercel.com/dashboard) - Card style, data tables
- [nextjs.org/docs](https://nextjs.org/docs) - Navigation, code blocks
- [geist-ui.dev](https://geist-ui.dev) - Component library reference

**Key Visual Traits:**
- High contrast black/white
- 1px borders, no shadows
- Monospace for technical elements
- Fast, subtle animations
- Generous whitespace
- Clear visual hierarchy

---

## Success Metrics

After implementation, the app should:
1. 🖤 Look clean and professional like Vercel/Linear
2. 🎯 Provide clear feedback at every step
3. 🔄 Allow recovery from any error state
4. ⚡ Feel fast with 150-200ms transitions
5. 📐 Maintain visual consistency with the design system

---

*Plan created for BunBun B-Roll Generator v2.0 — Vercel-Style SaaS UI*
