$url = "http://127.0.0.1:8317/v1/chat/completions"
$headers = @{
    "Content-Type" = "application/json"
    "Authorization" = "Bearer scriptflow_gemini_pk_local"
}
$body = @{
    model = "gemini-3-pro-preview"
    messages = @(
        @{ role = "system"; content = "You are a test bot." },
        @{ role = "user"; content = "Hello, are you working?" }
    )
    max_tokens = 100
} | ConvertTo-Json -Depth 5

Write-Host "Sending request to $url..."
try {
    $response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $body -TimeoutSec 30
    Write-Host "Success!" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5)
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $respBody = $reader.ReadToEnd()
        Write-Host "Response Body: $respBody" -ForegroundColor Yellow
    }
}
