#!/usr/bin/env pwsh
# Full Deployment: Build, Push to GHCR, SSH to VPS, Pull and Up
# Usage: .\push.ps1

$IMAGE_NAME = "ghcr.io/themasfebrianto/bunbun-broll-generator"
$TAG = "latest"
$VPS_USER = "yaumi"
$VPS_HOST = "212.2.249.127"
$VPS_PATH = "/srv/goodtree/apps/Bunbun-Broll"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Bunbun B-Roll Full Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 0: Sync .env to VPS (if exists locally)
if (Test-Path ".env") {
    Write-Host "[0/5] Syncing .env to VPS..." -ForegroundColor Yellow
    scp ".env" "${VPS_USER}@${VPS_HOST}:${VPS_PATH}/.env"
    if ($LASTEXITCODE -ne 0) {
        Write-Host ".env sync failed! Continuing anyway..." -ForegroundColor Yellow
    } else {
        Write-Host ".env synced!" -ForegroundColor Green
    }
    Write-Host ""
} else {
    Write-Host "[0/5] No local .env found, skipping sync..." -ForegroundColor Gray
    Write-Host "      Create .env from .env.example if needed" -ForegroundColor Gray
    Write-Host ""
}

# Step 1: Build the image   
Write-Host "[1/5] Building Docker image..." -ForegroundColor Yellow
Write-Host "Image: ${IMAGE_NAME}:${TAG}" -ForegroundColor Gray
docker build -t "${IMAGE_NAME}:${TAG}" .

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""

# Step 2: Push to GHCR
Write-Host "[2/5] Pushing to GHCR..." -ForegroundColor Yellow
docker push "${IMAGE_NAME}:${TAG}"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Push failed!" -ForegroundColor Red
    Write-Host "If you see authentication errors, run:" -ForegroundColor Yellow
    Write-Host "  docker login ghcr.io -u themasfebrianto" -ForegroundColor White
    exit 1
}
Write-Host "Push complete!" -ForegroundColor Green
Write-Host ""

# Step 3: Sync docker-compose.yml to VPS
Write-Host "[3/5] Syncing docker-compose.yml to VPS..." -ForegroundColor Yellow
scp "docker-compose.yml" "${VPS_USER}@${VPS_HOST}:${VPS_PATH}/docker-compose.yml"

if ($LASTEXITCODE -ne 0) {
    Write-Host "docker-compose.yml sync failed!" -ForegroundColor Red
    exit 1
}
Write-Host "docker-compose.yml synced!" -ForegroundColor Green
Write-Host ""

# Step 4: SSH to VPS and pull
Write-Host "[4/5] Connecting to VPS and pulling latest image..." -ForegroundColor Yellow
ssh ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && docker compose pull"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Pull failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Pull complete!" -ForegroundColor Green
Write-Host ""

# Step 5: Restart containers
Write-Host "[5/5] Restarting containers on VPS..." -ForegroundColor Yellow
ssh ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && docker compose up -d"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Container restart failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Containers restarted!" -ForegroundColor Green
Write-Host ""

Write-Host "========================================" -ForegroundColor Green
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Image available at:" -ForegroundColor Cyan
Write-Host "  https://github.com/themasfebrianto/bunbun-broll-generator/pkgs/container/bunbun-broll-generator" -ForegroundColor White
Write-Host ""
Write-Host "Note: Secrets are loaded from .env on VPS" -ForegroundColor Gray
Write-Host "      To update secrets: .\ssh-vps.ps1 env" -ForegroundColor Gray
