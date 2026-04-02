using Lior.Services.Interfaces;
using System.IO;

namespace Lior.Services;

public sealed class MediaFileCatalogService : IMediaFileCatalogService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mkv",
        ".avi",
        ".mov",
        ".wmv",
        ".mp3",
        ".flac",
        ".wav",
        ".m4a"
    };

    public IReadOnlyList<string> GetMediaFilesInFolder(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Array.Empty<string>();
        }

        var directoryPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(directoryPath)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
