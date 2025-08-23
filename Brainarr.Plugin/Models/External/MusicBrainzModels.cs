using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.External
{
    /// <summary>
    /// MusicBrainz API response format models
    /// </summary>
    public class MusicBrainzResponse
    {
        [JsonPropertyName("created")]
        public string Created { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("artists")]
        public List<MusicBrainzArtist> Artists { get; set; }

        [JsonPropertyName("recordings")]
        public List<MusicBrainzRecording> Recordings { get; set; }

        [JsonPropertyName("releases")]
        public List<MusicBrainzRelease> Releases { get; set; }
    }

    public class MusicBrainzArtist
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("sort-name")]
        public string SortName { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; }

        [JsonPropertyName("area")]
        public MusicBrainzArea Area { get; set; }

        [JsonPropertyName("begin-area")]
        public MusicBrainzArea BeginArea { get; set; }

        [JsonPropertyName("disambiguation")]
        public string Disambiguation { get; set; }

        [JsonPropertyName("life-span")]
        public MusicBrainzLifeSpan LifeSpan { get; set; }
    }

    public class MusicBrainzArea
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("sort-name")]
        public string SortName { get; set; }
    }

    public class MusicBrainzLifeSpan
    {
        [JsonPropertyName("begin")]
        public string Begin { get; set; }

        [JsonPropertyName("end")]
        public string End { get; set; }

        [JsonPropertyName("ended")]
        public bool Ended { get; set; }
    }

    public class MusicBrainzRecording
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("length")]
        public int? Length { get; set; }

        [JsonPropertyName("video")]
        public bool Video { get; set; }

        [JsonPropertyName("artist-credit")]
        public List<MusicBrainzArtistCredit> ArtistCredit { get; set; }

        [JsonPropertyName("releases")]
        public List<MusicBrainzRelease> Releases { get; set; }
    }

    public class MusicBrainzArtistCredit
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("artist")]
        public MusicBrainzArtist Artist { get; set; }
    }

    public class MusicBrainzRelease
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("score")]
        public int? Score { get; set; }

        [JsonPropertyName("status-id")]
        public string StatusId { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("artist-credit")]
        public List<MusicBrainzArtistCredit> ArtistCredit { get; set; }

        [JsonPropertyName("release-group")]
        public MusicBrainzReleaseGroup ReleaseGroup { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; }

        [JsonPropertyName("release-events")]
        public List<MusicBrainzReleaseEvent> ReleaseEvents { get; set; }

        [JsonPropertyName("barcode")]
        public string Barcode { get; set; }

        [JsonPropertyName("track-count")]
        public int TrackCount { get; set; }

        [JsonPropertyName("media")]
        public List<MusicBrainzMedia> Media { get; set; }
    }

    public class MusicBrainzReleaseGroup
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type-id")]
        public string TypeId { get; set; }

        [JsonPropertyName("primary-type-id")]
        public string PrimaryTypeId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("primary-type")]
        public string PrimaryType { get; set; }
    }

    public class MusicBrainzReleaseEvent
    {
        [JsonPropertyName("date")]
        public string Date { get; set; }

        [JsonPropertyName("area")]
        public MusicBrainzArea Area { get; set; }
    }

    public class MusicBrainzMedia
    {
        [JsonPropertyName("position")]
        public int Position { get; set; }

        [JsonPropertyName("format-id")]
        public string FormatId { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("track-count")]
        public int TrackCount { get; set; }

        [JsonPropertyName("track-offset")]
        public int TrackOffset { get; set; }
    }
}