$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/Nira.Cli/Nira.Cli.csproj'
& dotnet run --project $project
if ($LASTEXITCODE -ne 0) { throw 'NIRA CLI exited with an error.' }
