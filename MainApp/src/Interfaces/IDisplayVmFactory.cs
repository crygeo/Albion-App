namespace Albion_App.Interfaces;

public interface IDisplayVmFactory<in TSource, out TVm>
{
    TVm Create(TSource source, CancellationToken ct = default);
}