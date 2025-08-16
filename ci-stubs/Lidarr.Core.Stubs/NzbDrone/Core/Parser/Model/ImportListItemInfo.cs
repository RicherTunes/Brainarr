using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Parser.Model
{
    public class ImportListItemInfo
    {
        public string Artist { get; set; }
        public string ArtistMusicBrainzId { get; set; }
        public string Album { get; set; }
        public string AlbumMusicBrainzId { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string ImportListId { get; set; }
        public string Title { get; set; }

        public ImportListItemInfo()
        {
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1}", ReleaseDate, Artist);
        }
    }

    public enum ParsedArtistType
    {
        Artist = 0,
        Person = 1,
        Group = 2,
        Orchestra = 3,
        Choir = 4,
        Character = 5,
        Other = 6
    }

    public class ParsedArtistInfo
    {
        public string Name { get; set; }
        public string CleanName { get; set; }
        public string SortName { get; set; }
        public string Disambiguation { get; set; }
        public string Overview { get; set; }
        public ParsedArtistType Type { get; set; }
        public DateTime? BeginDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> Genres { get; set; }
        public List<string> SecondaryTypes { get; set; }
        public List<MemberInfo> Members { get; set; }
        public string MusicBrainzId { get; set; }

        public ParsedArtistInfo()
        {
            Genres = new List<string>();
            SecondaryTypes = new List<string>();
            Members = new List<MemberInfo>();
        }
    }

    public class MemberInfo
    {
        public string Name { get; set; }
        public string Instrument { get; set; }
        public DateTime? JoinDate { get; set; }
        public DateTime? LeaveDate { get; set; }
    }
}