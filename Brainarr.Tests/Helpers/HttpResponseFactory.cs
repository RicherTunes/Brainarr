using System.Net;
using NzbDrone.Common.Http;

// Forwarding shim — delegates all calls to the shared TestKit factory.
// Consumers keep their existing `using Brainarr.Tests.Helpers;` import.
// See Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory for the canonical source.

namespace Brainarr.Tests.Helpers;

/// <summary>
/// Passthrough shim for <see cref="Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory"/>.
/// New code should import <c>Lidarr.Plugin.Common.TestKit.Http</c> directly.
/// </summary>
public static class HttpResponseFactory
{
    public static HttpResponse CreateResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        => Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.CreateResponse(content ?? "", statusCode);

    public static HttpResponse CreateResponse(HttpRequest request, string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        => Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.CreateResponse(request, content ?? "", statusCode);

    public static HttpResponse Ok(string content)
        => Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Ok(content);

    public static HttpResponse Error(HttpStatusCode statusCode, string content = "")
        => Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Error(statusCode, content);
}
