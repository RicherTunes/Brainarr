using System.Net;
using System.Text;
using NzbDrone.Common.Http;

// Self-contained test helper for building HttpResponse objects in unit tests.
//
// NOTE: a previous refactor turned this into a forwarding shim delegating to
// Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory, but that canonical type
// never landed in Common's pinned main (the lift stalled on a feature branch),
// which broke compilation of all consumers. Until the shared factory is merged
// into Common and the submodule re-pinned, keep this implementation local so the
// 21-LOC explicit-request form continues to compile. Consumers keep importing
// `Brainarr.TestKit.Providers.Http`.

namespace Brainarr.TestKit.Providers.Http;

/// <summary>
/// Factory for creating <see cref="HttpResponse"/> objects in plugin unit tests
/// (explicit-request form).
/// </summary>
public static class HttpResponseFactory
{
    public static HttpResponse Ok(HttpRequest req, string json)
        => new(req, new HttpHeader(), Encoding.UTF8.GetBytes(json ?? string.Empty), HttpStatusCode.OK);

    public static HttpResponse Ok(HttpRequest req, byte[] bodyBytes)
        => new(req, new HttpHeader(), bodyBytes, HttpStatusCode.OK);

    public static HttpResponse Error(HttpRequest req, HttpStatusCode status, string body = "")
        => new(req, new HttpHeader(), Encoding.UTF8.GetBytes(body ?? string.Empty), status);

    public static HttpResponse Error(HttpRequest req, HttpStatusCode status, byte[] bodyBytes)
        => new(req, new HttpHeader(), bodyBytes, status);
}
