<#
.SYNOPSIS
    One-time setup that grants PhoneNotificationsVR.exe *package identity* via a sparse package, so the
    Windows UserNotificationListener API works. Run once (and again only if you move the .exe).

.DESCRIPTION
    Steps performed:
      1. Generate placeholder logo PNGs (so the manifest has valid assets).
      2. Create a self-signed code-signing certificate whose subject matches the manifest Publisher, and
         trust it (LocalMachine\TrustedPeople) so Windows will accept the sparse package.
      3. Pack the manifest + assets into a signed sparse .msix (MakeAppx + SignTool).
      4. Register it with -ExternalLocation pointing at your built app folder.

.NOTES
    * Run in an ELEVATED PowerShell (Administrator) — trusting a cert and registering a package need it.
    * Requires the Windows 10/11 SDK on PATH (makeappx.exe, signtool.exe). Install "Windows SDK" via the
      Visual Studio Installer if these are missing.
    * This is a DEV/self-signed flow. For distribution, sign with a real certificate.

.PARAMETER AppDir
    Folder containing the built PhoneNotificationsVR.exe. Defaults to the Release build output.

.EXAMPLE
    # from an elevated PowerShell, in the repo root:
    ./packaging/Register-Identity.ps1
#>

[CmdletBinding()]
param(
    [string]$AppDir = "$PSScriptRoot\..\src\App\bin\Release\net8.0-windows10.0.19041.0",
    [string]$Publisher = "CN=PhoneNotificationsVR Dev",
    [string]$PackageName = "PhoneNotificationsVR"
)

$ErrorActionPreference = "Stop"
$work    = Join-Path $PSScriptRoot "build"
$layout  = Join-Path $work "layout"
$assets  = Join-Path $layout "Assets"
$msix    = Join-Path $work "PhoneNotificationsVR.Sparse.msix"

# --- Resolve the app folder --------------------------------------------------------------------
$AppDir = (Resolve-Path $AppDir -ErrorAction Stop).Path
if (-not (Test-Path (Join-Path $AppDir "PhoneNotificationsVR.exe"))) {
    throw "PhoneNotificationsVR.exe not found in '$AppDir'. Build the app first: dotnet build -c Release"
}
Write-Host "App folder: $AppDir" -ForegroundColor Cyan

# --- 1. Layout + placeholder logos -------------------------------------------------------------
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot "AppxManifest.xml") (Join-Path $layout "AppxManifest.xml")

Add-Type -AssemblyName System.Drawing
function New-Logo([string]$path, [int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 94, 132, 241))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
New-Logo (Join-Path $assets "StoreLogo.png")        50  50
New-Logo (Join-Path $assets "Square150x150Logo.png") 150 150
New-Logo (Join-Path $assets "Square44x44Logo.png")   44  44

# --- 2. Self-signed cert (subject must equal the manifest Publisher) ----------------------------
Write-Host "Creating / trusting self-signed certificate for $Publisher" -ForegroundColor Cyan
$cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
    -KeyUsage DigitalSignature -FriendlyName "PhoneNotificationsVR Dev" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

# Export the private key (.pfx) for signing, and the public cert (.cer) to trust.
$pwd = ConvertTo-SecureString -String "pnvr-dev" -Force -AsPlainText
$pfx = Join-Path $work "signing.pfx"
$cer = Join-Path $work "cer.cer"
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
Export-Certificate   -Cert $cert -FilePath $cer | Out-Null
# Trust the public cert so Windows accepts the package signature.
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

# --- 3. Pack + sign the sparse package ---------------------------------------------------------
function Find-SdkTool([string]$name) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $root = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    $hit = Get-ChildItem $root -Recurse -Filter $name -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -match "x64" } | Select-Object -First 1
    if (-not $hit) { throw "$name not found. Install the Windows 10/11 SDK." }
    return $hit.FullName
}
$makeappx = Find-SdkTool "makeappx.exe"
$signtool = Find-SdkTool "signtool.exe"

Write-Host "Packing sparse package…" -ForegroundColor Cyan
& $makeappx pack /d $layout /p $msix /nv /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed." }

Write-Host "Signing…" -ForegroundColor Cyan
& $signtool sign /fd SHA256 /a /f $pfx /p "pnvr-dev" $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed." }

# --- 4. Register with external location --------------------------------------------------------
Write-Host "Registering sparse package (external location = app folder)…" -ForegroundColor Cyan
Add-AppxPackage -Path $msix -ExternalLocation $AppDir

Write-Host ""
Write-Host "Done. PhoneNotificationsVR now has package identity." -ForegroundColor Green
Write-Host "Launch the app, then approve the notification-access prompt (or Windows Settings ▸" -ForegroundColor Green
Write-Host "Privacy & security ▸ Notifications)." -ForegroundColor Green
Write-Host ""
Write-Host "To remove identity later:  Get-AppxPackage *PhoneNotificationsVR* | Remove-AppxPackage" -ForegroundColor DarkGray
