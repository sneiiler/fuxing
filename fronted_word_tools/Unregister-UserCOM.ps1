# Unregister-UserCOM.ps1
$ErrorActionPreference = "SilentlyContinue"

# Connect
Remove-Item "HKCU:\Software\Classes\CLSID\{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Classes\Wow6432Node\CLSID\{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Classes\FuXing.Connect" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Microsoft\Office\Word\Addins\FuXing.Connect" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Kingsoft\Office\WPS\Addins\FuXing.Connect" -Recurse -Force 2>$null
Remove-ItemProperty "HKCU:\Software\Kingsoft\Office\WPS\AddinsWL" -Name "FuXing.Connect" -Force -EA SilentlyContinue 2>$null

# TaskPaneControl
Remove-Item "HKCU:\Software\Classes\CLSID\{03326A51-B257-3623-917E-25A086B271B0}" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Classes\Wow6432Node\CLSID\{03326A51-B257-3623-917E-25A086B271B0}" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Classes\FuXing.TaskPaneControl" -Recurse -Force 2>$null

Write-Host "  [OK] User-level COM unregistered (Word + WPS)"