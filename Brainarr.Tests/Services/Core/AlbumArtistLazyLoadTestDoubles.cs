using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Music;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Shared counter for <see cref="RecordingArtistLazyLoaded"/> instances participating in a
    /// single test, so assertions can check the total number of would-be per-row DB round trips.
    /// </summary>
    internal sealed class LazyLoadCounter
    {
        public int Count;
    }

    /// <summary>
    /// Test double standing in for the real (internal to NzbDrone.Core) <c>LazyLoaded&lt;Artist,Artist&gt;</c>
    /// that Lidarr's <c>AlbumRepository</c> wires onto every <see cref="Album.Artist"/> returned by
    /// <c>IAlbumService.GetAllAlbums()</c> (a plain <c>SELECT * FROM Albums</c> with no Artist join).
    /// <para>
    /// Production behavior: the FIRST access to <see cref="Album.ArtistId"/> (which is
    /// <c>Artist?.Value?.Id ?? 0</c> — see NzbDrone.Core.Music.Model.Album) fires a full,
    /// per-row <c>ArtistRepository.Query()</c> database round trip. Against an ~11,700-artist
    /// library, touching <c>Album.ArtistId</c> once per album in a loop is an N+1 query storm
    /// that OOMs the process (live-observed: 18 <c>OutOfMemoryException</c>s/hour).
    /// </para>
    /// <para>
    /// This double records every such would-be round trip via a shared <see cref="LazyLoadCounter"/>
    /// so tests can assert it never happens (batch/O(1) lookup code paths), and can optionally
    /// throw to simulate a round trip that *fails* — representative of the live
    /// "Error parsing column 21 (Links=[...])" Dapper column-mapping fault also observed on this
    /// exact per-row query path.
    /// </para>
    /// </summary>
    internal sealed class RecordingArtistLazyLoaded : LazyLoaded<Artist>
    {
        private readonly Artist _artist;
        private readonly LazyLoadCounter _counter;
        private readonly Func<Exception> _failureFactory;

        public RecordingArtistLazyLoaded(Artist artist, LazyLoadCounter counter, Func<Exception> failureFactory = null)
        {
            _artist = artist;
            _counter = counter;
            _failureFactory = failureFactory;
        }

        public override void LazyLoad()
        {
            if (IsLoaded)
            {
                return;
            }

            _counter.Count++;

            var failure = _failureFactory?.Invoke();
            if (failure != null)
            {
                // Do NOT set IsLoaded — a real failed DB round trip doesn't memoize a value either,
                // so a retry would fail again exactly like the real ArtistRepository.Query() would.
                throw failure;
            }

            _value = _artist;
            IsLoaded = true;
        }
    }
}
