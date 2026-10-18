[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$config = Get-Content -Raw (Join-Path $PSScriptRoot 'quality-config.json') | ConvertFrom-Json
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$outputRoot = Join-Path $repoRoot ".quality-output/$runName"
$sarifRoot = Join-Path $outputRoot 'analysis'
$testRoot = Join-Path $outputRoot 'tests'
New-Item -ItemType Directory -Force $sarifRoot, $testRoot | Out-Null
$buildExit = 1
$testExit = 1
Push-Location $repoRoot
try {
    Write-Host '=== Static analysis: .NET recommended analyzers ===' -ForegroundColor Cyan
    dotnet build $config.sourceProject --no-incremental --disable-build-servers -m:1 '-p:QualityAnalysis=true' "-p:QualityOutputDirectory=$sarifRoot" 2>&1 |
        Tee-Object -FilePath (Join-Path $outputRoot 'build.log')
    $buildExit = $LASTEXITCODE

    Write-Host '=== Unit tests: consuming coverage from the testing contribution ===' -ForegroundColor Cyan
    dotnet test $config.testProject --disable-build-servers -m:1 --collect:'XPlat Code Coverage' --logger 'trx;LogFileName=results.trx' --results-directory $testRoot 2>&1 |
        Tee-Object -FilePath (Join-Path $outputRoot 'tests.log')
    $testExit = $LASTEXITCODE
}
finally { Pop-Location }

$findings = @(
    Get-ChildItem $sarifRoot -Filter '*.sarif' | ForEach-Object {
        $sarif = Get-Content -Raw $_.FullName | ConvertFrom-Json
        foreach ($run in $sarif.runs) {
            foreach ($result in $run.results) {
                if ($null -eq $result) { continue }
                $location = @($result.locations)[0].physicalLocation
                [pscustomobject]@{
                    Rule = $result.ruleId
                    Level = $result.level
                    Message = $result.message.text
                    File = $location.artifactLocation.uri
                    Line = $location.region.startLine
                }
            }
        }
    }
)
$errors = @($findings | Where-Object Level -EQ 'error').Count
$warnings = @($findings | Where-Object Level -EQ 'warning').Count
$notes = @($findings | Where-Object Level -EQ 'note').Count
$coverageFile = Get-ChildItem $testRoot -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
$trxFile = Get-ChildItem $testRoot -Recurse -Filter results.trx | Select-Object -First 1
$analysisFiles = @(Get-ChildItem $sarifRoot -Filter '*.sarif').Count
$lineCoverage = 'Unavailable'
$branchCoverage = 'Unavailable'
$passed = 0
$failed = 0
$total = 0
$skipped = 0
if ($coverageFile) {
    [xml]$coverage = Get-Content -Raw $coverageFile.FullName
    $lineCoverage = ([math]::Round([double]$coverage.coverage.'line-rate' * 100, 2)).ToString('0.00', [cultureinfo]::InvariantCulture) + '%'
    $branchCoverage = ([math]::Round([double]$coverage.coverage.'branch-rate' * 100, 2)).ToString('0.00', [cultureinfo]::InvariantCulture) + '%'
}
if ($trxFile) {
    [xml]$trx = Get-Content -Raw $trxFile.FullName
    $counters = $trx.TestRun.ResultSummary.Counters
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $total = [int]$counters.total
    $skipped = [int]$counters.notExecuted
}
$gatePassed = $buildExit -eq 0 -and $testExit -eq 0 -and $errors -eq 0 -and $analysisFiles -gt 0 -and $null -ne $coverageFile -and $total -gt 0 -and $failed -eq 0
$gate = if ($gatePassed) { 'PASS' } else { 'FAIL' }
$summary = @"
## $($config.name): Code Quality & Metrics

| Quality gate | Static errors | Static warnings | Suggestions | Tests passed / total | Skipped | Line coverage | Branch coverage |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| $gate | $errors | $warnings | $notes | $passed / $total | $skipped | $lineCoverage | $branchCoverage |

Gate: successful analyzer build, no static errors, passing tests, and generated analysis/coverage reports.
Warnings and suggestions remain visible for review; they do not currently block this initial gate.
Coverage includes the assemblies reported by Coverlet; it is not a test pass percentage.
Download **quality-evidence** and open the HTML dashboard. Detailed source findings are in the SARIF files.
"@
$summary | Set-Content (Join-Path $outputRoot 'summary.md')
if ($env:GITHUB_STEP_SUMMARY) { $summary | Add-Content $env:GITHUB_STEP_SUMMARY }

function Encode([object]$value) { [System.Net.WebUtility]::HtmlEncode([string]$value) }
$rows = ($findings | ForEach-Object {
    '<tr><td>' + (Encode $_.Rule) + '</td><td>' + (Encode $_.Level) + '</td><td>' + (Encode $_.Message) + '</td><td>' + (Encode $_.File) + ':' + (Encode $_.Line) + '</td></tr>'
}) -join [Environment]::NewLine
$html = @"
<!doctype html><html lang="en"><meta charset="utf-8"><title>$(Encode $config.name) Quality Report</title>
<style>body{font:16px system-ui;margin:40px;line-height:1.6;color:#172033;background:#f7f9fc}table{border-collapse:collapse;background:white;width:100%;margin:20px 0}th,td{border:1px solid #d6deea;padding:10px;text-align:left;overflow-wrap:anywhere}h1,h2{line-height:1.3}</style>
<h1>$(Encode $config.name): Code Quality &amp; Metrics</h1>
<p>Quality gate: <strong>$gate</strong></p>
<table><tr><th>Static errors</th><th>Warnings</th><th>Suggestions</th><th>Tests passed / total</th><th>Line coverage</th><th>Branch coverage</th></tr>
<tr><td>$errors</td><td>$warnings</td><td>$notes</td><td>$passed / $total</td><td>$lineCoverage</td><td>$branchCoverage</td></tr></table>
<p>Warnings are advisory; build errors, test failures, and missing reports fail the gate. Coverage measures exercised code, not correctness. Dependency-audit notices are recorded separately in build.log.</p>
<h2>Static analysis findings</h2><table><tr><th>Rule</th><th>Severity</th><th>Finding</th><th>Source location</th></tr>$rows</table>
<p>Generated $([datetime]::UtcNow.ToString('u')). No technical-debt estimate or SonarQube rating is calculated by this report.</p></html>
"@
$html | Set-Content (Join-Path $outputRoot 'dashboard.html')
[pscustomobject]@{ Gate = $gate; Errors = $errors; Warnings = $warnings; Suggestions = $notes; Passed = $passed; Total = $total; Skipped = $skipped; LineCoverage = $lineCoverage; BranchCoverage = $branchCoverage; Findings = $findings } |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $outputRoot 'metrics.json')
Write-Host $summary
Write-Host "Dashboard: $(Join-Path $outputRoot 'dashboard.html')"
if (-not $gatePassed) { throw 'Quality gate failed. See build.log, tests.log and dashboard.html.' }
