# Download FFmpeg for Windows
# This script downloads FFmpeg and extracts it to the ffmpeg-binaries folder

$ErrorActionPreference = "Stop"

$ffmpegDir = "$PSScriptRoot\ffmpeg-binaries"
$tempDir = "$env:TEMP\ffmpeg-download"
$ffmpegZip = "$tempDir\ffmpeg.zip"

# Create directories
Write-Host "Creating directories..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $ffmpegDir | Out-Null
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# Download FFmpeg from gyan.dev (recommended Windows build)
$ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"

Write-Host "Downloading FFmpeg from: $ffmpegUrl" -ForegroundColor Cyan
Write-Host "This may take a few minutes..." -ForegroundColor Yellow

try {
    # Download with progress
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $ffmpegUrl -OutFile $ffmpegZip -UseBasicParsing
    $ProgressPreference = 'Continue'
    
    Write-Host "Download complete. Extracting..." -ForegroundColor Cyan
    
    # Extract the zip
    Expand-Archive -Path $ffmpegZip -DestinationPath $tempDir -Force
    
    # Find the extracted folder (it has version number in the name)
    $extractedFolder = Get-ChildItem -Path $tempDir -Directory | Where-Object { $_.Name -like "ffmpeg-*" } | Select-Object -First 1
    
    if ($null -eq $extractedFolder) {
        throw "Could not find extracted FFmpeg folder"
    }
    
    # Copy binaries to ffmpeg-binaries folder
    $binPath = Join-Path $extractedFolder.FullName "bin"
    
    Write-Host "Copying FFmpeg binaries to: $ffmpegDir" -ForegroundColor Cyan
    
    Copy-Item -Path "$binPath\ffmpeg.exe" -Destination $ffmpegDir -Force
    Copy-Item -Path "$binPath\ffprobe.exe" -Destination $ffmpegDir -Force
    
    # Verify installation
    $ffmpegExe = Join-Path $ffmpegDir "ffmpeg.exe"
    if (Test-Path $ffmpegExe) {
        Write-Host ""
        Write-Host "FFmpeg installed successfully!" -ForegroundColor Green
        Write-Host "Location: $ffmpegDir" -ForegroundColor Gray
        
        # Get version
        $version = & $ffmpegExe -version 2>&1 | Select-Object -First 1
        Write-Host "Version: $version" -ForegroundColor Gray
    }
    else {
        throw "FFmpeg installation failed - ffmpeg.exe not found"
    }
    
    # Cleanup
    Write-Host "Cleaning up temporary files..." -ForegroundColor Cyan
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    
    Write-Host ""
    Write-Host "Done! You can now run the application." -ForegroundColor Green
    
}
catch {
    Write-Host ""
    Write-Host "ERROR: Failed to download/install FFmpeg" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Manual installation:" -ForegroundColor Yellow
    Write-Host "1. Download from: https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    Write-Host "2. Extract the zip file"
    Write-Host "3. Copy ffmpeg.exe and ffprobe.exe from the bin folder to: $ffmpegDir"
    exit 1
}
