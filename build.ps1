# Script de compilação para Windows
$ErrorActionPreference = "Stop"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  ORÁCULO GUIDANCE - Build Script (Windows)  " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$DefaultPath = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
if (-not $env:KSP_DIR) {
    if (Test-Path $DefaultPath) {
        $env:KSP_DIR = $DefaultPath
    }
}

Write-Host "[+] Diretório KSP: $env:KSP_DIR" -ForegroundColor Green

dotnet build OraculoGuidance.csproj -c Release

if ($env:KSP_DIR -and (Test-Path "$env:KSP_DIR\GameData")) {
    $Dest = "$env:KSP_DIR\GameData\OraculoGuidance"
    New-Item -ItemType Directory -Force -Path "$Dest\Plugins" | Out-Null
    Copy-Item -Recurse -Force "GameData\OraculoGuidance\*" $Dest
    Write-Host "[OK] Instalado no KSP com sucesso!" -ForegroundColor Green
} else {
    Write-Host "[!] DLL gerada em GameData/OraculoGuidance/Plugins/" -ForegroundColor Yellow
}
