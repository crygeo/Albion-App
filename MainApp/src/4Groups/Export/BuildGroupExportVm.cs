namespace Albion_App._4Groups.Export;

public sealed class BuildGroupExportVm
{
    public string GroupName { get; init; } = "";
    public IReadOnlyList<BuildCardExportVm> Cards { get; init; } = [];
}
