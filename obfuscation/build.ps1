<#
.SYNOPSIS
  Builds the obfuscated, customer-deliverable PDecision folder.

.DESCRIPTION
  1. dotnet publish PDecision in Release (framework-dependent: the customer runs .NET 8 already).
  2. Stages a copy, obfuscates PDecision.dll with Obfuscar using Obfuscar.template.xml (see that file and
     README.md for why each SkipType entry exists). DecisionMaking.dll and its PokerUtil dependency are never
     obfuscated - both ship unchanged from the Release publish output so their type names remain compatible
     with the legacy strategy code, which has been observed to behave differently after metadata/member
     transformations.
  3. Assembles -OutDir by copying the published output and overwriting PDecision.dll with the obfuscated one.
     Everything else (third-party DLLs, appsettings.json, images, nlog.config) ships unmodified.

  Needs a locally built obfuscar.console - see README.md "Building obfuscar.console from source". The
  2.2.50 NuGet release does not include the cross-assembly rename fix (GitHub issue #316) this project
  depends on; check NuGet again before rebuilding obfuscar yourself, in case a newer release has it.

.PARAMETER ObfuscarConsole
  Path to GlobalTools.dll (or a published obfuscar.console.exe) built per README.md.

.PARAMETER OutDir
  Where to put the finished deliverable folder. Default: .\dist next to this script.

.PARAMETER WorkDir
  Scratch folder for the publish/stage/obfuscate steps. Default: .\work next to this script. Safe to delete
  between runs; both this and OutDir are gitignored.

.EXAMPLE
  .\build.ps1 -ObfuscarConsole C:\tools\obfuscar\GlobalTools.dll
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ObfuscarConsole,
    [string]$OutDir = (Join-Path $PSScriptRoot "dist"),
    [string]$WorkDir = (Join-Path $PSScriptRoot "work")
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $repoRoot "PDecision\PDecision.csproj"

if (-not (Test-Path $ObfuscarConsole)) {
    throw "ObfuscarConsole not found: $ObfuscarConsole (see README.md to build it)"
}

Write-Host "`n== 1/4  dotnet publish ==" -ForegroundColor Cyan
# the private PokerSuite feed (pkgs.dev.azure.com) 401s outside a dev machine's own credentials; restore from
# the local package caches instead, same as every other build in this project
dotnet restore $csproj -s C:\NuGet\Packages -s $env:USERPROFILE\.nuget\packages
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

$published = Join-Path $WorkDir "framework-dependent"
Remove-Item $published -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $csproj -c Release --no-restore -o $published
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "`n== 2/4  stage + generate Obfuscar.xml ==" -ForegroundColor Cyan
$staging = Join-Path $WorkDir "obf-staging"
$obfOut = Join-Path $WorkDir "obf-output"
Remove-Item $staging, $obfOut -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $published $staging -Recurse

$template = Get-Content (Join-Path $PSScriptRoot "Obfuscar.template.xml") -Raw
$generatedXml = Join-Path $WorkDir "Obfuscar.xml"
$template = $template.Replace("{{WORKDIR}}", $WorkDir)
# DecisionMaking.dll and PokerUtil.dll are never obfuscated - see the .DESCRIPTION comment above.
$decisionMakingModulePath = [regex]::Escape((Join-Path $WorkDir "obf-staging\DecisionMaking.dll"))
$modulePattern = '(?s)\s*<Module file="' + $decisionMakingModulePath + '">.*?</Module>'
$template = [regex]::Replace($template, $modulePattern, "")
$pokerUtilModulePath = [regex]::Escape((Join-Path $WorkDir "obf-staging\PokerUtil.dll"))
$modulePattern = '(?s)\s*<Module file="' + $pokerUtilModulePath + '">.*?</Module>'
$template = [regex]::Replace($template, $modulePattern, "")
Write-Host "DecisionMaking.dll and PokerUtil.dll will remain unchanged" -ForegroundColor Yellow
$template | Set-Content $generatedXml -Encoding utf8

Write-Host "`n== 3/4  obfuscate ==" -ForegroundColor Cyan
dotnet $ObfuscarConsole $generatedXml
if ($LASTEXITCODE -ne 0) { throw "obfuscar failed" }

$mapping = Join-Path $obfOut "Mapping.txt"
if (Test-Path $mapping) {
    Write-Host "`n*** Mapping.txt reverses the obfuscation. Keep it out of the deliverable and out of git. ***" -ForegroundColor Yellow
    Write-Host "    ($mapping)" -ForegroundColor Yellow
}

Write-Host "`n== 4/4  assemble deliverable ==" -ForegroundColor Cyan
Remove-Item $OutDir -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $published $OutDir -Recurse
Copy-Item (Join-Path $obfOut "PDecision.dll") $OutDir -Force

Write-Host "`nDeliverable: $OutDir" -ForegroundColor Green
Write-Host "Before shipping: edit $OutDir\appsettings.json for the customer (Database:ConnectionString is the" -ForegroundColor Green
Write-Host "pdecision_client login - never the admin one), and run the tests in README.md ""Testing a build""." -ForegroundColor Green
