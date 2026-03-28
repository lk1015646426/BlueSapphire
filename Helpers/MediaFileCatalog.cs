using System;
using System.Collections.Generic;
using System.IO;
using Windows.Storage.Search;

namespace BlueSapphire.Helpers
{
    public static class MediaFileCatalog
    {
        private static readonly HashSet<string> ImageExtensionSet = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".heic"
        };

        private static readonly HashSet<string> AudioExtensionSet = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".aac", ".m4a"
        };

        private static readonly HashSet<string> DocumentExtensionSet = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".pdf", ".rtf",
            ".doc", ".docx", ".docm",
            ".xls", ".xlsx", ".xlsm", ".xlsb", ".csv",
            ".ppt", ".pptx", ".pptm"
        };

        public static IReadOnlyCollection<string> AllSupportedExtensions { get; } =
            BuildCombinedSet(ImageExtensionSet, AudioExtensionSet, DocumentExtensionSet);

        public static IReadOnlyCollection<string> ImageExtensions => ImageExtensionSet;

        public static QueryOptions CreateAllMediaQueryOptions()
        {
            return CreateQueryOptions(AllSupportedExtensions);
        }

        public static QueryOptions CreateImageQueryOptions()
        {
            return CreateQueryOptions(ImageExtensions);
        }

        public static bool IsImage(string? fileName) => HasExtension(fileName, ImageExtensionSet);

        public static bool IsAudio(string? fileName) => HasExtension(fileName, AudioExtensionSet);

        public static bool IsDocument(string? fileName) => HasExtension(fileName, DocumentExtensionSet);

        public static bool IsSupported(string? fileName) => HasExtension(fileName, AllSupportedExtensions);

        private static QueryOptions CreateQueryOptions(IEnumerable<string> extensions)
        {
            return new QueryOptions(CommonFileQuery.DefaultQuery, extensions)
            {
                FolderDepth = FolderDepth.Deep
            };
        }

        private static bool HasExtension(string? fileName, IEnumerable<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            foreach (var candidate in extensions)
            {
                if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyCollection<string> BuildCombinedSet(params IEnumerable<string>[] extensionGroups)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in extensionGroups)
            {
                foreach (var extension in group)
                {
                    set.Add(extension);
                }
            }

            return set;
        }
    }
}
