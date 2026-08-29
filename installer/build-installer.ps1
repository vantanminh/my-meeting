[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".." )).Path
$projectPath = Join-Path $repoRoot "src\MeetingAssistant\MeetingAssistant.csproj"
$publishPath = Join-Path $repoRoot "src\MeetingAssistant\bin\$Configuration\net8.0-windows\$Runtime\publish"
$installerPath = Join-Path $repoRoot "installer\MeetingAssistant.iss"

$firebaseApiKey = [Environment]::GetEnvironmentVariable("MEETING_ASSISTANT_FIREBASE_API_KEY")
$firebaseProjectId = [Environment]::GetEnvironmentVariable("MEETING_ASSISTANT_FIREBASE_PROJECT_ID")
if ([string]::IsNullOrWhiteSpace($firebaseApiKey) -or [string]::IsNullOrWhiteSpace($firebaseProjectId)) {
    throw "Set MEETING_ASSISTANT_FIREBASE_API_KEY and MEETING_ASSISTANT_FIREBASE_PROJECT_ID before building a shareable installer."
}

Write-Host "Publishing self-contained $Runtime build..."
dotnet publish $projectPath --configuration $Configuration --runtime $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

$firebaseConfig = [ordered]@{
    apiKey = $firebaseApiKey
    projectId = $firebaseProjectId
}
$firebaseConfig | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishPath "firebase.config.json") -Encoding utf8

if ($SkipInstaller) {
    Write-Host "Published package is ready at $publishPath"
    exit 0
}

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $knownPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    if ($knownPaths.Count -gt 0) {
        $isccPath = $knownPaths[0]
    }
    else {
        Write-Warning "Inno Setup compiler was not found. Install it with: winget install --id JRSoftware.InnoSetup -e"
        Write-Host "The self-contained published package is ready at $publishPath"
        exit 0
    }
}
else {
    $isccPath = $iscc.Source
}

New-Item -ItemType Directory -Path (Join-Path $repoRoot "dist") -Force | Out-Null
& $isccPath $installerPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Host "Installer is ready at $(Join-Path $repoRoot 'dist\MeetingAssistant-Setup.exe')"
