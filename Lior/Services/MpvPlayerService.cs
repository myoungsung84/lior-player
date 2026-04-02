using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Lior.Models;
using Lior.Services.Interfaces;
using Lior.Services.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lior.Options;

namespace Lior.Services;

public sealed class MpvPlayerService : IPlayerService, IDisposable
{
    private const int MpvFormatDouble = 5;
    private const int MpvFormatFlag = 3;
    private readonly ILogger<MpvPlayerService> _logger;
    private nint _handle;
    private nint _renderTarget;
    private bool _disposed;
    private bool _initialized;
    private double _volume;
    private bool _isMuted;
    private bool _playbackEndedRaised;

    public event EventHandler? PlaybackEnded;

    public MpvPlayerService(
        ILogger<MpvPlayerService> logger,
        IOptions<PlayerOptions> playerOptions)
    {
        _logger = logger;
        _volume = Math.Clamp(playerOptions.Value.DefaultVolume, 0, 100);
        EnsureNativeLibraryExists();
        _handle = MpvNative.mpv_create();

        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create mpv handle.");
        }

        SetOptionString("terminal", "no");
        SetOptionString("msg-level", "all=warn");
        SetOptionString("input-default-bindings", "no");
        SetOptionString("input-vo-keyboard", "no");
        SetOptionString("osc", "no");
        SetOptionString("keep-open", "yes");
        SetOptionString("idle", "yes");
        SetOptionString("volume", _volume.ToString(CultureInfo.InvariantCulture));
    }

    public PlaybackState State { get; private set; } = PlaybackState.None;

    public string? CurrentMediaPath { get; private set; }

    public double Volume => _volume;

    public bool IsMuted => _isMuted;

    public void SetRenderTarget(nint windowHandle)
    {
        ThrowIfDisposed();

        if (windowHandle == nint.Zero)
        {
            _logger.LogWarning("mpv render target was not set because the provided window handle is zero.");
            return;
        }

        if (_renderTarget == windowHandle)
        {
            return;
        }

        if (_initialized)
        {
            if (_renderTarget != windowHandle)
            {
                _logger.LogWarning("mpv render target change was ignored because the player is already initialized.");
            }

            return;
        }

        _renderTarget = windowHandle;

        if (!EnsureInitialized())
        {
            _logger.LogError("Failed to initialize mpv with the requested render target {WindowHandle}.", windowHandle);
        }
    }

    public bool Load(string mediaPath)
    {
        ThrowIfDisposed();

        if (!EnsureInitialized())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            _logger.LogWarning("mpv load request ignored because the media path is invalid: {MediaPath}", mediaPath);
            return false;
        }

        var result = ExecuteCommand("loadfile", mediaPath, "replace");
        if (result < 0)
        {
            _logger.LogWarning("mpv loadfile failed with code {Result} for {MediaPath}", result, mediaPath);
            return false;
        }

        var pauseResult = ExecuteCommand("set", "pause", "yes");
        if (pauseResult < 0)
        {
            _logger.LogWarning("mpv pause initialization failed with code {Result} for {MediaPath}", pauseResult, mediaPath);
        }

        CurrentMediaPath = mediaPath;
        State = PlaybackState.Loaded;
        _playbackEndedRaised = false;
        _logger.LogInformation("mpv loaded media: {MediaPath}", mediaPath);
        return true;
    }

    public bool Play()
    {
        ThrowIfDisposed();

        if (!EnsureInitialized())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(CurrentMediaPath))
        {
            return false;
        }

        if (State is PlaybackState.Stopped)
        {
            var mediaPath = CurrentMediaPath;
            if (string.IsNullOrWhiteSpace(mediaPath) || !Load(mediaPath))
            {
                return false;
            }
        }

        var result = ExecuteCommand("set", "pause", "no");
        if (result < 0)
        {
            _logger.LogWarning("mpv play request failed with code {Result}", result);
            return false;
        }

        State = PlaybackState.Playing;
        _playbackEndedRaised = false;
        return true;
    }

    public bool Pause()
    {
        ThrowIfDisposed();

        if (!EnsureInitialized())
        {
            return false;
        }

        if (State is not PlaybackState.Playing)
        {
            return false;
        }

        var result = ExecuteCommand("set", "pause", "yes");
        if (result < 0)
        {
            _logger.LogWarning("mpv pause request failed with code {Result}", result);
            return false;
        }

        State = PlaybackState.Paused;
        return true;
    }

    public bool Stop()
    {
        ThrowIfDisposed();

        if (!EnsureInitialized())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(CurrentMediaPath))
        {
            return false;
        }

        var result = ExecuteCommand("stop");
        if (result < 0)
        {
            _logger.LogWarning("mpv stop request failed with code {Result}", result);
            return false;
        }

        State = PlaybackState.Stopped;
        _playbackEndedRaised = false;
        return true;
    }

    public double GetPositionSeconds()
    {
        ThrowIfDisposed();

        if (!EnsureInitialized())
        {
            return 0;
        }

        SyncPlaybackLifecycle();
        return Math.Max(0, GetDoubleProperty("time-pos"));
    }

    public double GetDurationSeconds()
    {
        ThrowIfDisposed();

        if (!EnsureInitialized())
        {
            return 0;
        }

        SyncPlaybackLifecycle();
        return Math.Max(0, GetDoubleProperty("duration"));
    }

    public bool Seek(double positionSeconds)
    {
        ThrowIfDisposed();

        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(CurrentMediaPath))
        {
            return false;
        }

        var duration = GetDurationSeconds();
        if (duration <= 0)
        {
            return false;
        }

        var clampedPosition = Math.Clamp(positionSeconds, 0, duration);
        var result = SetDoubleProperty("time-pos", clampedPosition);

        if (result < 0)
        {
            _logger.LogWarning("mpv seek request failed with code {Result}", result);
            return false;
        }

        if (State is PlaybackState.Stopped)
        {
            State = PlaybackState.Loaded;
        }

        return true;
    }

    public bool SetVolume(double volume)
    {
        ThrowIfDisposed();

        var clampedVolume = Math.Clamp(volume, 0, 100);
        _volume = clampedVolume;

        if (!EnsureInitialized())
        {
            return true;
        }

        var result = SetDoubleProperty("volume", clampedVolume);
        if (result < 0)
        {
            _logger.LogWarning("mpv volume request failed with code {Result}", result);
            return false;
        }

        return true;
    }

    public bool SetMuted(bool isMuted)
    {
        ThrowIfDisposed();

        _isMuted = isMuted;

        if (!EnsureInitialized())
        {
            return true;
        }

        var result = SetFlagProperty("mute", isMuted);
        if (result < 0)
        {
            _logger.LogWarning("mpv mute request failed with code {Result}", result);
            return false;
        }

        return true;
    }

    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        if (_initialized)
        {
            var result = ExecuteCommand("stop");
            if (result < 0)
            {
                _logger.LogDebug("mpv stop during shutdown returned code {Result}.", result);
            }
        }

        ResetPlaybackState(clearMediaPath: true);
        CleanupHandle();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Shutdown();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private bool EnsureInitialized()
    {
        if (_initialized)
        {
            return true;
        }

        if (_renderTarget == nint.Zero)
        {
            _logger.LogWarning("mpv cannot initialize before a render target handle is assigned.");
            return false;
        }

        var windowId = ((long)_renderTarget).ToString(CultureInfo.InvariantCulture);
        if (SetOptionString("wid", windowId) < 0)
        {
            _logger.LogError("Failed to assign mpv render target window id: {WindowId}", windowId);
            return false;
        }

        SetOptionString("force-window", "yes");

        var initializeResult = MpvNative.mpv_initialize(_handle);
        if (initializeResult < 0)
        {
            _logger.LogError("Failed to initialize mpv. Error code: {ErrorCode}", initializeResult);
            return false;
        }

        var volumeResult = SetDoubleProperty("volume", _volume);
        if (volumeResult < 0)
        {
            _logger.LogWarning("Failed to apply mpv volume after initialization. Error code: {ErrorCode}", volumeResult);
        }

        var muteResult = SetFlagProperty("mute", _isMuted);
        if (muteResult < 0)
        {
            _logger.LogWarning("Failed to apply mpv mute after initialization. Error code: {ErrorCode}", muteResult);
        }

        _initialized = true;
        _logger.LogInformation("mpv player service initialized with embedded render target {WindowHandle}.", _renderTarget);
        return true;
    }

    private int SetOptionString(string name, string value)
    {
        var namePtr = nint.Zero;
        var valuePtr = nint.Zero;

        try
        {
            namePtr = Marshal.StringToCoTaskMemUTF8(name);
            valuePtr = Marshal.StringToCoTaskMemUTF8(value);
            return MpvNative.mpv_set_option_string(_handle, namePtr, valuePtr);
        }
        finally
        {
            if (namePtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(namePtr);
            }

            if (valuePtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(valuePtr);
            }
        }
    }

    private int ExecuteCommand(params string[] arguments)
    {
        var argumentPointers = new nint[arguments.Length + 1];
        var commandArrayPtr = nint.Zero;

        try
        {
            for (var index = 0; index < arguments.Length; index++)
            {
                argumentPointers[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);
            }

            commandArrayPtr = Marshal.AllocHGlobal(IntPtr.Size * argumentPointers.Length);
            Marshal.Copy(argumentPointers, 0, commandArrayPtr, argumentPointers.Length);

            return MpvNative.mpv_command(_handle, commandArrayPtr);
        }
        finally
        {
            if (commandArrayPtr != nint.Zero)
            {
                Marshal.FreeHGlobal(commandArrayPtr);
            }

            foreach (var pointer in argumentPointers)
            {
                if (pointer != nint.Zero)
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }
        }
    }

    private void ResetPlaybackState(bool clearMediaPath)
    {
        State = PlaybackState.None;
        _playbackEndedRaised = false;

        if (clearMediaPath)
        {
            CurrentMediaPath = null;
        }
    }

    private void SyncPlaybackLifecycle()
    {
        if (string.IsNullOrWhiteSpace(CurrentMediaPath) || State is PlaybackState.None)
        {
            return;
        }

        if (!GetFlagProperty("eof-reached"))
        {
            return;
        }

        State = PlaybackState.Stopped;

        if (_playbackEndedRaised)
        {
            return;
        }

        _playbackEndedRaised = true;
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private double GetDoubleProperty(string name)
    {
        var namePtr = nint.Zero;
        var valuePtr = nint.Zero;

        try
        {
            namePtr = Marshal.StringToCoTaskMemUTF8(name);
            valuePtr = Marshal.AllocHGlobal(sizeof(double));
            Marshal.StructureToPtr(0d, valuePtr, false);

            var result = MpvNative.mpv_get_property(_handle, namePtr, MpvFormatDouble, valuePtr);
            if (result < 0)
            {
                return 0;
            }

            return Marshal.PtrToStructure<double>(valuePtr);
        }
        finally
        {
            if (namePtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(namePtr);
            }

            if (valuePtr != nint.Zero)
            {
                Marshal.FreeHGlobal(valuePtr);
            }
        }
    }

    private int SetDoubleProperty(string name, double value)
    {
        var namePtr = nint.Zero;
        var valuePtr = nint.Zero;

        try
        {
            namePtr = Marshal.StringToCoTaskMemUTF8(name);
            valuePtr = Marshal.AllocHGlobal(sizeof(double));
            Marshal.StructureToPtr(value, valuePtr, false);
            return MpvNative.mpv_set_property(_handle, namePtr, MpvFormatDouble, valuePtr);
        }
        finally
        {
            if (namePtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(namePtr);
            }

            if (valuePtr != nint.Zero)
            {
                Marshal.FreeHGlobal(valuePtr);
            }
        }
    }

    private int SetFlagProperty(string name, bool value)
    {
        var namePtr = nint.Zero;
        var valuePtr = nint.Zero;

        try
        {
            namePtr = Marshal.StringToCoTaskMemUTF8(name);
            valuePtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(valuePtr, value ? 1 : 0);
            return MpvNative.mpv_set_property(_handle, namePtr, MpvFormatFlag, valuePtr);
        }
        finally
        {
            if (namePtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(namePtr);
            }

            if (valuePtr != nint.Zero)
            {
                Marshal.FreeHGlobal(valuePtr);
            }
        }
    }

    private bool GetFlagProperty(string name)
    {
        var namePtr = nint.Zero;
        var valuePtr = nint.Zero;

        try
        {
            namePtr = Marshal.StringToCoTaskMemUTF8(name);
            valuePtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(valuePtr, 0);

            var result = MpvNative.mpv_get_property(_handle, namePtr, MpvFormatFlag, valuePtr);
            if (result < 0)
            {
                return false;
            }

            return Marshal.ReadInt32(valuePtr) != 0;
        }
        finally
        {
            if (namePtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(namePtr);
            }

            if (valuePtr != nint.Zero)
            {
                Marshal.FreeHGlobal(valuePtr);
            }
        }
    }

    private static void EnsureNativeLibraryExists()
    {
        var nativeLibraryPath = Path.Combine(AppContext.BaseDirectory, "mpv-2.dll");
        if (!File.Exists(nativeLibraryPath))
        {
            throw new FileNotFoundException(
                "mpv-2.dll was not found. Place the file at 'Lior/vendor/mpv/mpv-2.dll' before running the app.",
                nativeLibraryPath);
        }
    }

    private void CleanupHandle()
    {
        if (_handle == nint.Zero)
        {
            return;
        }

        MpvNative.mpv_terminate_destroy(_handle);
        _handle = nint.Zero;
        _renderTarget = nint.Zero;
        _initialized = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
