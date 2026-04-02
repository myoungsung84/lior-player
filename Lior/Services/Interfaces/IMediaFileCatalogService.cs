namespace Lior.Services.Interfaces;

public interface IMediaFileCatalogService
{
    IReadOnlyList<string> GetMediaFilesInFolder(string filePath);
}
