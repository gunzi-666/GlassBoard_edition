param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[\w.-]+/[\w.-]+$')][string]$Repository,
    [string]$OutDir = 'dist'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$build = Join-Path $root 'source\bin\Release\net48'

$rules = Get-Content -Raw -LiteralPath (Join-Path $root 'source\GlassBoard\UpdateRules.cs')
$configured = [regex]::Match($rules, 'RepositoryPath\s*=\s*"([^"]+)"').Groups[1].Value
if ($configured -ne $Repository) {
    throw "UpdateRules.RepositoryPath 是 '$configured'，与当前仓库 '$Repository' 不一致，发布出去的程序会到别的仓库检查更新。请先修改 source\GlassBoard\UpdateRules.cs。"
}

# 必须与 UpdateRules.allowed 一致：多出或缺少文件，客户端都会拒绝整个更新包
$files = [ordered]@{
    'GlassBoard.exe'                 = Join-Path $build 'GlassBoard.exe'
    'GlassBoard.exe.config'          = Join-Path $build 'GlassBoard.exe.config'
    'GlassBoard.Updater.exe'         = Join-Path $root 'GlassBoard.Updater.exe'
    'Interop.UIAutomationClient.dll' = Join-Path $root 'Interop.UIAutomationClient.dll'
    'README.md'                      = Join-Path $root 'README.md'
    'THIRD_PARTY_NOTICES.md'         = Join-Path $root 'THIRD_PARTY_NOTICES.md'
    'Start.cmd'                      = Join-Path $root 'Start.cmd'
    'RecoverWidgets.cmd'             = Join-Path $root 'RecoverWidgets.cmd'
    'RestoreDesktop.cmd'             = Join-Path $root 'RestoreDesktop.cmd'
    'CheckMemory.cmd'                = Join-Path $root 'CheckMemory.cmd'
    'CheckWindows.cmd'               = Join-Path $root 'CheckWindows.cmd'
    'CollectDiagnostics.cmd'         = Join-Path $root 'CollectDiagnostics.cmd'
    'src/CheckMemory.ps1'            = Join-Path $root 'src\CheckMemory.ps1'
    'src/CheckWindows.ps1'           = Join-Path $root 'src\CheckWindows.ps1'
    'src/CollectDiagnostics.ps1'     = Join-Path $root 'src\CollectDiagnostics.ps1'
}
# data 目录里只能有 AppGroups.tsv 和分组对照说明.md 两个文件（同样受白名单限制），按目录收集以免脚本里写中文文件名
$dataFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'data') -File)
if ($dataFiles.Count -ne 2) {
    throw ('data 目录应只有 2 个文件，实际为：' + (($dataFiles | ForEach-Object Name) -join ', '))
}
foreach ($file in $dataFiles) {
    $files["data/$($file.Name)"] = $file.FullName
}

$missing = @($files.GetEnumerator() | Where-Object { -not (Test-Path -LiteralPath $_.Value -PathType Leaf) } | ForEach-Object Key)
if ($missing.Count -gt 0) {
    throw ('缺少打包文件：' + ($missing -join ', '))
}

$exeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($files['GlassBoard.exe']).FileVersion
if ($exeVersion -ne "$Version.0") {
    throw "GlassBoard.exe 的文件版本是 $exeVersion，应为 $Version.0；客户端会以「主程序版本与更新包名称不一致」拒绝更新。"
}

if (Test-Path -LiteralPath $OutDir) {
    Remove-Item -LiteralPath $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$name = "GlassBoard_v${Version}_User.zip"
$zipPath = Join-Path $OutDir $name

$sums = foreach ($entry in $files.GetEnumerator()) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.Value).Hash.ToLowerInvariant()
    "$hash  $($entry.Key)"
}
$sumsText = ($sums -join "`n") + "`n"

$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $files.GetEnumerator()) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value, "GlassBoard/$($entry.Key)", [IO.Compression.CompressionLevel]::Optimal)
    }
    $sumsEntry = $zip.CreateEntry('GlassBoard/SHA256SUMS.txt', [IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object IO.StreamWriter($sumsEntry.Open(), (New-Object Text.UTF8Encoding($false)))
    try {
        $writer.Write($sumsText)
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $zip.Dispose()
}

$check = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $names = @($check.Entries | ForEach-Object FullName)
    $expected = @($files.Keys | ForEach-Object { "GlassBoard/$_" }) + 'GlassBoard/SHA256SUMS.txt'
    $diff = Compare-Object -ReferenceObject $expected -DifferenceObject $names
    if ($diff) {
        throw ('更新包条目与预期不符：' + ($diff | Out-String))
    }
}
finally {
    $check.Dispose()
}

$zipItem = Get-Item -LiteralPath $zipPath
$manifest = [ordered]@{
    name                 = $name
    size                 = $zipItem.Length
    browser_download_url = "https://github.com/$Repository/releases/download/v$Version/$name"
    digest               = 'sha256:' + (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    type                 = 'file'
}
$manifestPath = Join-Path $OutDir 'update.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))

Write-Host "已生成：$zipPath（$($zipItem.Length) 字节）"
Write-Host "已生成：$manifestPath"
if ($env:GITHUB_OUTPUT) {
    "zip=$zipPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "manifest=$manifestPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
