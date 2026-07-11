## Task 1b: Preview snapshot field diffs

- `PreviewAsync` now maps `SyncPlanItem.RemoteEvent` for planned local pushes and only performs a preview GET for unplanned pushes.
- Added a regression test that rejects GET calls while requiring the planned remote title to appear in the push field diff.
- Verification: `dotnet test .\FavGCalSchedulerClone.Tests\FavGCalSchedulerClone.Tests.csproj --filter "FullyQualifiedName~GoogleCalendarSyncServiceTests"` — 62 passed.
