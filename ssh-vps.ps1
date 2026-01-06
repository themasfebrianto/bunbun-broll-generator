param(
    [string]$Action,
    [string]$Model
)

Write-Host "Connecting to Yaumi VPS..." -ForegroundColor Cyan

if ($Action -eq "up") {
    Write-Host "[Deployment Mode] Pulling latest image and restarting containers..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "cd /srv/goodtree/apps/Yaumi && docker compose -f docker-compose.yml pull app && docker compose -f docker-compose.yml up -d app && echo '' && echo '✅ Deployment complete!' && bash"
} elseif ($Action -eq "logs") {
    Write-Host "[Logs Mode] Showing Laravel application logs (Ctrl+C to exit)..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "docker exec -it yaumi-app tail -f -n 100 /var/www/html/storage/logs/laravel.log"
} elseif ($Action -eq "t") {
    Write-Host "[Tinker Mode] Starting Laravel Tinker REPL..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "docker exec -it yaumi-app php artisan tinker"
} elseif ($Action -eq "play") {
    Write-Host "[Play Mode] Executing playground.php in VPS..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "docker exec -it yaumi-app php artisan app:play"
} elseif ($Action -eq "sandbox-code") {
    if (-not $Model) { Write-Host "Usage: .\ssh-vps.ps1 sandbox-code '<php_code>'" -ForegroundColor red; return }
    $Bytes = [System.Text.Encoding]::UTF8.GetBytes($Model)
    $Encoded = [Convert]::ToBase64String($Bytes)
    Write-Host "[Sandbox Mode] Executing encoded code in VPS..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "docker exec -it yaumi-app php artisan app:play --code=$Encoded"
} elseif ($Action -eq "check-activity") {
    Write-Host "[Activity Check] Checking pending activity buffers..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "docker exec -it yaumi-app php artisan tinker --execute='use App\Models\ActivityBuffer; echo \"Pending Activities: \" . ActivityBuffer::count() . \"\n\"; ActivityBuffer::all()->each(fn(\$a) => dump(\$a->event_type . \" - \" . \$a->habit_name));'"
} elseif ($Action -eq "run-activity") {
    Write-Host "[Activity Run] Manually processing activity buffer..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "docker exec -it yaumi-app php artisan process:activity-buffer"
} elseif ($Action -eq "proxy-up") {
    Write-Host "[Proxy Mode] Building and restarting CLIProxyAPI..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "cd /srv/goodtree/apps/CliProxy && docker compose up -d --build && echo '' && echo '✅ Proxy deployment complete!' && bash"
} elseif ($Action -eq "proxy-logs") {
    Write-Host "[Proxy Logs] Showing CLIProxyAPI logs (Ctrl+C to exit)..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "docker logs -f cli-proxy-api"
} elseif ($Action -eq "proxy-bash") {
    Write-Host "[Proxy Admin] Opening shell in CLIProxy directory..." -ForegroundColor Yellow
    ssh -t yaumi@212.2.249.127 "cd /srv/goodtree/apps/CliProxy && bash"
} elseif ($Action -eq "proxy-login") {
    Write-Host "[Proxy Login] Starting interactive Google OAuth flow (with Tunnel on 8085)..." -ForegroundColor Yellow
    Write-Host "Please follow the URL printed below. The localhost callback will now work!" -ForegroundColor Cyan
    ssh -t -L 8085:localhost:8085 yaumi@212.2.249.127 "docker exec -it cli-proxy-api ./CLIProxyAPI -login -no-browser"
} elseif ($Action -eq "proxy-tunnel") {
    Write-Host "[Proxy Tunnel] Creating SSH tunnel to CLIProxyAPI on port 8317..." -ForegroundColor Yellow
    Write-Host "Access the proxy at: http://localhost:8317" -ForegroundColor Cyan
    Write-Host "API Key: yaumi_proxy_pk_7d9e2a4b8c1f0d3e5a2b6c9f" -ForegroundColor Green
    Write-Host "Press Ctrl+C to close the tunnel." -ForegroundColor DarkGray
    ssh -N -L 8317:localhost:8317 yaumi@212.2.249.127
} else {
    ssh -t yaumi@212.2.249.127 "cd /srv/goodtree/apps/Yaumi && bash"
}
