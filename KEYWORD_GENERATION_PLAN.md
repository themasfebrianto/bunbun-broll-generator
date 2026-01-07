# Keyword Generation System Improvement Plan

> **Document Version:** 1.0  
> **Date:** 2026-01-07  
> **Status:** Planning Phase  

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Current System Analysis](#current-system-analysis)
3. [Script Interpretation Framework](#script-interpretation-framework)
4. [Keyword Extraction Rules](#keyword-extraction-rules)
5. [Platform Mapping Strategy](#platform-mapping-strategy)
6. [Keyword Formatting Guidelines](#keyword-formatting-guidelines)
7. [Practical Examples](#practical-examples)
8. [Optimization Strategies](#optimization-strategies)
9. [Implementation Roadmap](#implementation-roadmap)

---

## Executive Summary

This document outlines a comprehensive strategy for improving the B-roll keyword generation system used in the BunBun B-Roll Generator. The goal is to produce keywords that:

- **Accurately reflect** the intent and message of each script segment
- **Optimize search results** on Pexels and Pixabay platforms
- **Align with platform conventions** to maximize relevant footage discovery

---

## Current System Analysis

### Existing Implementation

The current `IntelligenceService.cs` uses a Gemini LLM with a system prompt to extract keywords. Key observations:

| Aspect | Current State | Improvement Opportunity |
|--------|---------------|------------------------|
| Keyword count | 5-7 per segment | Add layered approach (primary/fallback) |
| Context awareness | Basic mood parameter | Structured multi-dimensional analysis |
| Platform alignment | Generic approach | Platform-specific optimization |
| Fallback strategy | Text extraction only | Semantic fallback chains |

### Identified Gaps

1. **No category mapping** — Keywords don't map to Pexels/Pixabay categories
2. **Limited context layers** — Missing action/verb differentiation
3. **No keyword prioritization** — All keywords treated equally
4. **Single keyword set** — No fallback or alternative sets

---

## Script Interpretation Framework

### Multi-Dimensional Analysis

Each script segment should be analyzed across four dimensions:

```
┌─────────────────────────────────────────────────────────────┐
│                    SCRIPT SEGMENT                           │
├─────────────┬─────────────┬─────────────┬──────────────────┤
│   CONTEXT   │   EMOTION   │   ACTION    │     TOPIC        │
│  (Setting)  │   (Mood)    │   (Verb)    │   (Subject)      │
├─────────────┼─────────────┼─────────────┼──────────────────┤
│ - Location  │ - Feeling   │ - Movement  │ - Main subject   │
│ - Time      │ - Tone      │ - Activity  │ - Theme          │
│ - Environ.  │ - Intensity │ - Gesture   │ - Object         │
└─────────────┴─────────────┴─────────────┴──────────────────┘
```

### Analysis Rules

#### 1. Context Analysis
- **Location Cues:** Identify physical settings (bedroom, office, street, nature)
- **Time Indicators:** Detect temporal references (morning, night, sunset, midnight)
- **Environmental Factors:** Weather, lighting, atmosphere (rainy, foggy, bright)

#### 2. Emotion Analysis
Map emotional content to visual metaphors:

| Script Emotion | Visual Keywords |
|----------------|-----------------|
| Sadness/Loneliness | empty street, rain window, fog, solitary figure |
| Anxiety/Stress | clock, crowds, papers, chaos, deadline |
| Hope/Optimism | sunrise, light rays, horizon, birds flying |
| Calm/Peace | still water, candles, slow motion, nature |
| Anger/Frustration | storm, breaking, fast cuts, red tones |

#### 3. Action Analysis
Extract verbs and translate to visual movements:

| Script Verb | B-Roll Action |
|-------------|---------------|
| Thinking/Reflecting | person sitting, staring window, walking slowly |
| Struggling | climbing stairs, pushing, resistance |
| Waiting | clock, sitting, empty bench, queue |
| Moving forward | walking, driving, road, journey |
| Remembering | old photos, flashback style, blurry transitions |

#### 4. Topic Analysis
Identify the core subject matter:

```
PRIMARY TOPICS:
- Personal (self, identity, emotions)
- Relational (family, friends, love)
- Professional (work, career, success)
- Existential (life, death, meaning, time)
- Material (objects, possessions, places)
```

---

## Keyword Extraction Rules

### Rule 1: Main Subject Keywords

**Definition:** The primary visual element that represents the script's core message.

**Extraction Guidelines:**
- Identify the **noun** that is central to the segment
- Add **context modifiers** to avoid generic matches
- Use **2-3 word combinations** for specificity

**Examples:**
| Script Phrase | ❌ Too Generic | ✅ Specific Keyword |
|---------------|----------------|---------------------|
| "I stare at the ceiling" | ceiling | bedroom ceiling lying |
| "The city never sleeps" | city | city skyline night lights |
| "My heart feels heavy" | heart | person chest holding sad |

### Rule 2: Supporting/Contextual Keywords

**Definition:** Secondary elements that provide atmosphere and setting.

**Extraction Guidelines:**
- Extract **setting descriptors** (indoor/outdoor, urban/nature)
- Include **time-of-day** markers
- Add **environmental modifiers**

**Pattern:**
```
[Setting] + [Atmospheric Modifier] + [Time/Weather]
```

**Examples:**
- "Dark apartment room night"
- "Empty street rain evening"
- "Cozy bedroom morning light"

### Rule 3: Mood/Emotion Keywords

**Definition:** Abstract emotional states translated to visual metaphors.

**Extraction Guidelines:**
- Never use emotion words alone (sad, happy, angry)
- Combine emotion with **visual representation**
- Use **symbolic imagery** that evokes the emotion

**Emotion-to-Visual Mapping:**

```yaml
Melancholic:
  - rain drops window
  - autumn leaves falling
  - empty bench park
  - fog morning city
  - person silhouette window

Anxious/Stressed:
  - clock ticking closeup
  - crowded subway people
  - papers flying desk
  - phone notifications screen
  - sleepless night insomnia

Hopeful/Optimistic:
  - sunrise timelapse city
  - seedling growing plant
  - open road driving
  - light breaking clouds
  - spring flowers blooming

Peaceful/Calm:
  - lake reflection sunset
  - meditation breathing
  - coffee morning quiet
  - gentle waves beach
  - candlelight dark room
```

### Rule 4: Action/Verb-Based Keywords

**Definition:** Movement and activity that matches the script's dynamics.

**Extraction Guidelines:**
- Translate **abstract verbs** to **concrete visual actions**
- Include **motion descriptors** (slow motion, timelapse, steady)
- Consider **camera movement** (panning, tracking, static)

**Verb Translation Matrix:**

| Abstract Verb | Physical Action Keyword |
|---------------|------------------------|
| Thinking | person gazing window, head in hands, walking alone |
| Struggling | climbing stairs effort, pushing heavy, walking against wind |
| Growing | plant timelapse, building construction, sunrise progression |
| Falling | autumn leaves, rain drops, person collapsing |
| Searching | person looking around, hands searching, scrolling phone |
| Waiting | clock face, person sitting bench, empty hallway |

---

## Platform Mapping Strategy

### Pexels Categories

Pexels organizes content into these main categories. Map script intent accordingly:

| Pexels Category | Intent Mapping | Keyword Style |
|-----------------|----------------|---------------|
| **Business** | Work, career, professional life | office, meeting, laptop, teamwork |
| **Technology** | Digital life, modern world | smartphone, computer, coding, data |
| **People** | Personal stories, emotions | portrait, silhouette, crowd, hands |
| **Nature** | Metaphorical, peaceful, time | forest, ocean, sunset, mountains |
| **Urban/City** | Modern life, isolation, busy | skyline, street, traffic, buildings |
| **Food & Drink** | Comfort, culture, routine | coffee, cooking, restaurant |
| **Travel** | Journey, adventure, escape | road, airplane, maps, destinations |
| **Abstract** | Emotions, concepts | light, shadows, textures, patterns |

### Pixabay Categories

Pixabay has similar but slightly different categorization:

| Pixabay Category | Preference Keywords |
|------------------|---------------------|
| **Backgrounds** | abstract, texture, gradient, pattern |
| **Emotions** | Use scene-based: sunset, storm, sunshine |
| **Industry** | factory, office, manufacturing |
| **Lifestyle** | home, family, routine, daily |
| **Motion** | timelapse, slow motion, tracking |

### Tag Style Conventions

Both platforms favor certain tagging patterns:

```
PREFERRED FORMAT:
✅ Two-word combinations: "ocean waves", "city night"
✅ Descriptive pairs: "lonely street", "busy traffic"
✅ Action + Subject: "walking person", "falling leaves"

AVOID:
❌ Single abstract words: "sadness", "hope", "anxiety"
❌ Long phrases: "person feeling sad in the rain"
❌ Non-visual concepts: "memory", "thought", "idea"
```

---

## Keyword Formatting Guidelines

### 1. Singular vs. Plural

| Use Case | Preference | Example |
|----------|------------|---------|
| Countable objects | **Singular** | tree, building, person |
| Natural phenomena | **Singular** | rain, fog, snow |
| Collective scenes | **Plural** | cars traffic, people crowd |
| Abstract elements | **Singular** | light, shadow, smoke |

**Rationale:** Singular keywords often return more diverse results on stock platforms.

### 2. Generic vs. Specific Terms

Follow the **Goldilocks Principle:**

```
TOO GENERIC          JUST RIGHT              TOO SPECIFIC
     ↓                    ↓                       ↓
   "room"        "bedroom morning light"    "blue bedroom ikea"
   "person"      "person silhouette window" "asian woman crying"
   "nature"      "forest path morning"      "redwood california trail"
```

**Sweet Spot:** 2-3 descriptive words that balance searchability with relevance.

### 3. Avoiding Abstract Words

Replace abstract concepts with concrete visuals:

| ❌ Abstract | ✅ Concrete Visual |
|-------------|-------------------|
| sadness | rain window, wilting flower, empty chair |
| time passing | clock timelapse, candle burning, aging |
| loneliness | empty room, single figure, isolated |
| pressure | weight, crowd pushing, deadline clock |
| freedom | birds flying, open road, breaking chains |

### 4. Word Order Best Practices

**Preferred Order:** `[Subject] + [Action/State] + [Context]`

- "person walking rain" > "rain walking person"
- "city skyline night" > "night city skyline"
- "coffee steam morning" > "morning steam coffee"

---

## Practical Examples

### Example 1: Emotional/Introspective Script

**Script (Indonesian):**
> "Langit-langit kamar seolah menatap balik, mengingatkan pada daftar masalah yang tak kunjung selesai."

**Analysis:**
- **Context:** Bedroom, lying down, night/contemplative
- **Emotion:** Overwhelmed, anxiety, stress
- **Action:** Staring, thinking, unable to sleep
- **Topic:** Personal problems, mental burden

**Generated Keywords:**

| Layer | Keywords |
|-------|----------|
| **Primary (Exact Visual)** | person lying bed staring ceiling, bedroom ceiling insomnia |
| **Mood (Emotional)** | dark room anxiety, overwhelmed thoughts night |
| **Contextual (Setting)** | dim bedroom evening, apartment room shadows |
| **Fallback (Abstract)** | sleepless night, clock ticking dark, insomnia stress |

**Category Mapping:** People → Lifestyle → Abstract

---

### Example 2: External/Observational Script

**Script (Indonesian):**
> "Di luar, dunia berputar tanpa henti. Orang-orang sibuk dengan urusan masing-masing."

**Analysis:**
- **Context:** Urban, outdoor, daytime/busy
- **Emotion:** Detachment, observation, feeling small
- **Action:** Moving, rushing, walking, commuting
- **Topic:** Society, crowd, modern life

**Generated Keywords:**

| Layer | Keywords |
|-------|----------|
| **Primary (Exact Visual)** | busy city crowd walking, people timelapse street |
| **Mood (Emotional)** | urban rush disconnected, city life overwhelm |
| **Contextual (Setting)** | downtown pedestrians day, subway station crowd |
| **Fallback (Abstract)** | motion blur people, traffic flow, time passing city |

**Category Mapping:** Urban → People → Motion

---

### Example 3: Hopeful/Transitional Script

**Script (English):**
> "But then, a small light appeared. Maybe tomorrow will be different."

**Analysis:**
- **Context:** Transition moment, dawn/new beginning
- **Emotion:** Hope, cautious optimism, possibility
- **Action:** Light appearing, dawn breaking, change
- **Topic:** Hope, future, change

**Generated Keywords:**

| Layer | Keywords |
|-------|----------|
| **Primary (Exact Visual)** | light through window, sunrise bedroom curtains |
| **Mood (Emotional)** | hope morning light, new beginning dawn |
| **Contextual (Setting)** | sun rays room, golden hour indoor |
| **Fallback (Abstract)** | candle lighting darkness, clouds parting sun, spring bloom |

**Category Mapping:** Nature → Abstract → Lifestyle

---

## Optimization Strategies

### Strategy 1: Fallback Keyword Chains

Implement a **cascading search strategy** with progressively broader keywords:

```
SEARCH CASCADE:
┌─────────────────────────────────────────┐
│ Level 1: Specific (e.g., "person lying  │
│          bed staring ceiling bedroom")  │
│                    ↓                    │
│         No results? Try Level 2         │
├─────────────────────────────────────────┤
│ Level 2: Moderate (e.g., "bedroom       │
│          person insomnia")              │
│                    ↓                    │
│         No results? Try Level 3         │
├─────────────────────────────────────────┤
│ Level 3: Broad (e.g., "dark room night")│
│                    ↓                    │
│         No results? Try Level 4         │
├─────────────────────────────────────────┤
│ Level 4: Generic Safe (e.g., "room      │
│          night", "person silhouette")   │
└─────────────────────────────────────────┘
```

### Strategy 2: Broad-to-Specific Layering

Generate keywords in **concentric circles** of specificity:

```yaml
Keyword Layers:
  Core (Most Specific):
    - person lying bed ceiling insomnia bedroom
    - sleepless worried thoughts night
  
  Middle (Moderate):
    - bedroom night thinking
    - person bed contemplating
    
  Outer (Broadest - Safe Fallback):
    - dark room
    - night bedroom
    - person silhouette
```

### Strategy 3: Multiple Keyword Sets Per Segment

Generate **parallel keyword sets** for different search strategies:

```yaml
Segment: "The night felt endless and lonely"

Set A - Literal:
  - lonely night bedroom
  - empty room dark
  - single person window

Set B - Metaphorical:
  - endless road night
  - stars dark sky
  - candle burning alone

Set C - Abstract/Safe:
  - night timelapse
  - shadows light
  - moon clouds
```

### Strategy 4: Platform-Specific Keyword Variations

Create variants optimized for each platform:

| Platform | Optimization | Example |
|----------|--------------|---------|
| **Pexels** | Focus on aesthetic/cinematic | "cinematic bedroom moody" |
| **Pixabay** | Focus on descriptive/practical | "dark bedroom night interior" |

### Strategy 5: Content Safety Filters

Pre-filter keywords to avoid unintended content:

```yaml
Avoid Generic Terms That Return Religious Content:
  - ceiling → bedroom ceiling, apartment ceiling
  - dome → city skyline, building exterior
  - architecture → modern building, home interior

Avoid Terms That May Return NSFW:
  - Add context: "bedroom" → "bedroom morning light"
  - Use safe modifiers: home, family, office, work

Safe Fallback Keywords (Always Work):
  - clouds timelapse
  - city skyline night
  - nature landscape
  - coffee morning aesthetic
  - person silhouette window
  - rain drops glass
```

---

## Implementation Roadmap

### Phase 1: Prompt Engineering (Quick Win)

**Effort:** Low | **Impact:** High

1. Update `SystemPrompt` in `IntelligenceService.cs`
2. Add structured output format for keyword layers
3. Include platform category hints

**New Prompt Structure:**
```json
{
  "primary_keywords": ["...", "..."],
  "mood_keywords": ["...", "..."],
  "fallback_keywords": ["...", "..."],
  "suggested_category": "People/Nature/Urban"
}
```

### Phase 2: Keyword Model Enhancement

**Effort:** Medium | **Impact:** Medium

1. Create `KeywordSet` model with layers
2. Implement cascading search in `AssetBroker`
3. Add keyword scoring/ranking

### Phase 3: Platform-Specific Optimization

**Effort:** Medium | **Impact:** High

1. Create separate keyword generators for Pexels/Pixabay
2. Implement category pre-filtering
3. Add platform-specific search parameters

### Phase 4: Machine Learning Enhancement (Future)

**Effort:** High | **Impact:** High

1. Collect search success/failure data
2. Train keyword effectiveness model
3. Implement adaptive keyword generation

---

## Appendix A: Common Stock Footage Categories

### Pexels Popular Categories
- People & Portraits
- Nature & Landscapes
- Urban & Architecture
- Business & Technology
- Food & Lifestyle
- Abstract & Textures
- Travel & Adventure
- Emotions & Concepts

### Pixabay Popular Categories
- Backgrounds/Textures
- Industry/Craft
- Nature/Landscapes
- People/Emotions
- Buildings/Landmarks
- Sports/Fitness
- Animals/Wildlife
- Technology/Science

---

## Appendix B: Multi-Language Support

For Indonesian-to-English translation, maintain a **domain-specific dictionary:**

```yaml
Indonesian Terms:
  kamar: bedroom, room
  langit-langit: ceiling (use with context: bedroom ceiling)
  jendela: window (add: apartment window, bedroom window)
  gelap: dark, dim
  malam: night, evening
  sepi: lonely, empty, quiet
  berat: heavy, weight, burden
  rindu: longing, missing, nostalgic
  takut: fear, anxious, worried
  marah: angry, frustrated
  bahagia: happy, joyful
  tenang: calm, peaceful, serene
```

---

## Appendix C: Keyword Quality Checklist

Before finalizing keywords, verify:

- [ ] No single-word generic terms (ceiling, room, person)
- [ ] All keywords are in English
- [ ] 2-3 words per keyword combination
- [ ] Context modifiers included (bedroom, city, night)
- [ ] Emotion keywords have visual representations
- [ ] At least one safe fallback keyword included
- [ ] No potentially offensive or inappropriate terms
- [ ] Platform-appropriate formatting (lowercase, no special chars)

---

*Document maintained by: BunBun B-Roll Generator Team*  
*Last updated: 2026-01-07*
