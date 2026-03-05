# Register-UserCOM.ps1
param(
    [Parameter(Mandatory = $true)]
    [string]$DllPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DllPath)) {
    Write-Error "DLL not found: $DllPath"
    exit 1
}

$DllFullPath = (Resolve-Path $DllPath).Path
$CodeBase = "file:///$($DllFullPath.Replace('\', '/'))"

$AssemblyName  = "FuXing, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
$RuntimeVer    = "v4.0.30319"
$ManagedCATID  = "{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"

# --- Helper: register a single COM class ---
function Register-ComClass {
    param(
        [string]$CLSID,
        [string]$ProgId,
        [string]$ClassName,
        [string]$CodeBase,
        [string]$AssemblyName,
        [string]$RuntimeVer,
        [string]$ManagedCATID
    )

    # 同时在普通路径和 Wow6432Node 路径注册，确保 32 位应用（WPS）也能发现
    $clsidRoots = @(
        "HKCU:\Software\Classes\CLSID\$CLSID",
        "HKCU:\Software\Classes\Wow6432Node\CLSID\$CLSID"
    )
    $progIdRoot = "HKCU:\Software\Classes\$ProgId"

    foreach ($clsidRoot in $clsidRoots) {
        # CLSID
        New-Item -Path $clsidRoot -Force | Out-Null
        Set-ItemProperty -Path $clsidRoot -Name "(Default)" -Value $ClassName

        $inproc = "$clsidRoot\InprocServer32"
        New-Item -Path $inproc -Force | Out-Null
        Set-ItemProperty -Path $inproc -Name "(Default)"       -Value "mscoree.dll"
        Set-ItemProperty -Path $inproc -Name "ThreadingModel"   -Value "Both"
        Set-ItemProperty -Path $inproc -Name "Class"            -Value $ClassName
        Set-ItemProperty -Path $inproc -Name "Assembly"         -Value $AssemblyName
        Set-ItemProperty -Path $inproc -Name "RuntimeVersion"   -Value $RuntimeVer
        Set-ItemProperty -Path $inproc -Name "CodeBase"         -Value $CodeBase

        $inprocVer = "$inproc\1.0.0.0"
        New-Item -Path $inprocVer -Force | Out-Null
        Set-ItemProperty -Path $inprocVer -Name "Class"          -Value $ClassName
        Set-ItemProperty -Path $inprocVer -Name "Assembly"       -Value $AssemblyName
        Set-ItemProperty -Path $inprocVer -Name "RuntimeVersion" -Value $RuntimeVer
        Set-ItemProperty -Path $inprocVer -Name "CodeBase"       -Value $CodeBase

        New-Item -Path "$clsidRoot\ProgId" -Force | Out-Null
        Set-ItemProperty -Path "$clsidRoot\ProgId" -Name "(Default)" -Value $ProgId

        New-Item -Path "$clsidRoot\Implemented Categories\$ManagedCATID" -Force | Out-Null
    }

    # ProgId -> CLSID
    New-Item -Path $progIdRoot -Force | Out-Null
    Set-ItemProperty -Path $progIdRoot -Name "(Default)" -Value $ClassName
    New-Item -Path "$progIdRoot\CLSID" -Force | Out-Null
    Set-ItemProperty -Path "$progIdRoot\CLSID" -Name "(Default)" -Value $CLSID

    Write-Host "       $ProgId -> $CLSID"
}

# 1. Register Connect (add-in entry point)
Register-ComClass -CLSID "{C9F68F90-E8C4-4A8B-9A8B-5E6F7D8E9F0A}" `
                  -ProgId "FuXing.Connect" -ClassName "FuXing.Connect" `
                  -CodeBase $CodeBase -AssemblyName $AssemblyName `
                  -RuntimeVer $RuntimeVer -ManagedCATID $ManagedCATID

# 2. Register TaskPaneControl (required for CustomTaskPane creation)
Register-ComClass -CLSID "{03326A51-B257-3623-917E-25A086B271B0}" `
                  -ProgId "FuXing.TaskPaneControl" -ClassName "FuXing.TaskPaneControl" `
                  -CodeBase $CodeBase -AssemblyName $AssemblyName `
                  -RuntimeVer $RuntimeVer -ManagedCATID $ManagedCATID

# 3. Office Add-in（Microsoft Word）
$addinKey = "HKCU:\Software\Microsoft\Office\Word\Addins\FuXing.Connect"
New-Item -Path $addinKey -Force | Out-Null
Set-ItemProperty -Path $addinKey -Name "Description"    -Value ([string]::new([char[]]@(0x798F,0x661F,0x63D2,0x4EF6,0x0020,0x002D,0x0020,0x0041,0x0049,0x6587,0x672C,0x7EA0,0x9519,0x3001,0x6807,0x51C6,0x6821,0x9A8C,0x3001,0x8868,0x683C,0x683C,0x5F0F,0x5316)))
Set-ItemProperty -Path $addinKey -Name "FriendlyName"   -Value ([string]::new([char[]]@(0x798F,0x661F)))
Set-ItemProperty -Path $addinKey -Name "LoadBehavior"   -Value 3 -Type DWord

# 4. WPS 文字 Add-in
$wpsAddinKey = "HKCU:\Software\Kingsoft\Office\WPS\Addins\FuXing.Connect"
New-Item -Path $wpsAddinKey -Force | Out-Null
Set-ItemProperty -Path $wpsAddinKey -Name "Description"    -Value ([string]::new([char[]]@(0x798F,0x661F,0x63D2,0x4EF6,0x0020,0x002D,0x0020,0x0041,0x0049,0x6587,0x672C,0x7EA0,0x9519,0x3001,0x6807,0x51C6,0x6821,0x9A8C,0x3001,0x8868,0x683C,0x683C,0x5F0F,0x5316)))
Set-ItemProperty -Path $wpsAddinKey -Name "FriendlyName"   -Value ([string]::new([char[]]@(0x798F,0x661F)))
Set-ItemProperty -Path $wpsAddinKey -Name "LoadBehavior"   -Value 3 -Type DWord

# 5. WPS 白名单（WPS 只加载白名单中的 Add-in）
$wpsWL = "HKCU:\Software\Kingsoft\Office\WPS\AddinsWL"
New-Item -Path $wpsWL -Force | Out-Null
New-ItemProperty -Path $wpsWL -Name "FuXing.Connect" -Value "" -PropertyType String -Force | Out-Null

Write-Host "  [OK] User-level COM registered (Word + WPS)"
Write-Host "       CodeBase = $CodeBase"