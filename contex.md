Here is the detailed architectural plan for your **Local AI B-Roll Automation Engine**.

This plan focuses on the **What** (Architecture/Components) and the **How** (Logic/Data Flow), ensuring a clean backend separation of concerns without getting bogged down in syntax.

---

use this local gemini 2.5 flash
C:\Users\omega>curl http://127.0.0.1:8317/v1/models ^
More?   -H "Authorization: Bearer sk-dummy"
{"data":[{"created":1750118400,"id":"gemini-2.5-flash","object":"model","owned_by":"google"},{"created":1753142400,"id":"gemini-2.5-flash-lite","object":"model","owned_by":"google"},{"created":1737158400,"id":"gemini-3-pro-preview","object":"model","owned_by":"google"},{"created":1765929600,"id":"gemini-3-flash-preview","object":"model","owned_by":"google"},{"created":1750118400,"id":"gemini-2.5-pro","object":"model","owned_by":"google"}],"object":"list"}

# Project Plan: Auto-Broll Engine

## 1. High-Level Architecture

**Goal:** Create a robust, modular .NET backend that acts as the orchestration layer between a raw video script, a Local LLM (Intelligence), and External APIs (Assets).

### The Stack

* **Core:** .NET 8/9 Web API (Modular Monolith).
* **Intelligence:** Local Gemini 2.5 Flash (via `127.0.0.1:8317`).
* **Asset Provider:** Pexels API (Primary), Pixabay (Secondary/Future).
* **Storage:** Local File System (Structured for Video Editors).

---

## 2. Core Modules (The "What")

We will split the application into three distinct domains to prevent "spaghetti code."

### A. The Script Processor (Input Domain)

* **Responsibility:** Ingest raw text and break it down into actionable "units" of work.
* **Logic:** It should not care about AI or Video. It only cares about text structure.
* **Key Challenge:** Determining where one scene ends and another begins.

### B. The Intelligence Layer (Brain Domain)

* **Responsibility:** Interfacing with your Local LLM (`gemini-2.5-flash`).
* **Logic:** Converts generic sentences (Indonesian/English) into optimized, searchable English keywords.
* **Configuration:** Must read `GOOGLE_GEMINI_BASE_URL` from the environment to remain portable.

### C. The Asset Broker (External Domain)

* **Responsibility:** Negotiating with the Pexels API.
* **Logic:** Handling authentication, searching, filtering for quality (1080p/720p), and managing rate limits.
* **Output:** Direct download links, not the files themselves.

### D. The Downloader (Infrastructure Domain)

* **Responsibility:** reliably moving bytes from a URL to a physical disk path.
* **Logic:** Streaming data (low memory footprint), handling retries, and ensuring file integrity.

---

## 3. The Automation Pipeline (The "How")

This is the step-by-step lifecycle of a single request.

### Step 1: Segmentation

* **Input:** A full video script (e.g., a `.txt` file or a text blob).
* **How:** The system splits the text by newlines or punctuation.
* **Output:** A list of objects: `[{ ID: 1, Text: "Intro..." }, { ID: 2, Text: "Main point..." }]`.

### Step 2: Contextual Extraction (The AI Step)

* **Input:** A specific text segment (e.g., "Wanita sedang sholat malam").
* **How:**
* Send request to `127.0.0.1:8317`.
* **System Prompt Strategy:** Strict instruction to output *only* JSON or CSV. No "Here are your keywords" chat.
* **Model:** Use `gemini-2.5-flash` for sub-second latency.


* **Output:** Normalized Keywords: `["muslim woman", "praying", "night", "silouhette"]`.

### Step 3: Search & Filter (The API Step)

* **Input:** Keywords.
* **How:**
* Query Pexels Search API.
* **Filter Logic:** Apply strict filters *before* accepting a result.
* *Orientation:* Landscape (16:9).
* *Duration:* 5s - 30s (discard very short clips).
* *Resolution:* Prefer HD (1920x1080) or HD-Ready (1280x720). Avoid 4K (too large) or SD (too blurry).




* **Output:** A direct `.mp4` URL.

### Step 4: Acquisition & Organization

* **Input:** `.mp4` URL.
* **How:**
* Stream the response body directly to a `FileStream`.
* **Naming Convention:** Crucial for editing.
* Format: `/{ProjectName}/{SequenceID}_{KeywordSlug}.mp4`
* Example: `/YaumiPromo/03_praying_woman.mp4`




* **Outcome:** A file ready for Premiere/DaVinci Resolve.

---

## 4. Resilience & Edge Cases

Since this is a backend solution, we must handle failure gracefully.

* **The "Zero Results" Scenario:**
* *Problem:* AI suggests keywords that yield 0 results on Pexels.
* *Strategy:* Implement a "Fallback Loop." If 0 results, ask AI for *broader* keywords and retry once.


* **Rate Limiting:**
* *Problem:* Pexels blocks requests if sent too fast.
* *Strategy:* Implement a generic "Polly" retry policy with exponential backoff (wait 1s, then 2s, then 4s).


* **Local LLM Availability:**
* *Problem:* The local server (127.0.0.1) might be down or loading.
* *Strategy:* Quick timeout (e.g., 2 seconds) to fail fast and report "Check Local Server."



---

## 5. Directory Structure Strategy

This structure ensures you can drag and drop folders directly into video editors.

```text
/Broll_Workspace
    /Active_Project_Name (e.g., "Yaumi_Feature_Intro")
        /00_Script.txt          <-- Source of Truth
        /01_Scene_Intro
             /selected_clip.mp4
             /alternative_clip_1.mp4
        /02_Scene_Problem
             /selected_clip.mp4
        /logs
             /generation_log.json

```

---

## 6. Future Scalability (Day 2)

Once the core is working, these "How" items can be added:

1. **Duration Matching:** If the script segment is long (approx 150 words), download *two* clips for that scene instead of one.
2. **Style Consistency:** Inject a "Mood" parameter into the AI prompt (e.g., "Cinematic", "Moody", "Bright") to keep visual consistency across all clips.
3. **Hash Check:** Calculate file hashes to avoid re-downloading the same stock footage for different projects.

---

### Next Step

Would you like to move to the **Implementation Phase** for the **Script Processor** (Step 1) or the **Intelligence Layer** (Step 2) first?