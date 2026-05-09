using LibNavegation;
using MaterialDesignThemes.Wpf;

namespace Albion_App.Models;

public interface ISectionIcons : ISection
{
    PackIconKind Icon { get; }

}