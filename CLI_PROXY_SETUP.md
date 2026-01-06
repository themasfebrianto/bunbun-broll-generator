# CLIProxyAPI Setup Guide

install this web app in the vps at yaumi@BUNTU1:/srv/goodtree/apps/Bunbun-Broll$ 

Dokumentasi lengkap untuk setup dan penggunaan CLIProxyAPI sebagai proxy untuk mengakses Gemini API menggunakan OAuth Google Account.

## Overview

CLIProxyAPI adalah proxy server yang memungkinkan kita menggunakan Gemini API melalui autentikasi OAuth Google Account. Ini berguna untuk:
- Mengakses Gemini API tanpa perlu API key berbayar
- Menggunakan quota gratis dari Google Cloud Project
- Menyediakan endpoint OpenAI-compatible untuk integrasi dengan berbagai tools

## Arsitektur

```
[Web App / Client] 
       ↓ (HTTP Request)
[SSH Tunnel: localhost:8317]
       ↓
[VPS: CLIProxyAPI Container]
       ↓ (Google OAuth)
[Google Gemini API]
```

## Informasi Server

| Item | Value |
|:---|:---|
| **VPS IP** | `212.2.249.127` |
| **Directory** | `/srv/goodtree/apps/CliProxy` |
| **Container Name** | `cli-proxy-api` |
| **API Key** | `yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f` |
| **Google Account** | `themasfebrianto.atwork@gmail.com` |
| **GCP Project** | `fairshare-eb692` |

---

## Setup Awal (Sudah Selesai)

### 1. Clone Repository

```bash
cd /srv/goodtree/apps
git clone https://github.com/router-for-me/CLIProxyAPI.git CliProxy
cd CliProxy
```

### 2. Buat Config File

```bash
cat > config.yaml << 'EOF'
logging-to-file: false
debug: true
auth-dir: /root/.cli-proxy-api

api-keys:
  - yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f

port: 8317
host: 0.0.0.0
EOF
```

### 3. Siapkan Directory

```bash
mkdir -p auths logs
```

### 4. Build & Run Container

```bash
docker compose up -d --build
```

### 5. Login Google Account

Jalankan dari local machine:
```powershell
.\ssh-vps.ps1 proxy-login
```

Ikuti URL yang muncul, login dengan akun Google, dan pilih project GCP.

---

## Management Commands

Semua command dijalankan dari direktori `yaumi_app_backend`:

### Membuat SSH Tunnel (Wajib untuk akses)

```powershell
.\ssh-vps.ps1 proxy-tunnel
```

> ⚠️ **Penting**: Terminal ini harus tetap terbuka selama menggunakan proxy!

### Melihat Logs

```powershell
.\ssh-vps.ps1 proxy-logs
```

### Restart Container

```powershell
.\ssh-vps.ps1 proxy-up
```

### Login Akun Google Baru

```powershell
.\ssh-vps.ps1 proxy-login
```

### Akses Shell di VPS

```powershell
.\ssh-vps.ps1 proxy-bash
```

---

## Cara Penggunaan di Web App

### Base Configuration

Saat tunnel aktif, gunakan konfigurasi berikut:

| Setting | Value |
|:---|:---|
| **Base URL** | `http://localhost:8317/v1` |
| **API Key** | `yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f` |

### Contoh: cURL

```bash
# List available models
curl http://localhost:8317/v1/models \
  -H "Authorization: Bearer yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f"

# Chat completion
curl http://localhost:8317/v1/chat/completions \
  -H "Authorization: Bearer yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemini-2.0-flash",
    "messages": [
      {"role": "user", "content": "Hello, how are you?"}
    ]
  }'
```

### Contoh: JavaScript/TypeScript

```javascript
const response = await fetch('http://localhost:8317/v1/chat/completions', {
  method: 'POST',
  headers: {
    'Authorization': 'Bearer yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f',
    'Content-Type': 'application/json',
  },
  body: JSON.stringify({
    model: 'gemini-2.0-flash',
    messages: [
      { role: 'user', content: 'Hello, how are you?' }
    ],
  }),
});

const data = await response.json();
console.log(data.choices[0].message.content);
```

### Contoh: Python

```python
import requests

response = requests.post(
    'http://localhost:8317/v1/chat/completions',
    headers={
        'Authorization': 'Bearer yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f',
        'Content-Type': 'application/json',
    },
    json={
        'model': 'gemini-2.0-flash',
        'messages': [
            {'role': 'user', 'content': 'Hello, how are you?'}
        ],
    }
)

print(response.json()['choices'][0]['message']['content'])
```

### Contoh: OpenAI SDK (Python)

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:8317/v1",
    api_key="yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f"
)

response = client.chat.completions.create(
    model="gemini-2.0-flash",
    messages=[
        {"role": "user", "content": "Hello, how are you?"}
    ]
)

print(response.choices[0].message.content)
```

### Contoh: OpenAI SDK (JavaScript)

```javascript
import OpenAI from 'openai';

const openai = new OpenAI({
  baseURL: 'http://localhost:8317/v1',
  apiKey: 'yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f',
});

const response = await openai.chat.completions.create({
  model: 'gemini-2.0-flash',
  messages: [
    { role: 'user', content: 'Hello, how are you?' }
  ],
});

console.log(response.choices[0].message.content);
```

---

## Available Models

Model yang tersedia tergantung pada GCP Project yang dipilih saat login. Untuk melihat daftar model:

```bash
curl http://localhost:8317/v1/models \
  -H "Authorization: Bearer yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f"
```

Biasanya termasuk:
- `gemini-2.0-flash`
- `gemini-2.0-flash-lite`
- `gemini-1.5-pro`
- `gemini-1.5-flash`
- dll.

---

## Troubleshooting

### Error: "Invalid API key"

1. Pastikan config sudah benar:
   ```bash
   ssh yaumi@212.2.249.127 "cat /srv/goodtree/apps/CliProxy/config.yaml"
   ```

2. Restart container:
   ```powershell
   .\ssh-vps.ps1 proxy-up
   ```

### Error: "Could not connect to server"

1. Pastikan tunnel berjalan:
   ```powershell
   .\ssh-vps.ps1 proxy-tunnel
   ```

2. Cek status container:
   ```bash
   ssh yaumi@212.2.249.127 "docker ps --filter name=cli-proxy-api"
   ```

### Container terus restart

Cek logs untuk error:
```powershell
.\ssh-vps.ps1 proxy-logs
```

Biasanya karena YAML syntax error di `config.yaml`.

### Token expired

Login ulang:
```powershell
.\ssh-vps.ps1 proxy-login
```

---

## Files di VPS

```
/srv/goodtree/apps/CliProxy/
├── config.yaml          # Konfigurasi utama
├── auths/               # OAuth tokens (jangan dihapus!)
│   └── *.json           # Token files per akun
├── logs/                # Log files
├── docker-compose.yml   # Docker config
└── Dockerfile           # Build config
```

---

## Security Notes

1. **API Key**: Jangan share API key ini ke publik
2. **OAuth Tokens**: File di `/auths` berisi credentials sensitif
3. **SSH Tunnel**: Selalu gunakan tunnel, jangan expose port 8317 ke public

---

## Referensi

- [CLIProxyAPI Repository](https://github.com/router-for-me/CLIProxyAPI)
- [OpenAI API Reference](https://platform.openai.com/docs/api-reference) (compatible endpoints)
