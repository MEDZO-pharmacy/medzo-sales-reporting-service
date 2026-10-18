# medzo-sales-reporting-service

Medzo Sales and Reporting Microservice.

## Mutation testing

This branch uses Stryker.NET 5 to assess whether the unit tests detect deliberate
faults in the business-critical sale, catalogue search, expiry alert, and batch
removal services. The tool version is pinned in `.config/dotnet-tools.json` so
local and CI runs use the same version.

Run the checks from the repository root:

```powershell
dotnet tool restore
dotnet test Medzo.SalesReporting.slnx --configuration Release
dotnet stryker --config-file stryker-config.json
```

The verified baseline has 30 passing unit tests and a 69.78% mutation score
(157 killed, 37 survived, and 31 uncovered mutants). CI fails below 65%, while
75% remains the improvement target. A surviving mutant is not hidden: it is a
specific test-quality gap to inspect and discuss rather than a normal test
failure.

Stryker writes local HTML, JSON, and Markdown reports under `StrykerOutput/`.
That generated directory is ignored by Git. The mutation-testing GitHub Actions
workflow runs the same commands and uploads all three report formats as a
downloadable `sales-mutation-reports-*` artifact, even when the quality gate
fails.
