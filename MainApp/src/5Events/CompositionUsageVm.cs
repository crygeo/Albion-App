namespace Albion_App._5Events;

public sealed class CompositionUsageVm(string name, int usageCount)
{
    public string Name        { get; } = name;
    public int    UsageCount  { get; } = usageCount;
    public string UsageDisplay => UsageCount == 1 ? "usada 1 vez" : $"usada {UsageCount} veces";
}
