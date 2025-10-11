using System.Net;
using System.Text;
using NzbDrone.Common.Http;

namespace Brainarr.TestKit.Providers.Http
{
    public static class HttpResponseFactory
    {
        public static HttpResponse Ok(HttpRequest req, string json)
            => new(req, new HttpHeader(), Encoding.UTF8.GetBytes(json), HttpStatusCode.OK);

        public static HttpResponse Ok(HttpRequest req, byte[] bodyBytes)
            => new(req, new HttpHeader(), bodyBytes, HttpStatusCode.OK);

        public static HttpResponse Error(HttpRequest req, HttpStatusCode status, string body = "")
            => new(req, new HttpHeader(), Encoding.UTF8.GetBytes(body), status);

        public static HttpResponse Error(HttpRequest req, HttpStatusCode status, byte[] bodyBytes)
            => new(req, new HttpHeader(), bodyBytes, status);
    }
}
