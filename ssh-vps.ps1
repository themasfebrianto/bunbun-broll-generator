param(
    [string]$Action,
    [string]$Model
)

$VPS_USER = "yaumi"
$VPS_HOST = "212.2.249.127"
$VPS_PATH = "/srv/goodtree/apps/Bunbun-Broll"

Write-Host "Connecting to Bunbun B-Roll VPS..." -ForegroundColor Cyan

if ($Action -eq "up") {
    Write-Host "[Deployment Mode] Pulling latest image and restarting containers..." -ForegroundColor Yellow
    ssh -t ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && docker compose -f docker-compose.yml pull app && docker compose -f docker-compose.yml up -d app && echo '' && echo '✅ Deployment complete!' && bash"
} elseif ($Action -eq "logs") {
    Write-Host "[Logs Mode] Showing Bunbun B-Roll application logs (Ctrl+C to exit)..." -ForegroundColor Yellow
    ssh -t ${VPS_USER}@${VPS_HOST} "docker logs -f bunbun-broll"
} elseif ($Action -eq "env") {
    Write-Host "[Env Mode] Managing environment variables on VPS..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Current .env on VPS:" -ForegroundColor Cyan
    ssh ${VPS_USER}@${VPS_HOST} "cat ${VPS_PATH}/.env 2>/dev/null || echo 'No .env file found!'"
    Write-Host ""
    Write-Host "To update .env on VPS, either:" -ForegroundColor Gray
    Write-Host "  1. Run: .\push.ps1 (syncs local .env automatically)" -ForegroundColor White
    Write-Host "  2. Run: .\ssh-vps.ps1 env-edit (edit directly on VPS)" -ForegroundColor White
} elseif ($Action -eq "env-edit") {
    Write-Host "[Env Edit Mode] Opening .env for editing on VPS..." -ForegroundColor Yellow
    ssh -t ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && nano .env"
} elseif ($Action -eq "env-sync") {
    Write-Host "[Env Sync Mode] Syncing local .env to VPS..." -ForegroundColor Yellow
    if (Test-Path ".env") {
        scp ".env" "${VPS_USER}@${VPS_HOST}:${VPS_PATH}/.env"
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ .env synced to VPS!" -ForegroundColor Green
            Write-Host "Restarting container to apply changes..." -ForegroundColor Yellow
            ssh ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && docker compose up -d"
            Write-Host "✅ Container restarted!" -ForegroundColor Green
        } else {
            Write-Host "❌ Sync failed!" -ForegroundColor Red
        }
    } else {
        Write-Host "❌ No local .env file found!" -ForegroundColor Red
        Write-Host "Create one from .env.example first." -ForegroundColor Yellow
    }
} elseif ($Action -eq "env-create") {
    Write-Host "[Env Create Mode] Creating .env on VPS from template..." -ForegroundColor Yellow
    $envContent = @"
# BunBun B-Roll Generator - Production Secrets
# Created: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

# Authentication
AUTH_EMAIL=bunbuncantik@gmail.com
AUTH_PASSWORD=bunbuncantik123!

# API Keys
PEXELS_API_KEY=MKnxIaTkWjP0kRynLevRSimMJFP0NiDYc4pxoUh4tj4AWetIcz4AXIaE
PIXABAY_API_KEY=54076676-39c035ba3240546f548320c2c
GEMINI_API_KEY=yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f
"@
    Write-Host "Creating .env with your credentials..." -ForegroundColor Yellow
    ssh ${VPS_USER}@${VPS_HOST} "cat > ${VPS_PATH}/.env << 'EOF'
$envContent
EOF"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ .env created on VPS!" -ForegroundColor Green
        Write-Host "Restarting container to apply changes..." -ForegroundColor Yellow
        ssh ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && docker compose up -d"
        Write-Host "✅ Container restarted!" -ForegroundColor Green
    } else {
        Write-Host "❌ Failed to create .env!" -ForegroundColor Red
    }
} elseif ($Action -eq "restart") {
    Write-Host "[Restart Mode] Restarting container..." -ForegroundColor Yellow
    ssh -t ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && docker compose up -d && echo '✅ Container restarted!'"
} elseif ($Action -eq "proxy-up") {
    Write-Host "[Proxy Mode] Building and restarting CLIProxyAPI..." -ForegroundColor Yellow
    ssh -t ${VPS_USER}@${VPS_HOST} "cd /srv/goodtree/apps/CliProxy && docker compose up -d --build && echo '' && echo '✅ Proxy deployment complete!' && bash"
} elseif ($Action -eq "proxy-logs") {
    Write-Host "[Proxy Logs] Showing CLIProxyAPI logs (Ctrl+C to exit)..." -ForegroundColor Yellow
    ssh -t ${VPS_USER}@${VPS_HOST} "docker logs -f cli-proxy-api"
} elseif ($Action -eq "proxy-bash") {
    Write-Host "[Proxy Admin] Opening shell in CLIProxy directory..." -ForegroundColor Yellow
    ssh -t ${VPS_USER}@${VPS_HOST} "cd /srv/goodtree/apps/CliProxy && bash"
} elseif ($Action -eq "proxy-login") {
    Write-Host "[Proxy Login] Starting interactive Google OAuth flow (with Tunnel on 8085)..." -ForegroundColor Yellow
    Write-Host "Please follow the URL printed below. The localhost callback will now work!" -ForegroundColor Cyan
    ssh -t -L 8085:localhost:8085 ${VPS_USER}@${VPS_HOST} "docker exec -it cli-proxy-api ./CLIProxyAPI -login -no-browser"
} elseif ($Action -eq "proxy-tunnel") {
    Write-Host "[Proxy Tunnel] Creating SSH tunnel to CLIProxyAPI on port 8317..." -ForegroundColor Yellow
    Write-Host "Access the proxy at: http://localhost:8317" -ForegroundColor Cyan
    Write-Host "API Key: (check your .env file)" -ForegroundColor Green
    Write-Host "Press Ctrl+C to close the tunnel." -ForegroundColor DarkGray
    ssh -N -L 8317:localhost:8317 ${VPS_USER}@${VPS_HOST}
} elseif ($Action -eq "help" -or $Action -eq "-h" -or $Action -eq "--help") {
    Write-Host ""
    Write-Host "Usage: .\ssh-vps.ps1 [action]" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Actions:" -ForegroundColor Yellow
    Write-Host "  (none)       Open SSH shell in project directory" -ForegroundColor White
    Write-Host "  up           Pull and restart container" -ForegroundColor White
    Write-Host "  logs         Show container logs" -ForegroundColor White
    Write-Host "  restart      Restart container" -ForegroundColor White
    Write-Host ""
    Write-Host "Environment:" -ForegroundColor Yellow
    Write-Host "  env          Show current .env on VPS" -ForegroundColor White
    Write-Host "  env-edit     Edit .env on VPS (nano)" -ForegroundColor White
    Write-Host "  env-sync     Sync local .env to VPS" -ForegroundColor White
    Write-Host "  env-create   Create .env on VPS with defaults" -ForegroundColor White
    Write-Host ""
    Write-Host "Proxy:" -ForegroundColor Yellow
    Write-Host "  proxy-up     Rebuild and restart proxy" -ForegroundColor White
    Write-Host "  proxy-logs   Show proxy logs" -ForegroundColor White
    Write-Host "  proxy-bash   SSH into proxy directory" -ForegroundColor White
    Write-Host "  proxy-tunnel Create tunnel to proxy (port 8317)" -ForegroundColor White
    Write-Host "  proxy-login  Run OAuth login flow" -ForegroundColor White
    Write-Host ""
} else {
    ssh -t ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && bash"
}

# Local development: secrets in appsettings.Development.json (auto-loaded)

# View VPS secrets:
# .\ssh-vps.ps1 env

# Edit VPS secrets directly:
# .\ssh-vps.ps1 env-edit

# Deploy (auto-syncs .env if it exists locally):
# .\push.ps1