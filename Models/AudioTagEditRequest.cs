using System;

namespace BlueSapphire.Models
{
    public sealed record AudioTagEditSeed(
        int ItemCount,
        string PrimaryFileName,
        string? Title,
        string? Artist,
        string? Album,
        uint? TrackNumber,
        uint? Year,
        string? AlbumArtist = null,
        string? Composer = null,
        string? Genre = null,
        uint? DiscNumber = null,
        string? Comment = null,
        string? Lyrics = null,
        bool HasEmbeddedCoverArt = false)
    {
        public bool IsBatch => ItemCount > 1;
    }

    public sealed record AudioTagEditRequest(
        bool ApplyTitle,
        string? Title,
        bool ApplyArtist,
        string? Artist,
        bool ApplyAlbum,
        string? Album,
        bool ApplyTrackNumber,
        uint? TrackNumber,
        bool ApplyYear,
        uint? Year,
        bool ApplyAlbumArtist = false,
        string? AlbumArtist = null,
        bool ApplyComposer = false,
        string? Composer = null,
        bool ApplyGenre = false,
        string? Genre = null,
        bool ApplyDiscNumber = false,
        uint? DiscNumber = null,
        bool ApplyComment = false,
        string? Comment = null,
        bool ApplyLyrics = false,
        string? Lyrics = null)
    {
        public bool HasChanges =>
            ApplyTitle ||
            ApplyArtist ||
            ApplyAlbum ||
            ApplyTrackNumber ||
            ApplyYear ||
            ApplyAlbumArtist ||
            ApplyComposer ||
            ApplyGenre ||
            ApplyDiscNumber ||
            ApplyComment ||
            ApplyLyrics;
    }

    public sealed record AudioTagUpdateResult(string SourcePath, bool Success, string Message, string? OutputPath = null)
    {
        public static AudioTagUpdateResult Succeeded(string sourcePath, string message = "标签更新成功。", string? outputPath = null)
        {
            return new AudioTagUpdateResult(sourcePath, true, message, outputPath);
        }

        public static AudioTagUpdateResult Failed(string sourcePath, string message)
        {
            return new AudioTagUpdateResult(sourcePath, false, message);
        }
    }
}
