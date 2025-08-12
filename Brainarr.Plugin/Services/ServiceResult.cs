using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class ServiceResult<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string ErrorMessage { get; set; }
        public string ProviderUsed { get; set; }
        public Exception Exception { get; set; }

        public static ServiceResult<T> Ok(T data, string provider = null)
        {
            return new ServiceResult<T>
            {
                Success = true,
                Data = data,
                ProviderUsed = provider
            };
        }

        public static ServiceResult<T> Fail(string error, Exception ex = null)
        {
            return new ServiceResult<T>
            {
                Success = false,
                ErrorMessage = error,
                Exception = ex
            };
        }
    }
}