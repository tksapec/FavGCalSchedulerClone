# Task 2: UTC migration repository regressions

- Added `InitializeAsync_MigratesMixedOffsetLegacyRowsAndUsesUtcTicksForDirtyAndSeriesOrdering` to prove migrated legacy rows sort by UTC ticks rather than lexical timestamp text in both dirty and recurrence-series loaders.
- Added `MarkSyncedMethods_WriteMatchingLegacyTextAndUtcTicks` to verify `MarkSyncedAsync` and `MarkSyncedByIdsAsync` persist parseable round-trip timestamp text and matching UTC ticks; the bulk path also shares one timestamp across its updated IDs.
- Mutation checks: changing dirty/series ordering to legacy TEXT failed the ordering test; nulling sync ticks failed the timestamp persistence test.
- Verification: `dotnet test FavGCalSchedulerClone.Tests\\FavGCalSchedulerClone.Tests.csproj --filter FullyQualifiedName~CalendarRepositoryTests --no-restore` passed (24/24).

