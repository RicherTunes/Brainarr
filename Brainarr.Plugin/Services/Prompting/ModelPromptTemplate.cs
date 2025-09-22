namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public sealed record ModelPromptTemplate(string Name)
{
    public static ModelPromptTemplate Default { get; } = new("default");
}
