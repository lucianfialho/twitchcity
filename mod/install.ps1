# install.ps1 - Compila e instala o mod no Cities: Skylines
# Rode como: powershell -ExecutionPolicy Bypass -File install.ps1

$ErrorActionPreference = "Stop"

# 1. Detectar path do Cities Skylines
$steamPaths = @(
    "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines",
    "C:\Program Files\Steam\steamapps\common\Cities_Skylines",
    "D:\SteamLibrary\steamapps\common\Cities_Skylines",
    "D:\Steam\steamapps\common\Cities_Skylines",
    "E:\SteamLibrary\steamapps\common\Cities_Skylines"
)

$citiesPath = $null
foreach ($path in $steamPaths) {
    if (Test-Path "$path\Cities_Data\Managed\ICities.dll") {
        $citiesPath = $path
        break
    }
}

if (-not $citiesPath) {
    Write-Host "Cities: Skylines nao encontrado nos paths padrao." -ForegroundColor Red
    $citiesPath = Read-Host "Digite o path do Cities Skylines (ex: D:\Games\Cities_Skylines)"
    if (-not (Test-Path "$citiesPath\Cities_Data\Managed\ICities.dll")) {
        Write-Host "ICities.dll nao encontrado em $citiesPath\Cities_Data\Managed\" -ForegroundColor Red
        exit 1
    }
}

Write-Host "Cities: Skylines encontrado em: $citiesPath" -ForegroundColor Green

# 2. Compilar
Write-Host "`nCompilando mod..." -ForegroundColor Cyan
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projDir = Join-Path $scriptDir "CitySkylinesBridge"

# Atualizar csproj com path correto
$csproj = Join-Path $projDir "CitySkylinesBridge.csproj"
$content = Get-Content $csproj -Raw
$managed = "$citiesPath\Cities_Data\Managed"
$content = $content -replace '<CitiesManaged>.*</CitiesManaged>', "<CitiesManaged>$managed</CitiesManaged>"
Set-Content $csproj $content

dotnet build $projDir -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Falha na compilacao!" -ForegroundColor Red
    exit 1
}

# 3. Instalar
$modDir = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\TwitchCityBridge"
if (-not (Test-Path $modDir)) {
    New-Item -ItemType Directory -Path $modDir -Force | Out-Null
}

$dll = Get-ChildItem "$projDir\bin\Release\*.dll" -Recurse | Where-Object { $_.Name -eq "CitySkylinesBridge.dll" } | Select-Object -First 1
if (-not $dll) {
    Write-Host "DLL nao encontrada apos build!" -ForegroundColor Red
    exit 1
}

Copy-Item $dll.FullName "$modDir\CitySkylinesBridge.dll" -Force
Write-Host "`nMod instalado em: $modDir" -ForegroundColor Green

# 4. Firewall
Write-Host "`nAdicionando regra de firewall (porta 8080)..." -ForegroundColor Cyan
try {
    netsh advfirewall firewall add rule name="TwitchCity Bridge" dir=in action=allow protocol=TCP localport=8080 2>$null
    Write-Host "Firewall configurado." -ForegroundColor Green
} catch {
    Write-Host "Aviso: nao conseguiu configurar firewall (rode como admin)" -ForegroundColor Yellow
}

Write-Host "`n✅ Pronto! Abra o Cities: Skylines e ative o mod 'TwitchCity Bridge' no Content Manager." -ForegroundColor Green
Write-Host "Depois, rode 'npm start' no Mac para o Onoma comecar a jogar." -ForegroundColor Cyan
