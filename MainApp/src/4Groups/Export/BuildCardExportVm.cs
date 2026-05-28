using System.Windows.Media.Imaging;

namespace Albion_App._4Groups.Export;

/// <summary>
/// Data for one build card in the exported image.
/// All BitmapImage properties are pre-loaded before rendering; null = empty slot.
/// </summary>
public sealed class BuildCardExportVm
{
    public string Label { get; init; } = "";

    // Grid order matches the editor layout:
    // Col0=Bag/MainHand/Potion/—  Col1=Head/Chest/Boots/Mount  Col2=Cape/OffHand/Food/—
    public BitmapImage? Bag      { get; init; }
    public BitmapImage? Head     { get; init; }
    public BitmapImage? Cape     { get; init; }
    public BitmapImage? MainHand { get; init; }
    public BitmapImage? Chest    { get; init; }
    public BitmapImage? OffHand  { get; init; }
    public BitmapImage? Potion   { get; init; }
    public BitmapImage? Boots    { get; init; }
    public BitmapImage? Food     { get; init; }
    public BitmapImage? Mount    { get; init; }

    /// <summary>Only non-null extras — section hidden when empty.</summary>
    public IReadOnlyList<BitmapImage> Extras { get; init; } = [];

    public bool HasExtras => Extras.Count > 0;
}
