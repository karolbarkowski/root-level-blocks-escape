$ErrorActionPreference = 'Stop'
$previousAppData = $env:APPDATA
Push-Location $PSScriptRoot
try {
    # Keep NuGet's configuration discovery inside this workspace in sandboxed hosts.
    $env:APPDATA = Join-Path $PSScriptRoot '.build-env'
    $solution = '../LevelGenerator.slnx'
    dotnet restore $solution --configfile .\NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    # The solution builds BlockPuzzle.Core, LevelGenerator, LevelGenerator.Tools and the tests.
    dotnet build $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet ..\LevelGenerator.Tests\bin\Release\net10.0\LevelGenerator.Tests.dll
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    $env:APPDATA = $previousAppData
    Pop-Location
}
