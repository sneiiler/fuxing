# Unregister-UserCOM.ps1
$ErrorActionPreference = "SilentlyContinue"

# Connect
Remove-Item "HKCU:\Software\Classes\CLSID\{D0E1F2A3-B4C5-6789-ABCD-EF0123456789}" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Classes\Wow6432Node\CLSID\{D0E1F2A3-B4C5-6789-ABCD-EF0123456789}" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Classes\FuXingAgent.Connect" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Microsoft\Office\Word\Addins\FuXingAgent.Connect" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Kingsoft\Office\WPS\Addins\FuXingAgent.Connect" -Recurse -Force 2>$null
Remove-ItemProperty "HKCU:\Software\Kingsoft\Office\WPS\AddinsWL" -Name "FuXingAgent.Connect" -Force -EA SilentlyContinue 2>$null

# TaskPaneHost
Remove-Item "HKCU:\Software\Classes\CLSID\{E1F2A3B4-C5D6-7890-ABCD-EF1234567890}" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Classes\Wow6432Node\CLSID\{E1F2A3B4-C5D6-7890-ABCD-EF1234567890}" -Recurse -Force 2>$null
Remove-Item "HKCU:\Software\Classes\FuXingAgent.TaskPaneHost" -Recurse -Force 2>$null

Write-Host "  [OK] User-level COM unregistered (Word + WPS)"
