using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IBrainarrActionHandler
    {
        object HandleAction(string action, IDictionary<string, string> query);
        object GetModelOptions(string provider);
        object GetFallbackModelOptions(string provider);
    }
}