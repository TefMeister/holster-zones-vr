# install.ps1 -- copies Holster Zones from dist\ to %LOCALAPPDATA%\Programs\HolsterZones and puts its icon on the desktop.
#   powershell -ExecutionPolicy Bypass -File install.ps1 [-From <folder with the built files>]
param([string]$From = "")
if ($From -eq "") { $From = if (Test-Path (Join-Path $PSScriptRoot "dist\HolsterZones.exe")) { Join-Path $PSScriptRoot "dist" } else { $PSScriptRoot } }
$ErrorActionPreference = "Stop"
$to = Join-Path $env:LOCALAPPDATA "Programs\HolsterZones"
New-Item -ItemType Directory -Force $to | Out-Null
foreach ($f in "HolsterZones.exe", "holster_zones.ico") {
    if (Test-Path (Join-Path $From $f)) { Copy-Item (Join-Path $From $f) (Join-Path $to $f) -Force }
}
$desktop = [Environment]::GetFolderPath("Desktop")
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path $desktop "Holster Zones.lnk"))
$lnk.TargetPath = Join-Path $to "HolsterZones.exe"
$lnk.WorkingDirectory = $to
$lnk.IconLocation = (Join-Path $to "holster_zones.ico") + ",0"
$lnk.Description = "Move and resize the holster spots on the body in VR"
$lnk.Save()
Write-Output "Installed to $to, icon on the desktop."
