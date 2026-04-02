namespace Lior.Services.Interfaces;

public interface IFileDialogService
{
    string? OpenMediaFile();

    IReadOnlyList<string> OpenMediaFiles();
}
