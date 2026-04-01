using Lior.Models;

namespace Lior.Services.Interfaces;

public interface IPlayerService
{
    PlaybackState State { get; }

    string? CurrentMediaPath { get; }

    void SetRenderTarget(nint windowHandle);

    void Shutdown();

    bool Load(string mediaPath);

    bool Play();

    bool Pause();

    bool Stop();
}
