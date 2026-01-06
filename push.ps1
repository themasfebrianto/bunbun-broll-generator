#!/usr/bin/env pwsh
# Full Deployment: Build, Push to GHCR, SSH to VPS, Pull and Up
# Usage: .\push.ps1

$IMAGE_NAME = "ghcr.io/themasfebrianto/yaumi-backend"
$TAG = "latest"
$VPS_USER = "yaumi"
$VPS_HOST = "212.2.249.127"
$VPS_PATH = "/srv/goodtree/apps/Yaumi"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Yaumi Full Deployment Pipeline" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the image
Write-Host "[1/4] Building Docker image..." -ForegroundColor Yellow
Write-Host "Image: ${IMAGE_NAME}:${TAG}" -ForegroundColor Gray
docker build -t "${IMAGE_NAME}:${TAG}" .

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""

# Step 2: Push to GHCR
Write-Host "[2/4] Pushing to GHCR..." -ForegroundColor Yellow
docker push "${IMAGE_NAME}:${TAG}"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Push failed!" -ForegroundColor Red
    Write-Host "If you see authentication errors, run:" -ForegroundColor Yellow
    Write-Host "  docker login ghcr.io -u themasfebrianto" -ForegroundColor White
    exit 1
}
Write-Host "Push complete!" -ForegroundColor Green
Write-Host ""

# Step 3: SSH to VPS and pull
Write-Host "[3/4] Connecting to VPS and pulling latest image..." -ForegroundColor Yellow
ssh ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && docker compose -f docker-compose.yml pull app"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Pull failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Pull complete!" -ForegroundColor Green
Write-Host ""

# Step 4: Restart containers
Write-Host "[4/4] Restarting containers on VPS..." -ForegroundColor Yellow
ssh ${VPS_USER}@${VPS_HOST} "cd ${VPS_PATH} && docker compose -f docker-compose.yml up -d app"

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
Write-Host "  https://github.com/themasfebrianto/yaumi_app_backend/pkgs/container/yaumi-backend" -ForegroundColor White
