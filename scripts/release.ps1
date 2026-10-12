# インストーラーと自動更新用のファイルを作る（Velopack）。アップロードはしない。
# 使い方: powershell -ExecutionPolicy Bypass -File scripts/release.ps1
# 事前に: dotnet tool install -g vpk
# 出力:   dist/releases/（Pogoreco-win-Setup.exe、Pogoreco-<版>-full.nupkg、releases.win.json など）
# GitHub に出すとき: git tag vX.Y.Z → git push origin vX.Y.Z → gh release create vX.Y.Z dist/releases/*
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

& "$PSScriptRoot/publish.ps1"
if ($LASTEXITCODE -ne 0) { throw 'publish 失敗' }

[xml]$props = Get-Content Directory.Build.props
$version = $props.Project.PropertyGroup.Version
$packDir = "dist/pogoreco-$version-win-x64"

# 過去の版を取り込めるよう、すでに GitHub にある版を取得しておく（差分更新のため。初回は無くてよい）
vpk download github --repoUrl https://github.com/naoki1714/pogoreco --outputDir dist/releases 2>$null

vpk pack --packId Pogoreco --packTitle pogoreco --packVersion $version `
    --packDir $packDir --mainExe Pogoreco.exe --outputDir dist/releases
if ($LASTEXITCODE -ne 0) { throw 'vpk pack 失敗' }
Write-Host "作成しました: dist/releases"
