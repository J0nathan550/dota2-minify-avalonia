using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

/// <summary>Content of a <see cref="Views.DetailsWindow"/>: a title and an optional full-size preview.</summary>
public interface IDetailsViewModel
{
    string Title { get; }
    bool IsLightboxOpen { get; set; }
    Localizer Loc { get; }
}
