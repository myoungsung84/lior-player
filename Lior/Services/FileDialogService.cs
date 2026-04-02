using Lior.Services.Interfaces;
using Microsoft.Win32;

namespace Lior.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? OpenMediaFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open media file",
            Filter = "Media files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.mp3;*.wav;*.flac|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> OpenMediaFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open media files",
            Filter = "Media files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.mp3;*.wav;*.flac|All files|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }
}
