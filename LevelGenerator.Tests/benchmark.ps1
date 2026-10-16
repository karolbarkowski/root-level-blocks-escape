param([int]$Seconds = 15, [string]$Output = 'output/benchmark')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # Generation has no deadline option, so each run is capped here: the first verified
    # level within the budget counts; a run still searching at the deadline is stopped.
    $rows = @()
    foreach ($seed in @(44, 45, 71)) {
        foreach ($method in @('random', 'anneal')) {
            $folder = Join-Path $Output "$seed-$method"
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            Get-ChildItem -LiteralPath $folder -File | Where-Object Extension -in '.json', '.html', '.svg' | Remove-Item
            $arguments = "../LevelGenerator/bin/Release/net10.0/LevelGenerator.dll generate --seed $seed --method $method --neutral false --layered false --frozen false --directional false --min-opening 4 --maxCandidates 1 --out `"$folder`""
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $process = Start-Process dotnet -ArgumentList $arguments -NoNewWindow -PassThru `
                -RedirectStandardOutput (Join-Path $folder 'console.txt') -RedirectStandardError (Join-Path $folder 'errors.txt')
            $timedOut = -not $process.WaitForExit($Seconds * 1000)
            if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
            $watch.Stop()
            $runPath = Join-Path $folder 'run.json'
            $run = if (Test-Path -LiteralPath $runPath) { Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json } else { $null }
            $path = if ($run.exported -gt 0) { Join-Path $folder $run.files[0] } else { $null }
            $level = if ($path -and (Test-Path -LiteralPath $path)) { Get-Content -Raw -LiteralPath $path | ConvertFrom-Json } else { $null }
            $rows += [pscustomobject]@{
                seed = $seed; method = $method; searchBudgetSeconds = $Seconds; timedOut = $timedOut
                exitCode = if ($timedOut) { $null } else { $process.ExitCode }
                attempts = $run.attempts; evaluated = $run.evaluated; exported = [int]$run.exported
                openingLowerBound = $level.analysis.opening.lowerBound
                openingExact = $level.analysis.opening.exact
                solutionMoves = $level.solution.Count; optimal = $level.analysis.solutionOptimal
                difficulty = $level.analysis.difficulty; occupancy = $level.analysis.occupancy; elapsedSeconds = [math]::Round($watch.Elapsed.TotalSeconds, 2)
            }
            $rows | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'summary.json') -Encoding utf8
            Write-Host "seed=$seed method=$method exported=$([int]$run.exported) opening=$($level.analysis.opening.lowerBound) timedOut=$timedOut"
        }
    }
}
finally { Pop-Location }
