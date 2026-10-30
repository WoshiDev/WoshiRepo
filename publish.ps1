# Builds a plugin, uploads it as a GitHub release, and updates repo.json so Dalamud sees the new version.
# Usage: .\publish.ps1 RetainerUndercut   (bump <Version> in the .csproj first)
param([Parameter(Mandatory)][string]$Plugin)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet build $Plugin -c Release
if ($LASTEXITCODE) { throw "Build failed" }

$out = "$Plugin\bin\Release\$Plugin"
$manifest = Get-Content "$out\$Plugin.json" -Raw | ConvertFrom-Json
$repo = gh repo view --json nameWithOwner -q .nameWithOwner
$tag = "$Plugin-$($manifest.AssemblyVersion)"

gh release view $tag *> $null
if ($LASTEXITCODE) { gh release create $tag "$out\latest.zip" --title "$($manifest.Name) $($manifest.AssemblyVersion)" --notes "" }
else { gh release upload $tag "$out\latest.zip" --clobber }
if ($LASTEXITCODE) { throw "Release upload failed" }

$zip = "https://github.com/$repo/releases/download/$tag/latest.zip"
$manifest | Add-Member -Force RepoUrl "https://github.com/$repo/tree/main/$Plugin"
$manifest | Add-Member -Force IconUrl "https://raw.githubusercontent.com/$repo/main/$Plugin/images/icon.png"
$manifest | Add-Member -Force DownloadLinkInstall $zip
$manifest | Add-Member -Force DownloadLinkUpdate $zip
$manifest | Add-Member -Force DownloadLinkTesting $zip
$manifest | Add-Member -Force LastUpdate ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())

$entries = @(Get-Content repo.json -Raw | ConvertFrom-Json | Where-Object InternalName -ne $Plugin) + $manifest
ConvertTo-Json -InputObject @($entries | Sort-Object InternalName) -Depth 5 | Set-Content repo.json

git add repo.json
git commit -m "Publish $tag"
# Push with gh's login so this works without a separate git sign-in.
git -c credential.helper= -c "credential.helper=!gh auth git-credential" push
