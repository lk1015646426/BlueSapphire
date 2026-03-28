using System;
using System.Linq;
using System.Threading.Tasks;
using BlueSapphire.Helpers;
using Windows.Storage;

namespace BlueSapphire.Services
{
    public sealed record AudioMetadataInfo(
        TimeSpan Duration,
        string? Artist,
        string? Album,
        string? Title,
        uint TrackNumber,
        uint Year,
        uint EncodingBitrate,
        uint SampleRate,
        string? AlbumArtist = null,
        string? Composer = null,
        string? Genre = null,
        uint DiscNumber = 0,
        string? Comment = null,
        string? Lyrics = null,
        bool HasEmbeddedCoverArt = false);

    public class AudioMetadataService
    {
        public async Task<AudioMetadataInfo?> TryReadAsync(StorageFile file)
        {
            if (!MediaFileCatalog.IsAudio(file.Name))
            {
                return null;
            }

            try
            {
                return await Task.Run(() => ReadWithTagLib(file.Path));
            }
            catch
            {
                return await TryReadWithWindowsPropertiesAsync(file);
            }
        }

        private static AudioMetadataInfo ReadWithTagLib(string filePath)
        {
            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;
            var properties = tagFile.Properties;

            return new AudioMetadataInfo(
                properties.Duration,
                NormalizeText(tag.JoinedPerformers),
                NormalizeText(tag.Album),
                NormalizeText(tag.Title),
                tag.Track,
                tag.Year,
                ToUnsignedInt(properties.AudioBitrate * 1000),
                ToUnsignedInt(properties.AudioSampleRate),
                NormalizeText(tag.JoinedAlbumArtists),
                NormalizeText(tag.JoinedComposers),
                NormalizeText(tag.JoinedGenres),
                tag.Disc,
                NormalizeText(tag.Comment),
                NormalizeText(tag.Lyrics),
                HasEmbeddedCoverArt(tag));
        }

        private static async Task<AudioMetadataInfo?> TryReadWithWindowsPropertiesAsync(StorageFile file)
        {
            try
            {
                var musicProperties = await file.Properties.GetMusicPropertiesAsync();

                return new AudioMetadataInfo(
                    musicProperties.Duration,
                    NormalizeText(musicProperties.Artist),
                    NormalizeText(musicProperties.Album),
                    NormalizeText(musicProperties.Title),
                    musicProperties.TrackNumber,
                    musicProperties.Year,
                    0,
                    0);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasEmbeddedCoverArt(TagLib.Tag tag)
        {
            return tag.Pictures?.Any(picture => picture?.Data != null && picture.Data.Count > 0) == true;
        }

        private static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static uint ToUnsignedInt(int value)
        {
            return value > 0 ? (uint)value : 0;
        }
    }
}
