using Xunit.Sdk;
using Xunit.v3;

// The generator's graph-count and prepared-spec cache hooks are process-global.
[assembly: Parallelization(Mode = ParallelMode.None)]
