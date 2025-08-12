using System;
using System.Net;
using System.Reflection;
using NzbDrone.Common.Http;

namespace Brainarr.Tests.Helpers
{
    public static class HttpResponseFactory
    {
        public static HttpResponse CreateResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            // HttpResponse might need special construction. Let's use reflection to find its constructor
            var responseType = typeof(HttpResponse);
            var constructors = responseType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            HttpResponse response = null;
            
            // Try to find a constructor we can use
            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                
                if (parameters.Length == 0)
                {
                    // Parameterless constructor
                    response = (HttpResponse)constructor.Invoke(new object[0]);
                    break;
                }
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(HttpRequest))
                {
                    // Constructor that takes HttpRequest
                    var request = new HttpRequest("http://test.com");
                    response = (HttpResponse)constructor.Invoke(new object[] { request });
                    break;
                }
            }
            
            if (response == null)
            {
                // If we still don't have a response, try using FormatterServices (deprecated but works)
                response = (HttpResponse)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(responseType);
            }
            
            // Use reflection to set properties
            var statusCodeProperty = responseType.GetProperty("StatusCode");
            if (statusCodeProperty != null && statusCodeProperty.CanWrite)
            {
                statusCodeProperty.SetValue(response, statusCode);
            }
            else
            {
                // Try backing field if property is readonly
                var statusCodeField = responseType.GetField("_statusCode", BindingFlags.NonPublic | BindingFlags.Instance) ??
                                      responseType.GetField("<StatusCode>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                statusCodeField?.SetValue(response, statusCode);
            }
            
            var contentProperty = responseType.GetProperty("Content");
            if (contentProperty != null && contentProperty.CanWrite)
            {
                contentProperty.SetValue(response, content);
            }
            else
            {
                // Try backing field if property is readonly
                var contentField = responseType.GetField("_content", BindingFlags.NonPublic | BindingFlags.Instance) ??
                                  responseType.GetField("<Content>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                contentField?.SetValue(response, content);
            }
            
            return response;
        }
    }
}