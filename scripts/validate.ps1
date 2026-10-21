param([switch]$Smoke)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    dotnet build AppBox.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/AppBox.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
    if ($Smoke) {
        $process = Start-Process -FilePath '.\src\AppBox.App\bin\Release\net10.0-windows\AppBox.App.exe' -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(30000)) { throw "Diagnostic run timed out (PID $($process.Id))." }
        if ($process.ExitCode -ne 0) { throw 'Diagnostic run failed; inspect artifacts/smoke-error.txt and artifacts/secondary-error.txt.' }
        Get-Content -LiteralPath 'artifacts/smoke/smoke-result.txt'
        Get-Content -LiteralPath 'artifacts/smoke/phase-1c-result.txt'
    }
}
finally { Pop-Location }
