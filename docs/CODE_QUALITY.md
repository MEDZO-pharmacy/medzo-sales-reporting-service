# Code Quality & Metrics — Sales and Reporting

Owner: Dayan-02. Branch: `feature/se3112-code-quality`.

## Contribution and tools

This contribution enables the .NET 10 recommended static analyzers, collects one
SARIF report per production project, consumes Coverlet coverage from the existing
unit tests, and builds an HTML dashboard and a GitHub Actions summary.

The unit tests were implemented on the separate Unit Testing & Mocking branch.
This quality contribution reuses their results and does not claim ownership of
those tests. Infrastructure is not required.

## Local demo

Install .NET 10 and PowerShell 7, then run from this repository:

```powershell
pwsh -File scripts/Run-CodeQuality.ps1
```

The console prints analyzer findings, test output, coverage percentages and the
dashboard's location. Open the printed `dashboard.html` path in a browser.
Each invocation gets a new folder under `.quality-output/`, so stale reports
cannot make a failed run look successful.

## GitHub Actions demo

1. Push this branch, then open **Actions → Code Quality and Metrics**.
2. Open the newest run and expand **Analyze source, run tests and build quality dashboard**.
3. Show a static warning with its rule ID, message, file and line.
4. Open **Summary** to explain the gate, errors, warnings, suggestions and coverage.
5. Download **quality-evidence**, extract it, and open its `dashboard.html`.
   SARIF files, TRX test results, Cobertura XML, and console logs are included.
6. For another live execution, use **Re-run all jobs** on an existing run.
   GitHub's manual workflow button requires the workflow on the default branch.

## Reading the metrics

- Static errors prevent compilation or violate error-level analyzer rules.
- Static warnings/suggestions identify possible maintainability, reliability,
  security, design or performance problems. Inspect the actual rule, not just
  the count. A warning is not automatically a confirmed bug.
- Line coverage is the covered executable lines divided by measured lines.
- Branch coverage is the covered decision branches divided by measured branches.
- Coverage describes the assemblies Coverlet includes, not every file in the
  repository. Generated code and untested infrastructure can affect the denominator.
- Package vulnerability notices are visible in `build.log` and are separate from
  source-analyzer counts. This contribution does not automatically update packages.

## Gate policy and limitations

The current gate requires a successful analyzer build, zero static errors,
successful tests with at least one test result, and generated analysis/coverage
reports. Warnings and suggestions are advisory; no minimum coverage percentage
is enforced. Passing the gate does not mean all warnings have been fixed or all
code is covered. Teams can tighten this policy after agreeing a baseline.

This dashboard does not calculate SonarQube code-smell ratings, estimated
technical debt, or claim SonarQube integration. Those require a separate SonarQube
setup. The assessment allows appropriate static-analysis tools and metrics.

## Explain it in the viva

> Static analysis examines source code without executing the application.
> Unit tests execute behaviour. Our quality pipeline combines analyzer findings
> with test coverage, reports both, and fails on errors, failed tests or missing
> evidence. A high coverage number alone does not prove the tests catch defects.

Analyzer documentation:
https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview
