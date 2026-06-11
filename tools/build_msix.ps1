<#
  build_msix.ps1 - builds the universal CRMRS desktop app into a Microsoft-Store
  ready, UNSIGNED .msix, entirely from the command line (no Visual Studio).

  The Microsoft Store SIGNS the package for you, so we never sign it here - that
  is what makes Store publishing free (no code-signing certificate to buy).

  The three identity values come from Partner Center:
    Product management > Product Identity
      -IdentityName          = "Package/Identity/Name"
      -Publisher             = "Package/Identity/Publisher"
      -PublisherDisplayName  = "Package/Properties/PublisherDisplayName"

  For CRMRS they are:
    -IdentityName "CRMRS.CRMRS"
    -Publisher "CN=1D45CB9D-7DEA-4FAC-88CB-D0A3B116B404"
    -PublisherDisplayName "CRMRS"

  Example (first build):
    powershell -File tools\build_msix.ps1 -IdentityName "CRMRS.CRMRS" -Publisher "CN=1D45CB9D-7DEA-4FAC-88CB-D0A3B116B404" -PublisherDisplayName "CRMRS" -Version "1.0.0.0" -Republish

  Example (later UPDATE - just bump the 3rd number, keep the 4th as 0):
    powershell -File tools\build_msix.ps1 -IdentityName "CRMRS.CRMRS" -Publisher "CN=1D45CB9D-7DEA-4FAC-88CB-D0A3B116B404" -PublisherDisplayName "CRMRS" -Version "1.0.1.0" -Republish

  Output:  CRMRS.Package\CRMRS_store.msix   (upload it in Partner Center > Packages)
#>
param(
  [Parameter(Mandatory=$true)][string]$IdentityName,
  [Parameter(Mandatory=$true)][string]$Publisher,
  [Parameter(Mandatory=$true)][string]$PublisherDisplayName,
  [string]$Version = "1.0.0.0",
  [switch]$Republish
)
$ErrorActionPreference='Stop'
$root   = Split-Path $PSScriptRoot -Parent
$pkg    = Join-Path $root "CRMRS.Package"
$layout = Join-Path $pkg  "pkg_layout"
$csproj = Join-Path $root "VKdesktopapp\CRMRSDesktopApp.csproj"

$makeappx = Get-ChildItem (Join-Path $PSScriptRoot "_sdk") -Recurse -Filter makeappx.exe |
            Where-Object { $_.FullName -match 'x64' } | Select-Object -First 1
if (-not $makeappx) { throw "makeappx.exe not found under tools/_sdk. Re-run the SDK BuildTools download." }

# 1) publish the self-contained universal app (skip if already built unless -Republish)
if ($Republish -or -not (Test-Path (Join-Path $layout "CRMRS.exe"))) {
  if (Test-Path $layout) { Get-ChildItem $layout -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
  dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $layout --nologo
}

# 2) patch the manifest identity with the real Partner Center values
[xml]$m = Get-Content (Join-Path $pkg "Package.appxmanifest")
$m.Package.Identity.Name      = $IdentityName
$m.Package.Identity.Publisher = $Publisher
$m.Package.Identity.Version   = $Version
$m.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$m.Save((Join-Path $layout "AppxManifest.xml"))

# 3) stage images + pack
Copy-Item (Join-Path $pkg "Images") (Join-Path $layout "Images") -Recurse -Force
$out = Join-Path $pkg "CRMRS_store.msix"
& $makeappx.FullName pack /o /d $layout /p $out | Out-Null
if ($LASTEXITCODE -ne 0) { throw ("makeappx failed: " + $LASTEXITCODE) }

$mb = [math]::Round((Get-Item $out).Length/1MB,1)
Write-Output ("DONE -> " + $out + "  (" + $mb + " MB)")
Write-Output "Upload that file in Partner Center: your MSIX product > Packages."
