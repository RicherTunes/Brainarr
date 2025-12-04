using System.Net;
using System.Text;
using NzbDrone.Common.Http;

namespace Brainarr.Tests.Helpers
{
    /// <summary>
    /// Factory for creating mock-friendly HttpResponse objects in tests.
    /// Uses actual HttpResponse constructors for proper initialization.
    /// </summary>
    public static class HttpResponseFactory
    {
        /// <summary>
        /// Creates an HttpResponse with the given content and status code.
        /// </summary>
        /// <param name="content">Response body content (string)</param>
        /// <param name="statusCode">HTTP status code (defaults to OK/200)</param>
        /// <returns>Properly initialized HttpResponse</returns>
        public static HttpResponse CreateResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var request = new HttpRequest("http://test.local");
            return new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(content ?? string.Empty), statusCode);
        }

        /// <summary>
        /// Creates an HttpResponse with a specific request context.
        /// </summary>
        public static HttpResponse CreateResponse(HttpRequest request, string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(content ?? string.Empty), statusCode);
        }

        /// <summary>
        /// Creates an OK (200) HttpResponse with the given content.
        /// </summary>
        public static HttpResponse Ok(string content)
            => CreateResponse(content, HttpStatusCode.OK);

        /// <summary>
        /// Creates an error HttpResponse with the given status code.
        /// </summary>
        public static HttpResponse Error(HttpStatusCode statusCode, string content = "")
            => CreateResponse(content, statusCode);
    }
}
