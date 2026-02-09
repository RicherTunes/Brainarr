using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Sections;

/// <summary>
/// A self-contained section of the rendered prompt. Sections are composed by
/// <see cref="LibraryPromptRenderer"/> in <see cref="Order"/> sequence.
/// New sections can be added without editing the core <c>Render()</c> method.
/// </summary>
internal interface IPromptSection
{
    /// <summary>
    /// Controls rendering order. Lower values render first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Returns <c>true</c> when this section has content worth rendering for the given plan/profile.
    /// </summary>
    bool CanBuild(PromptPlan plan, LibraryProfile profile);

    /// <summary>
    /// Builds the section text including its heading. The caller appends a trailing blank line.
    /// </summary>
    string Build(PromptPlan plan, LibraryProfile profile, bool minimalFormatting);
}
