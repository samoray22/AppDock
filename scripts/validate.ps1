param([switch]$Smoke, [switch]$Isolated)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $buildArguments = @('build', 'AppBox.sln', '-c', 'Release')
    if ($Isolated) {
        $isolatedRoot = Join-Path $PWD 'artifacts\phase2b-bin\'
        $buildArguments += "-p:BaseOutputPath=$isolatedRoot"
        $appExecutable = Join-Path $isolatedRoot 'Release\net10.0-windows\AppDock.exe'
        $testExecutable = Join-Path $isolatedRoot 'Release\net10.0-windows\AppBox.Tests.exe'
    } else {
        $appExecutable = Join-Path $PWD 'src\AppBox.App\bin\Release\net10.0-windows\AppDock.exe'
        $testExecutable = Join-Path $PWD 'tests\AppBox.Tests\bin\Release\net10.0-windows\AppBox.Tests.exe'
    }
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
    if ($Smoke) {
        $process = Start-Process -FilePath $appExecutable -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(60000)) { throw "Diagnostic run timed out (PID $($process.Id))." }
        if ($process.ExitCode -ne 0) { throw 'Diagnostic run failed; inspect artifacts/smoke-error.txt and artifacts/secondary-error.txt.' }
        foreach ($report in 'smoke-result.txt', 'phase-1c-result.txt', 'phase-2a-result.txt', 'phase-2b-result.txt') { Get-Content -LiteralPath (Join-Path 'artifacts/smoke' $report) }
    }
}
finally { Pop-Location }
