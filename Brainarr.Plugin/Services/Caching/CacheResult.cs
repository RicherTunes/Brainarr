using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    public class CacheResult<T>
    {
        public bool Found { get; set; }
        public T Value { get; set; }
        public CacheLevel? Level { get; set; }
        public Exception Error { get; set; }

        public static CacheResult<T> Hit(T value, CacheLevel level)
        {
            return new CacheResult<T> { Found = true, Value = value, Level = level };
        }

        public static CacheResult<T> Miss()
        {
            return new CacheResult<T> { Found = false };
        }

        public static CacheResult<T> Failure(Exception ex)
        {
            return new CacheResult<T> { Found = false, Error = ex };
        }
    }
}
