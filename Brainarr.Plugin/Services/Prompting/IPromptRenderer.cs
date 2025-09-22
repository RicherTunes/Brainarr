using System.Threading;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public interface IPromptRenderer
{
    string Render(PromptPlan plan, ModelPromptTemplate template, CancellationToken cancellationToken);
}
