# 配布用の zip を作る。画面（Pogoreco.exe）と記録係（Pogoreco.Recorder.exe）を同じフォルダに出す。
# 使い方: pwsh scripts/publish.ps1        （バージョンは Directory.Build.props の Version）
# 出力:   dist/pogoreco-<バージョン>-win-x64.zip
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

[xml]$props = Get-Content Directory.Build.props
$version = $props.Project.PropertyGroup.Version
if (-not $version) { throw 'Directory.Build.props に Version がありません' }

$name = "pogoreco-$version-win-x64"
$out = "dist/$name"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force dist | Out-Null

foreach ($proj in 'src/Pogoreco.App', 'src/Pogoreco.Recorder') {
    dotnet publish $proj -c Release -r win-x64 --self-contained true -o $out
    if ($LASTEXITCODE -ne 0) { throw "publish 失敗: $proj" }
}
Remove-Item "$out/*.pdb" -Force -ErrorAction SilentlyContinue
Copy-Item README.md "$out/README.md"

$zip = "dist/$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out/*" -DestinationPath $zip
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "作成しました: $zip ($mb MB)"
