param(
    [Parameter(Mandatory = $true)] [string] $BaselinePath,
    [Parameter(Mandatory = $true)] [string] $CandidatePath
)

$ErrorActionPreference = 'Stop'
$baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
$candidate = Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json
$pairFields = @('Scenario', 'Seed', 'EntitiesPerGroup', 'ChunkCapacity', 'WarmupFrames', 'Runtime', 'Os',
    'Architecture', 'Cpu', 'LogicalProcessors', 'ServerGc', 'Frequency', 'InitialChecksum', 'AfterWarmupChecksum')

foreach ($field in $pairFields) {
    if ($baseline.$field -cne $candidate.$field) { throw "Runs differ in $field." }
}
if ($baseline.Samples.Count -eq 0 -or $baseline.Samples.Count -ne $candidate.Samples.Count) {
    throw 'Runs have a different or absent set of measured frames.'
}

for ($i = 0; $i -lt $baseline.Samples.Count; $i++) {
    $a = $baseline.Samples[$i]
    $b = $candidate.Samples[$i]
    if ($a.Phase -cne $b.Phase -or $a.Checksum -cne $b.Checksum -or $a.Memory.Entities -ne $b.Memory.Entities) {
        throw "Runs diverge at frame $($baseline.WarmupFrames + $i)."
    }
}

function Get-MemorySummary($run) {
    $last = $run.Samples[-1].Memory
    $warm = $run.AfterWarmup
    $memory = $run.Samples.Memory
    [pscustomobject] @{
        BuildId = $run.BuildId
        CoreAssemblySha256 = $run.CoreAssemblySha256
        TargetRetainedBytes = $run.TargetRetainedBytes
        PeakNativeBytes = ($memory.NativeBytes | Measure-Object -Maximum).Maximum
        PeakTrackedRetainedBytes = ($memory.RetainedBytes | Where-Object { $null -ne $_ } | Measure-Object -Maximum).Maximum
        PeakOverBudgetBytes = ($memory.OverBudgetBytes | Where-Object { $null -ne $_ } | Measure-Object -Maximum).Maximum
        PhysicalAllocationsThroughWarmup = $warm.PhysicalAllocations
        PhysicalAllocations = $last.PhysicalAllocations - $warm.PhysicalAllocations
        Reuses = $last.Reuses - $warm.Reuses
        Releases = $last.Releases - $warm.Releases
        ManagedFrameBytes = ($run.Samples.ManagedBytes | Measure-Object -Sum).Sum
    }
}

$phaseRatios = foreach ($a in $baseline.Phases) {
    $b = $candidate.Phases | Where-Object Phase -CEQ $a.Phase
    [pscustomobject] @{
        Phase = $a.Phase
        P50Ratio = $b.P50Ticks / $a.P50Ticks
        P99Ratio = $b.P99Ticks / $a.P99Ticks
    }
}

[pscustomobject] @{
    Scenario = $baseline.Scenario
    VerifiedFrames = $baseline.Samples.Count
    P50Ratio = $candidate.P50Ticks / $baseline.P50Ticks
    P95Ratio = $candidate.P95Ticks / $baseline.P95Ticks
    P99Ratio = $candidate.P99Ticks / $baseline.P99Ticks
    CandidateTrimP99Microseconds = $candidate.TrimP99Ticks * 1e6 / $candidate.Frequency
    Phases = @($phaseRatios)
    Baseline = Get-MemorySummary $baseline
    Candidate = Get-MemorySummary $candidate
}
