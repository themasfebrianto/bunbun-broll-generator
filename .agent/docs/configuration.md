# Configuration Reference

All configuration is in `appsettings.json` (or `appsettings.Development.json` for dev overrides).

---

## Full Configuration Schema

```jsonc
{
  // Standard ASP.NET logging
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "BunbunBroll.Services": "Debug"  // Verbose service logging
    }
  },

  // Simple email/password auth
  "Auth": {
    "Email": "",
    "Password": ""
  },

  // LLM Configuration (OpenAI-compatible endpoint)
  "Gemini": {
    "BaseUrl": "http://127.0.0.1:8317",    // CLI Proxy API endpoint
    "Model": "gemini-3-pro-preview",
    "ApiKey": "scriptflow_gemini_pk_local",
    "TimeoutSeconds": 120
  },

  // Stock Video APIs
  "Pexels": {
    "ApiKey": "<api-key>",
    "BaseUrl": "https://api.pexels.com/"
  },
  "Pixabay": {
    "ApiKey": "<api-key>",
    "BaseUrl": "https://pixabay.com/api/videos/"
  },

  // Video Download
  "Downloader": {
    "OutputDirectory": "Broll_Workspace"
  },

  // FFmpeg Configuration
  "FFmpeg": {
    "BinaryDirectory": "./ffmpeg-binaries",  // Auto-download location
    "TempDirectory": "./temp/ffmpeg",        // Temporary processing files
    "UseHardwareAccel": true,                // Try hardware encoding
    "Preset": "veryfast",                    // x264 preset (speed vs quality)
    "ParallelClips": 3,                      // Concurrent clip processing
    "CRF": 23                                // Quality (18=high, 35=low)
  },

  // Short Video Output
  "ShortVideo": {
    "DefaultDuration": 30,             // Default target seconds
    "OutputDirectory": "./output/shorts"
  },

  // Script Pattern Files
  "Patterns": {
    "Directory": "patterns"   // Relative to project root
  },

  // Script Output Paths
  "ScriptOutput": {
    "BaseDirectory": "output/scripts",
    "ExportDirectory": "output/exports"
  },

  // Available YouTube channels
  "Channels": ["Riwayat Umat"],

  // Whisk (Google Imagen) Image Generation
  "Whisk": {
    "Cookie": "<session-cookie>",     // Auth (override: WHISK_COOKIE env var)
    "EnableImageGeneration": true,    // Feature toggle
    "AspectRatio": "LANDSCAPE",       // LANDSCAPE | PORTRAIT | SQUARE
    "Model": "IMAGEN_3_5",           // Imagen model version
    "OutputDirectory": "output/whisk-images"
  }
}
```

---

## Environment Variable Overrides

| Variable | Overrides | Purpose |
|----------|-----------|---------|
| `WHISK_COOKIE` | `Whisk:Cookie` | Session cookie for Whisk API auth |

---

## Database

| Setting | Value |
|---------|-------|
| Provider | SQLite |
| File | `bunbun.db` (project root) |
| Framework | EF Core |
| Migrations | Auto-created via `db.Database.EnsureCreated()` + manual `ALTER TABLE` in `Program.cs` |

---

## Static File Serving

| URL Path | Physical Path | Purpose |
|----------|--------------|---------|
| `/project-assets/*` | `output/` | Serve generated images, videos, exports |

Configured in `Program.cs`:
```csharp
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "output")),
    RequestPath = "/project-assets"
});
```

---

## CLI Proxy API

The Gemini LLM is accessed via a **CLI Proxy API** that provides an OpenAI-compatible REST endpoint.

**Default**: `http://127.0.0.1:8317`

**Setup docs**: See `CLI_PROXY_SETUP.md` in project root.

**Start command** (from user's terminal):
```bash
cd ~/CLIProxyAPI && ./cli-proxy-api
```

---

## Pattern Configuration Files

Located in `patterns/` directory. Each JSON file defines a script generation pattern.

**Example**: `patterns/jazirah-ilmu.json`

**Schema**:
```jsonc
{
  "name": "Jazirah Ilmu",
  "description": "Islamic knowledge video format",
  "phases": [
    {
      "id": "opening",
      "name": "Pembukaan",
      "order": 1,
      "durationTarget": { "min": 30, "max": 60 },
      "wordCountTarget": { "min": 80, "max": 150 },
      "guidance": "...",
      "customRules": { "tone": "engaging", "hook": true }
    }
    // ... more phases
  ],
  "globalRules": {
    "language": "Indonesian",
    "style": "poetic narrative",
    "audience": "Indonesian Muslim viewers"
    // ... more rules
  },
  "closingFormula": "Wallahu a'lam bish-shawab...",
  "productionChecklist": {
    "items": ["Verify timestamps", "Check word count"]
  }
}
```

---

## Development vs Production

| Aspect | Development | Production |
|--------|------------|------------|
| Config file | `appsettings.Development.json` | `appsettings.json` |
| Gemini endpoint | `http://127.0.0.1:8317` (local proxy) | Same (deployed with proxy) |
| DB | Local `bunbun.db` | Persistent volume |
| Docker | `docker-compose.yml` available | Caddy reverse proxy |
| FFmpeg | Auto-downloaded or system-installed | Pre-installed in container |

---

## Quick Start

```bash
# 1. Start CLI Proxy (for Gemini LLM access)
cd ~/CLIProxyAPI && ./cli-proxy-api

# 2. Run the app
cd bunbun-broll-generator
dotnet run

# App runs at http://localhost:5000 (or configured port)
```
