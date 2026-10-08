$ErrorActionPreference = 'Stop'

$simHubProcess = Get-Process -Name 'SimHubWPF' -ErrorAction SilentlyContinue
if ($null -ne $simHubProcess) {
    throw 'SimHub laeuft noch. Bitte SimHub vollstaendig beenden und die Reparatur danach erneut starten.'
}

$settingsPath = 'C:\Program Files (x86)\SimHub\PluginsData\Common\ArduinoRGBMatrixSettings.json'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "Die RGB-Matrix-Einstellungen wurden nicht gefunden: $settingsPath"
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$profile = $settings.Profiles | Where-Object { $_.Name -match '^\s*Open\s*OMSI\s*$' } | Select-Object -First 1

if ($null -eq $profile) {
    $currentGame = [string]$settings.CurrentProfileGame.CurrentGame
    $mapping = $settings.LastGameProfiles.PSObject.Properties |
        Where-Object { $_.Name -ieq $currentGame } |
        Select-Object -First 1
    if ($null -ne $mapping -and -not [string]::IsNullOrWhiteSpace([string]$mapping.Value)) {
        $profile = $settings.Profiles |
            Where-Object { $_.ProfileId -eq [string]$mapping.Value } |
            Select-Object -First 1
    }
}

if ($null -eq $profile) {
    throw 'Das geklonte RGB-Matrix-Profil OpenOMSI wurde nicht gefunden. Bitte das Profil in SimHub speichern und SimHub danach vollstaendig beenden.'
}

$containerId = 'c8bc5f8e-6c21-4be0-8aa5-3a0f9d9e693e'
$javascript = @'
var gear = String($prop('DataCorePlugin.GameData.NewData.Gear') || '');
var glyph = ['00000000','00000000','00111000','00100100','00100100','00100100','00111000','00000000'];
var result = [];
for (var row = 0; row < 8; row++) {
  for (var col = 0; col < 8; col++) {
    if (gear == 'D') {
      if (row == 0) {
        result.push('#00000000');
      } else {
        result.push(glyph[row].charAt(col) == '1' ? '#FF0000FF' : '#FF000000');
      }
    } else {
      result.push('#00000000');
    }
  }
}
return result;
'@

function Find-ContainerById {
    param($Owner, [string]$Id)
    foreach ($container in @($Owner.LedContainers)) {
        if ($container.ContainerId -eq $Id) {
            return $container
        }
        if ($null -ne $container.PSObject.Properties['LedContainers']) {
            $found = Find-ContainerById -Owner $container -Id $Id
            if ($null -ne $found) {
                return $found
            }
        }
    }
    return $null
}

function Find-GearOwner {
    param($Owner)
    foreach ($container in @($Owner.LedContainers)) {
        if ($container.ContainerType -eq 'GearContainer' -and $container.StartPositionMatrix -eq 2) {
            return $Owner
        }
        if ($null -ne $container.PSObject.Properties['LedContainers']) {
            $found = Find-GearOwner -Owner $container
            if ($null -ne $found) {
                return $found
            }
        }
    }
    return $null
}

$existing = Find-ContainerById -Owner $profile -Id $containerId
if ($null -ne $existing) {
    $existing.ContentFormula.JSExt = 0
    $existing.ContentFormula.Interpreter = 1
    $existing.ContentFormula.Expression = $javascript
    $existing.Description = 'OpenOMSI centered D with full right indicator pass-through'
    $existing.IsEnabled = $true
}

if ($null -eq $existing) {
    $gearOwner = Find-GearOwner -Owner $profile
    if ($null -eq $gearOwner) {
        throw "Im Profil '$($profile.Name)' wurde auf der rechten Matrix kein Gang-Baustein gefunden."
    }

    $overlay = [pscustomobject]@{
        ContentFormula      = [pscustomobject]@{
            JSExt       = 0
            Interpreter = 1
            Expression  = $javascript
        }
        Rows                = 8
        Columns             = 8
        StartPositionMatrix = 2
        StartPositionX      = 1
        StartPositionY      = 1
        ContainerId         = $containerId
        ContainerType       = 'ScriptedContentContainer'
        Description         = 'OpenOMSI centered D with full right indicator pass-through'
        IsEnabled           = $true
        DeviceKind          = 2
    }
    $gearOwner.LedContainers = @($gearOwner.LedContainers) + $overlay
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = "$settingsPath.OpenOMSI-Backup-$stamp"
Copy-Item -LiteralPath $settingsPath -Destination $backupPath

$json = $settings | ConvertTo-Json -Depth 100
[IO.File]::WriteAllText($settingsPath, $json, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host "Fertig: Das mittige D und der vollstaendige rechte Blinkerbalken wurden im Profil '$($profile.Name)' eingerichtet." -ForegroundColor Green
Write-Host 'Die gesamte obere Zeile bleibt fuer den gruenen Blinkerbalken frei.'
Write-Host 'Elektro- und Diesel-Automatikbusse verwenden D/N/R. Handschalter verwenden R/N und Gangnummern.'
Write-Host "Sicherung: $backupPath"
Write-Host 'SimHub kann jetzt wieder gestartet werden.'
