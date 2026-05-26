using System.Net;
using NzbDrone.Common.Http;

// Forwarding shim — delegates all calls to the shared TestKit factory.
// Consumers keep their existing `using Brainarr.TestKit.Providers.Http;` import.
// See Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory for the canonical source.

namespace Brainarr.TestKit.Providers.Http;

/// <summary>
/// Passthrough shim for <see cref="Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory"/>.
/// New code should import <c>Lidarr.Plugin.Common.TestKit.Http</c> directly.
/// </summary>
public static class HttpResponseFactory
{
    public static HttpResponse Ok(HttpRequest req, string json)
        => Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Ok(req, json);

    public static HttpResponse Ok(HttpRequest req, byte[] bodyBytes)
        => Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Ok(req, bodyBytes);

    public static HttpResponse Error(HttpRequest req, HttpStatusCode status, string body = "")
        => Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Error(req, status, body);

    public static HttpResponse Error(HttpRequest req, HttpStatusCode status, byte[] bodyBytes)
        => Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Error(req, status, bodyBytes);
}
