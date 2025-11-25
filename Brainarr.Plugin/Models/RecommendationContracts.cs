using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr.Models;

public sealed record ArtistRecommendation(
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("genre")] string Genre,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("adjacency_source")] string AdjacencySource,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record AlbumRecommendation(
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("album")] string Album,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("genre")] string Genre,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("adjacency_source")] string AdjacencySource,
    [property: JsonPropertyName("reason")] string Reason);
