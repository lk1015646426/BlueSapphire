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

        public static IReadOnlyCollection<string> AllSupportedExtensions => ImageExtensionSet;

        public static IReadOnlyCollection<string> ImageExtensions => ImageExtensionSet;

        public static QueryOptions CreateAllMediaQueryOptions()
        {
            return CreateImageQueryOptions();
        }

        public static QueryOptions CreateImageQueryOptions()
        {
            return new QueryOptions(CommonFileQuery.DefaultQuery, ImageExtensionSet)
            {
                FolderDepth = FolderDepth.Deep
            };
        }

        public static bool IsImage(string? fileName) => HasExtension(fileName, ImageExtensionSet);

        public static bool IsSupported(string? fileName) => IsImage(fileName);

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
    }
}
