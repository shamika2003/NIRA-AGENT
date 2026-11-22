$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/Nira.Desktop/Nira.Desktop.csproj'
& dotnet run --project $project
if ($LASTEXITCODE -ne 0) { throw 'NIRA desktop exited with an error.' }
