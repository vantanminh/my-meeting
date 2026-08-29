[CmdletBinding()]
param(
    [string]$ProjectId = [Environment]::GetEnvironmentVariable("MEETING_ASSISTANT_FIREBASE_PROJECT_ID")
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectId)) {
    throw "Set MEETING_ASSISTANT_FIREBASE_PROJECT_ID or pass -ProjectId before deploying Firestore rules."
}

$firebase = Get-Command firebase -ErrorAction SilentlyContinue
if ($null -eq $firebase) {
    throw "Firebase CLI is required. Install it with: npm install -g firebase-tools"
}

$firebaseRoot = (Resolve-Path $PSScriptRoot).Path
Push-Location $firebaseRoot
try {
    & $firebase.Source deploy --only firestore:rules --project $ProjectId
    if ($LASTEXITCODE -ne 0) {
        throw "Firebase rules deployment failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
