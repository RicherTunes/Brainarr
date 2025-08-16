using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace NzbDrone.Common.Http
{
    public interface IHttpClient
    {
        HttpResponse Execute(HttpRequest request);
        Task<HttpResponse> ExecuteAsync(HttpRequest request);
        HttpResponse Get(HttpRequest request);
        HttpResponse Head(HttpRequest request);
        HttpResponse Post(HttpRequest request);
        HttpResponse<T> Get<T>(HttpRequest request) where T : new();
        HttpResponse<T> Post<T>(HttpRequest request) where T : new();
        Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new();
        Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new();
        void DownloadFile(string url, string fileName);
        Task DownloadFileAsync(string url, string fileName);
    }

    public class HttpRequest
    {
        public HttpMethod Method { get; set; }
        public HttpUri Url { get; set; }
        public HttpHeader Headers { get; set; }
        public byte[] ContentData { get; set; }
        public string ContentSummary { get; set; }
        public bool LogResponseContent { get; set; }
        public bool AllowAutoRedirect { get; set; }
        public bool ConnectionKeepAlive { get; set; }
        public TimeSpan RequestTimeout { get; set; }
        public ICredentials Credentials { get; set; }
        public Action<Stream> ContentCallback { get; set; }
        public Action<HttpWebRequest> RequestCallback { get; set; }
        public bool IgnoreNotFound { get; set; }
        public bool SuppressHttpError { get; set; }
        public bool UseSimplifiedUserAgent { get; set; }
        public CookieContainer Cookies { get; set; }
        public bool StoreRequestCookie { get; set; }
        public bool StoreResponseCookie { get; set; }

        public HttpRequest(string url, HttpAccept httpAccept = null)
        {
            Method = HttpMethod.Get;
            Url = new HttpUri(url);
            Headers = new HttpHeader();
            AllowAutoRedirect = true;
            Cookies = new CookieContainer();

            if (httpAccept != null)
            {
                Headers.Accept = httpAccept.Value;
            }
        }

        public void SetContent(byte[] data)
        {
            ContentData = data;
        }

        public void SetContent(string data)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            SetContent(bytes);
        }

        public override string ToString()
        {
            return $"[{Method}] {Url}";
        }
    }

    public class HttpResponse
    {
        public int StatusCode { get; set; }
        public HttpHeader Headers { get; set; }
        public byte[] ResponseData { get; set; }
        public string Content { get; set; }
        public HttpRequest Request { get; set; }
        public bool HasHttpError { get; set; }
        public bool HasHttpRedirect { get; set; }
        public List<HttpHeader> RedirectHeaders { get; set; }
        public CookieContainer Cookies { get; set; }

        public HttpResponse(HttpRequest request)
        {
            Request = request;
            Headers = new HttpHeader();
            RedirectHeaders = new List<HttpHeader>();
        }

        public override string ToString()
        {
            return $"[{StatusCode}] {Request?.Url}";
        }
    }

    public class HttpResponse<T> : HttpResponse
    {
        public T Resource { get; set; }

        public HttpResponse(HttpRequest request) : base(request)
        {
        }
    }

    public class HttpHeader : Dictionary<string, string>
    {
        public string Accept
        {
            get => GetValueOrDefault("Accept");
            set => this["Accept"] = value;
        }

        public string AcceptEncoding
        {
            get => GetValueOrDefault("Accept-Encoding");
            set => this["Accept-Encoding"] = value;
        }

        public string Authorization
        {
            get => GetValueOrDefault("Authorization");
            set => this["Authorization"] = value;
        }

        public string CacheControl
        {
            get => GetValueOrDefault("Cache-Control");
            set => this["Cache-Control"] = value;
        }

        public string ContentType
        {
            get => GetValueOrDefault("Content-Type");
            set => this["Content-Type"] = value;
        }

        public string UserAgent
        {
            get => GetValueOrDefault("User-Agent");
            set => this["User-Agent"] = value;
        }

        public string GetValueOrDefault(string key)
        {
            return TryGetValue(key, out var value) ? value : null;
        }
    }

    public class HttpUri
    {
        public string FullUri { get; set; }
        public string Scheme { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Path { get; set; }
        public string Query { get; set; }
        public string Fragment { get; set; }

        public HttpUri(string uri)
        {
            FullUri = uri;
            var parsed = new Uri(uri);
            Scheme = parsed.Scheme;
            Host = parsed.Host;
            Port = parsed.Port;
            Path = parsed.AbsolutePath;
            Query = parsed.Query;
            Fragment = parsed.Fragment;
        }

        public override string ToString()
        {
            return FullUri;
        }

        public static implicit operator string(HttpUri uri)
        {
            return uri?.ToString();
        }

        public static implicit operator HttpUri(string uri)
        {
            return new HttpUri(uri);
        }
    }

    public class HttpAccept
    {
        public string Value { get; }

        private HttpAccept(string value)
        {
            Value = value;
        }

        public static HttpAccept Json = new HttpAccept("application/json");
        public static HttpAccept Xml = new HttpAccept("application/xml");
        public static HttpAccept Rss = new HttpAccept("application/rss+xml");
        public static HttpAccept Html = new HttpAccept("text/html");
    }

    public enum HttpMethod
    {
        Get,
        Post,
        Put,
        Delete,
        Head,
        Options
    }

    public class HttpException : Exception
    {
        public HttpResponse Response { get; }

        public HttpException(HttpResponse response) : base($"HTTP Error - {response.StatusCode}")
        {
            Response = response;
        }

        public HttpException(HttpResponse response, string message) : base(message)
        {
            Response = response;
        }

        public HttpException(HttpResponse response, string message, Exception innerException) : base(message, innerException)
        {
            Response = response;
        }
    }

    public class HttpRequestException : Exception
    {
        public HttpRequest Request { get; }

        public HttpRequestException(HttpRequest request, string message) : base(message)
        {
            Request = request;
        }

        public HttpRequestException(HttpRequest request, string message, Exception innerException) : base(message, innerException)
        {
            Request = request;
        }
    }

    public static class HttpRequestBuilderExtensions
    {
        public static HttpRequestBuilder CreateRequest(this IHttpClient httpClient, string url)
        {
            return new HttpRequestBuilder(url);
        }

        public static HttpRequestBuilder CreateRequest(this IHttpClient httpClient, HttpUri uri)
        {
            return new HttpRequestBuilder(uri.FullUri);
        }
    }

    public class HttpRequestBuilder
    {
        private readonly HttpRequest _request;

        public HttpRequestBuilder(string url)
        {
            _request = new HttpRequest(url);
        }

        public HttpRequestBuilder Accept(HttpAccept accept)
        {
            _request.Headers.Accept = accept.Value;
            return this;
        }

        public HttpRequestBuilder SetHeader(string name, string value)
        {
            _request.Headers[name] = value;
            return this;
        }

        public HttpRequestBuilder Post()
        {
            _request.Method = HttpMethod.Post;
            return this;
        }

        public HttpRequestBuilder Put()
        {
            _request.Method = HttpMethod.Put;
            return this;
        }

        public HttpRequestBuilder Delete()
        {
            _request.Method = HttpMethod.Delete;
            return this;
        }

        public HttpRequestBuilder AddQueryParam(string key, object value)
        {
            // Simple implementation
            var separator = _request.Url.FullUri.Contains("?") ? "&" : "?";
            _request.Url = new HttpUri(_request.Url.FullUri + $"{separator}{key}={value}");
            return this;
        }

        public HttpRequest Build()
        {
            return _request;
        }

        public static implicit operator HttpRequest(HttpRequestBuilder builder)
        {
            return builder.Build();
        }
    }
}