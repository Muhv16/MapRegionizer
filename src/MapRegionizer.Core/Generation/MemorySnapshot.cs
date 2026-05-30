namespace MapRegionizer.Core.Generation;

public readonly struct MemorySnapshot
{
    public long ManagedBytes { get; }
    public long WorkingSetBytes { get; }

    private MemorySnapshot(long managedBytes, long workingSetBytes)
    {
        ManagedBytes = managedBytes;
        WorkingSetBytes = workingSetBytes;
    }

    public static MemorySnapshot Capture()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return new MemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            Environment.WorkingSet);
    }

    public MemoryDelta DeltaFrom(MemorySnapshot previous)
    {
        return new MemoryDelta(
            ManagedBytes - previous.ManagedBytes,
            WorkingSetBytes - previous.WorkingSetBytes);
    }
}

public readonly struct MemoryDelta
{
    public long ManagedDelta { get; }
    public long WorkingSetDelta { get; }

    public MemoryDelta(long managedDelta, long workingSetDelta)
    {
        ManagedDelta = managedDelta;
        WorkingSetDelta = workingSetDelta;
    }

    public string Format(long totalManaged, long totalWorkingSet)
    {
        static string Sig(long bytes) => bytes switch
        {
            >= 0 => $"+{bytes / (1024.0 * 1024.0):F1}M",
            < 0 => $"{bytes / (1024.0 * 1024.0):F1}M"
        };

        static string Total(long bytes) => $"{bytes / (1024.0 * 1024.0):F1}M";

        return $"{Sig(ManagedDelta),10} managed, {Sig(WorkingSetDelta),9} WS  | total: {Total(totalManaged),7} / {Total(totalWorkingSet),7}";
    }
}
