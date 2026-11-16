$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    foreach ($project in @('SpineExporter.Tests', 'SpineValidator.Tests', 'SpineTool.Tests')) {
        dotnet run --project "Tools/$project/$project.csproj" --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "$project failed ($LASTEXITCODE)." }
    }
} finally { Pop-Location }
