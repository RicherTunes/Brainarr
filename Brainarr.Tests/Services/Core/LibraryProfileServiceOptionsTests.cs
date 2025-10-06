using System;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public sealed class LibraryProfileServiceOptionsTests
    {
        private sealed class FakeBuilder : ILibraryContextBuilder
        {
            private readonly Logger _logger;
            public FakeBuilder(Logger logger) { _logger = logger; }
            public LibraryProfile BuildProfile(IArtistService artistService, IAlbumService albumService)
            {
                return new LibraryProfile
                {
                    TotalArtists = 1,
                    TotalAlbums = 1
                };
            }
            public string GenerateFingerprint(LibraryProfile profile)
            {
                // Use a very simple stable serialization to avoid pulling other helpers
                return $"artists:{profile?.TotalArtists ?? 0};albums:{profile?.TotalAlbums ?? 0}";
            }
        }

        [Fact]
        public void CachedProfile_Expires_AccordingTo_Ttl()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var options = new LibraryProfileOptions { Ttl = TimeSpan.FromMilliseconds(50) };
            var svc = new LibraryProfileService(new FakeBuilder(logger), logger, artistService: null, albumService: null, options);

            var key = "k1";
            var profile = new LibraryProfile { TotalArtists = 2, TotalAlbums = 3 };

            svc.CacheProfile(key, profile);
            var hit1 = svc.GetCachedProfile(key);
            Assert.NotNull(hit1);

            Thread.Sleep(80);

            var miss = svc.GetCachedProfile(key);
            Assert.Null(miss);
        }

        [Fact]
        public void Fingerprint_Deterministic_BeforeAndAfterExpiry()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var svc = new LibraryProfileService(new FakeBuilder(logger), logger, artistService: null, albumService: null, options: null);
            var profile = new LibraryProfile { TotalArtists = 5, TotalAlbums = 7 };
            var f1 = svc.GenerateLibraryFingerprint(profile);
            Thread.Sleep(10);
            var f2 = svc.GenerateLibraryFingerprint(profile);
            Assert.Equal(f1, f2);
        }
    }
}
