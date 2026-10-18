# Sales and Reporting: unit testing and mocking

## Additional business-rule tests

`CatalogueHttpBoundaryTests.cs` adds 8 cases with a mocked HTTP handler for error statuses, JSON null, and mapping identity/pagination from successful responses. No Catalogue API runs. `ReceiptConfigurationTests.cs` adds 4 cases for invalid tax settings and missing pharmacy names, using mocked options and an unconfigured DbContext to prove validation happens before database access. Existing SQLite-backed tests remain database-backed service tests; the new tests listed here are isolated unit tests.

## Run locally

Run from this repository:

```powershell
dotnet test tests/Medzo.SalesReporting.Tests/Medzo.SalesReporting.Tests.csproj --collect:"XPlat Code Coverage"
```

Latest local validation: **47 passing cases**, including the pre-existing tests. Each parameterized input counts as one case.

## Live demonstration

Push to `feature/se3112-unit-mocking`, open this repository's **Actions** tab, and open the newest unit-test workflow run. Expand the test step for console output, then show the **Summary** table for total/passed/failed/skipped counts.

The workflow uploads TRX test results and Coverlet Cobertura XML in its test-evidence artifact. Coverage measures executed lines/branches within the assemblies included by the collector; it is not the same as the percentage of passing tests. Compare reports only when their included assemblies and filters match.

