param(
    [string]$UnityEditor,
    [string]$Version = '0.1.0',
    [switch]$SkipPreviewerBuild,
    [string]$PreviewerDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
$packageName = "SpineTool-Win64-$Version"
$package = Join-Path $artifacts $packageName
if (Test-Path -LiteralPath $package) { throw "Output already exists: $package. Choose a new version or move the previous build." }
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
if (-not $SkipPreviewerBuild) {
    if (-not $UnityEditor -or -not (Test-Path -LiteralPath $UnityEditor)) { throw 'Pass -UnityEditor with the Unity 2022.3.62f2 executable path.' }
    $PreviewerDirectory = Join-Path $artifacts 'previewer'
    $previewOutput = Join-Path $PreviewerDirectory 'SpinePreviewer.exe'
    $previewLog = Join-Path $artifacts 'previewer-build.log'
    $previewProject = Join-Path $repoRoot 'Tools/SpinePreviewer'
    $arguments = @('-batchmode', '-quit', '-projectPath', ('"' + $previewProject + '"'), '-executeMethod', 'PreviewBuild.BuildWindows', '-previewOutput', ('"' + $previewOutput + '"'), '-logFile', ('"' + $previewLog + '"'))
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Unity build failed. See $previewLog" }
}
if (-not $PreviewerDirectory -or -not (Test-Path -LiteralPath (Join-Path $PreviewerDirectory 'SpinePreviewer.exe'))) { throw 'A complete Previewer build directory is required.' }
Push-Location $repoRoot
try {
    $app = Join-Path $package 'App'
    dotnet publish Tools/SpineTool/SpineTool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $app
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    New-Item -ItemType Directory -Path (Join-Path $app 'Previewer') -Force | Out-Null
    Copy-Item -Path (Join-Path $PreviewerDirectory '*') -Destination (Join-Path $app 'Previewer') -Recurse -Force
    Set-Content -LiteralPath (Join-Path $app 'portable.flag') -Value 'portable' -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $repoRoot 'Licenses') -Destination (Join-Path $package 'Licenses') -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/USER_GUIDE.md') -Destination (Join-Path $package '使用说明.md')
    $guideHtml = (ConvertFrom-Markdown -LiteralPath (Join-Path $repoRoot 'docs/USER_GUIDE.md')).Html
    $page = '<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>SpineTool 使用说明</title><style>body{font-family:Microsoft YaHei,sans-serif;max-width:1000px;margin:36px auto;padding:0 24px;line-height:1.7;color:#182434}table{border-collapse:collapse}th,td{border:1px solid #cbd5e1;padding:10px}code{background:#edf2f7;padding:2px 4px}h1,h2{color:#1756a0}</style></head><body>' + $guideHtml + '</body></html>'
    Set-Content -LiteralPath (Join-Path $package '使用说明.html') -Value $page -Encoding UTF8
    @'
完整解压后，直接运行 App\SpineTool.exe。
独立预览运行 App\Previewer\SpinePreviewer.exe。
首次导出请设置本机已激活的 Spine 4.1 / Spine.com。
修改参数后先保存，再回工程选择对应配置。
内置“无”“齿轮士兵”；个人数据位于 App\UserData。
请完整保留 App 目录。详细说明见 使用说明.md。
'@ | Set-Content -LiteralPath (Join-Path $package '请先阅读.txt') -Encoding UTF8
    $zip = Join-Path $artifacts "$packageName.zip"
    Compress-Archive -LiteralPath $package -DestinationPath $zip
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $packageName.zip" | Set-Content -LiteralPath (Join-Path $artifacts "$packageName.sha256") -Encoding ASCII
    Write-Output "Package: $zip"
} finally { Pop-Location }
