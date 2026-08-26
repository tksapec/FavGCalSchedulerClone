using Xunit;

// CalendarRepository tests clear the process-wide SQLite connection pools during
// cleanup. Running those independent database fixtures concurrently can close a
// pool that another fixture is still using, which makes the suite nondeterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
