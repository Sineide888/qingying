using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Qingying.Core;

namespace Qingying;

public sealed class PlaybackSession : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LibVLC _libvlc;
    private readonly MediaPlayer _player;
    private Media? _media;
    private int _currentIndex = -1;
    private int _generation;
    private int _volume = 80;
    private bool _muted;
    private float _rate = 1;
    private string? _lastError;

    public PlaybackSession(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _libvlc = new LibVLC("--no-video-title-show");
        _libvlc.Log += (_, args) =>
        {
            if (args.Level == LogLevel.Error)
            {
                _lastError = args.Message;
            }
        };
        _player = new MediaPlayer(_libvlc);
        _player.TimeChanged += (_, _) => Dispatch(PublishPosition);
        _player.LengthChanged += (_, _) => Dispatch(PublishPosition);
        _player.Playing += (_, _) => Dispatch(OnPlaying);
        _player.Paused += (_, _) => Dispatch(() => StateChanged?.Invoke(PlayerViewState.Paused));
        _player.Stopped += (_, _) => Dispatch(() => StateChanged?.Invoke(PlayerViewState.Stopped));
        _player.EncounteredError += (_, _) => Dispatch(OnError);
        _player.EndReached += (_, _) =>
        {
            var seen = _generation;
            _ = _dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(80);
                if (seen != _generation)
                {
                    return;
                }

                var next = PlaylistLogic.Resolve(_currentIndex, Flat().Count, RepeatMode, PlaylistMove.Ended);
                if (next < 0)
                {
                    Stop();
                    return;
                }

                PlayIndex(next);
            });
        };
    }

    public MediaPlayer MediaPlayer => _player;

    public ObservableCollection<PlaylistNode> Roots { get; } = [];

    public int CurrentIndex => _currentIndex;

    public float Brightness { get; set; } = 1;

    public float Contrast { get; set; } = 1;

    public float Saturation { get; set; } = 1;

    public float Gamma { get; set; } = 1;

    public int Hue { get; set; }

    public event Action? PlaylistChanged;

    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

    public AspectMode Aspect { get; set; } = AspectMode.Fit;

    public bool IsPlaying => _player.IsPlaying;

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            _player.Volume = _volume;
        }
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            _player.Mute = value;
        }
    }

    public double Rate
    {
        get => _rate;
        set => _rate = (float)Math.Clamp(value, 0.25, 2);
    }

    public event Action<PlayerViewState>? StateChanged;

    public event Action<long, long>? PositionChanged;

    public event Action<PlaylistNode?>? MediaChanged;

    public event Action<string>? Failed;

    public event Action? TracksChanged;

    public int AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (PlaylistTree.ContainsFile(Roots, fullPath))
            {
                continue;
            }

            Roots.Add(new PlaylistNode(Path.GetFileName(fullPath), fullPath, null));
            added++;
        }

        if (added > 0)
        {
            PlaylistChanged?.Invoke();
        }

        return added;
    }

    public bool AddFolder(string folderPath)
    {
        var folder = PlaylistTree.CreateFolder(folderPath);
        if (folder is null)
        {
            return false;
        }

        if (Roots.Any(node => node.IsFolder && string.Equals(node.FolderPath, folder.FolderPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var existing = PlaylistTree.Flatten(Roots)
            .Select(node => node.FullPath)
            .Where(path => path is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        PruneDuplicates(folder, existing);
        if (PlaylistTree.CountFiles(folder) == 0)
        {
            return false;
        }

        Roots.Add(folder);
        PlaylistChanged?.Invoke();
        return true;
    }

    public void Restore(IEnumerable<PlaylistSnapshot> nodes, IEnumerable<string> legacyPaths, int lastIndex)
    {
        foreach (var snapshot in nodes)
        {
            var restored = PlaylistTree.FromSnapshot(snapshot);
            if (restored is not null)
            {
                Roots.Add(restored);
            }
        }

        if (Roots.Count == 0)
        {
            AddFiles(legacyPaths.Where(File.Exists));
        }

        var files = Flat();
        if (lastIndex >= 0 && lastIndex < files.Count)
        {
            SetCurrent(lastIndex, playing: false);
        }

        PlaylistChanged?.Invoke();
    }

    public void Clear()
    {
        Stop();
        Roots.Clear();
        SetCurrent(-1, playing: false);
        PlaylistChanged?.Invoke();
    }

    public void RemoveMany(IReadOnlyList<PlaylistNode> nodes)
    {
        var tops = PlaylistTree.TopMost(nodes);
        if (tops.Count == 0)
        {
            return;
        }

        var current = CurrentItem();
        var removingCurrent = current is not null && tops.Any(node => ReferenceEquals(node, current) || PlaylistTree.ContainsNode(node, current));
        var wasPlaying = _player.IsPlaying || _player.State == VLCState.Paused;
        var oldIndex = _currentIndex;
        foreach (var node in tops)
        {
            PlaylistTree.Remove(Roots, node);
        }

        FinishPlaylistEdit(current, removingCurrent, wasPlaying, oldIndex);
    }

    public bool MoveNodes(IReadOnlyList<PlaylistNode> nodes, PlaylistNode? target, PlaylistDropKind kind)
    {
        var current = CurrentItem();
        if (!PlaylistTree.TryMove(Roots, nodes, target, kind))
        {
            return false;
        }

        var index = current is null ? -1 : Flat().IndexOf(current);
        SetCurrent(index, _player.IsPlaying);
        PlaylistChanged?.Invoke();
        return true;
    }

    public void Remove(PlaylistNode node)
    {
        var current = CurrentItem();
        var removingCurrent = current is not null && PlaylistTree.ContainsNode(node, current);
        var wasPlaying = _player.IsPlaying || _player.State == VLCState.Paused;
        var oldIndex = _currentIndex;
        if (!PlaylistTree.Remove(Roots, node))
        {
            return;
        }

        FinishPlaylistEdit(current, removingCurrent, wasPlaying, oldIndex);
    }

    private void FinishPlaylistEdit(PlaylistNode? current, bool removingCurrent, bool wasPlaying, int oldIndex)
    {
        PlaylistChanged?.Invoke();
        var files = Flat();
        if (files.Count == 0)
        {
            Stop();
            return;
        }

        if (!removingCurrent)
        {
            var index = current is null ? -1 : files.IndexOf(current);
            SetCurrent(index, _player.IsPlaying);
            return;
        }

        var next = Math.Clamp(oldIndex, 0, files.Count - 1);
        if (wasPlaying)
        {
            PlayIndex(next);
        }
        else
        {
            SetCurrent(next, playing: false);
        }
    }

    public List<PlaylistSnapshot> CaptureTree()
    {
        return Roots.Select(PlaylistTree.ToSnapshot).ToList();
    }

    public void TogglePlayPause()
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
            return;
        }

        if (_player.State == VLCState.Paused)
        {
            _player.Play();
            return;
        }

        PlayIndex(_currentIndex >= 0 ? _currentIndex : 0);
    }

    public void PlayNode(PlaylistNode node)
    {
        var index = Flat().IndexOf(node);
        if (index >= 0)
        {
            PlayIndex(index);
        }
    }

    public void Stop()
    {
        _generation++;
        if (_player.State != VLCState.Stopped)
        {
            _player.Stop();
        }

        SetCurrent(_currentIndex, playing: false);
        PositionChanged?.Invoke(0, 0);
    }

    public void PlayIndex(int index)
    {
        var files = Flat();
        if (index < 0 || index >= files.Count)
        {
            return;
        }

        var item = files[index];
        if (item.FullPath is null || !File.Exists(item.FullPath))
        {
            SetCurrent(index, playing: false);
            Failed?.Invoke($"找不到文件：{item.Name}");
            return;
        }

        _generation++;
        SetCurrent(index, playing: true);
        MediaChanged?.Invoke(item);
        _media?.Dispose();
        _media = new Media(_libvlc, item.FullPath!, FromType.FromPath);
        _lastError = null;
        _player.Play(_media);
    }

    public void Next()
    {
        var index = PlaylistLogic.Resolve(_currentIndex, Flat().Count, RepeatMode, PlaylistMove.Next);
        if (index < 0)
        {
            Stop();
            return;
        }

        PlayIndex(index);
    }

    public void PreviousItem()
    {
        var index = PlaylistLogic.Resolve(_currentIndex, Flat().Count, RepeatMode, PlaylistMove.Previous);
        if (index < 0)
        {
            return;
        }

        PlayIndex(index);
    }

    public void Previous()
    {
        if (_player.Time > 3000 && (_player.IsPlaying || _player.State == VLCState.Paused))
        {
            _player.Time = 0;
            return;
        }

        var index = PlaylistLogic.Resolve(_currentIndex, Flat().Count, RepeatMode, PlaylistMove.Previous);
        if (index < 0)
        {
            return;
        }

        PlayIndex(index);
    }

    public void StepFrame(int direction)
    {
        if (_player.Media is null && _currentIndex < 0)
        {
            return;
        }

        if (direction > 0)
        {
            _player.NextFrame();
            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
        }

        var target = Math.Max(0, _player.Time - 40);
        _player.Time = target;
        _player.NextFrame();
    }

    public bool TrySnapshot(string folder, out string path)
    {
        path = string.Empty;
        if (_player.State is not (VLCState.Playing or VLCState.Paused))
        {
            return false;
        }

        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(CurrentItem()?.Name ?? "frame");
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(invalid, '_');
        }

        path = Path.Combine(folder, $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        return _player.TakeSnapshot(0, path, 0, 0);
    }

    public void ApplyImage()
    {
        var changed = Math.Abs(Brightness - 1f) > 0.01f
            || Math.Abs(Contrast - 1f) > 0.01f
            || Math.Abs(Saturation - 1f) > 0.01f
            || Math.Abs(Gamma - 1f) > 0.01f
            || Hue != 0;
        _player.SetAdjustInt(VideoAdjustOption.Enable, changed ? 1 : 0);
        if (!changed)
        {
            return;
        }

        _player.SetAdjustFloat(VideoAdjustOption.Brightness, Brightness);
        _player.SetAdjustFloat(VideoAdjustOption.Contrast, Contrast);
        _player.SetAdjustFloat(VideoAdjustOption.Saturation, Saturation);
        _player.SetAdjustFloat(VideoAdjustOption.Gamma, Gamma);
        _player.SetAdjustInt(VideoAdjustOption.Hue, Hue);
    }

    public void Seek(long milliseconds)
    {
        if (_player.Length <= 0)
        {
            return;
        }

        _player.Time = Math.Clamp(milliseconds, 0, _player.Length);
    }

    public bool AddSubtitle(string path)
    {
        if (_player.Media is null)
        {
            return false;
        }

        var uri = new Uri(path).AbsoluteUri;
        return _player.AddSlave(MediaSlaveType.Subtitle, uri, select: true);
    }

    public IReadOnlyList<TrackChoice> SubtitleTracks() => Describe(_player.SpuDescription, _player.Spu);

    public IReadOnlyList<TrackChoice> AudioTracks() => Describe(_player.AudioTrackDescription, _player.AudioTrack);

    public void SelectSubtitle(int id) => _player.SetSpu(id);

    public void SelectAudioTrack(int id) => _player.SetAudioTrack(id);

    public string? CurrentPath => CurrentItem()?.FullPath;

    public IReadOnlyList<TrackChoice> VideoTracks() => Describe(_player.VideoTrackDescription, _player.VideoTrack);

    public void SelectVideoTrack(int id) => _player.SetVideoTrack(id);

    public IReadOnlyList<MediaFact> ReadFacts()
    {
        var item = CurrentItem();
        if (item?.FullPath is null)
        {
            return [];
        }

        var path = item.FullPath;
        var facts = new List<MediaFact>
        {
            new("文件", item.Name),
            new("路径", path),
            new("格式", MediaText.FormatName(path)),
            new("大小", File.Exists(path) ? MediaText.FormatSize(new FileInfo(path).Length) : "未知")
        };

        var length = _player.Length;
        facts.Add(new("时长", length > 0 ? TimeFormat.Format(length) : "未知"));

        MediaTrack[] tracks = [];
        MediaStats stats = default;
        var media = _player.Media;
        if (media is not null)
        {
            try
            {
                tracks = media.Tracks ?? [];
                stats = media.Statistics;
            }
            catch (Exception ex) when (ex is ObjectDisposedException or VLCException)
            {
            }
        }

        var hasVideo = tracks.Any(track => track.TrackType == TrackType.Video);
        var video = hasVideo ? tracks.First(track => track.TrackType == TrackType.Video) : default;
        var audioCandidates = tracks.Where(track => track.TrackType == TrackType.Audio).ToArray();
        var audioId = _player.AudioTrack;
        var hasAudio = audioCandidates.Length > 0;
        var audio = hasAudio
            ? audioCandidates.FirstOrDefault(track => track.Id == audioId, audioCandidates[0])
            : default;

        if (hasVideo)
        {
            var picture = video.Data.Video;
            var width = picture.Width;
            var height = picture.Height;
            if (width == 0 || height == 0)
            {
                var size = VideoSize();
                width = size.Width;
                height = size.Height;
            }

            facts.Add(new("分辨率", width == 0 || height == 0 ? "未知" : $"{width} × {height}"));
            facts.Add(new("帧率", MediaText.FormatFps(picture.FrameRateNum, picture.FrameRateDen, _player.Fps)));
            facts.Add(new("视频编码", MediaText.CodecLabel(video)));
            facts.Add(new("视频码率", MediaText.FormatBits(video.Bitrate)));
        }
        else
        {
            var size = VideoSize();
            facts.Add(new("分辨率", size.Width == 0 ? "无视频" : $"{size.Width} × {size.Height}"));
            facts.Add(new("帧率", _player.Fps > 1 ? $"{_player.Fps:0.##} fps" : "无视频"));
            facts.Add(new("视频编码", "无视频"));
            facts.Add(new("视频码率", "无视频"));
        }

        if (hasAudio)
        {
            var sound = audio.Data.Audio;
            var language = string.IsNullOrWhiteSpace(audio.Language) ? string.Empty : " · " + audio.Language;
            facts.Add(new("音频编码", MediaText.CodecLabel(audio)));
            facts.Add(new("音频", $"{MediaText.ChannelName(sound.Channels)} · {sound.Rate} Hz{language}"));
            facts.Add(new("音频码率", MediaText.FormatBits(audio.Bitrate)));
        }
        else
        {
            facts.Add(new("音频编码", "无音频"));
            facts.Add(new("音频", "无音频"));
            facts.Add(new("音频码率", "无音频"));
        }

        var live = stats.DemuxBitrate > 1 ? stats.DemuxBitrate : stats.InputBitrate;
        facts.Add(new("码流", MediaText.FormatByteRate(live)));
        return facts;
    }

    public (uint Width, uint Height) VideoSize()
    {
        uint width = 0;
        uint height = 0;
        _player.Size(0, ref width, ref height);
        return (width, height);
    }

    public void Dispose()
    {
        _generation++;
        _player.Stop();
        _media?.Dispose();
        _player.Dispose();
        _libvlc.Dispose();
    }

    private List<PlaylistNode> Flat() => PlaylistTree.Flatten(Roots);

    private PlaylistNode? CurrentItem()
    {
        var files = Flat();
        return _currentIndex >= 0 && _currentIndex < files.Count ? files[_currentIndex] : null;
    }

    private void SetCurrent(int index, bool playing)
    {
        _currentIndex = index;
        var files = Flat();
        for (var i = 0; i < files.Count; i++)
        {
            files[i].IsPlaying = playing && i == index;
        }
    }

    private static void PruneDuplicates(PlaylistNode node, HashSet<string> existing)
    {
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (child.IsFolder)
            {
                PruneDuplicates(child, existing);
                if (PlaylistTree.CountFiles(child) == 0)
                {
                    node.Children.RemoveAt(i);
                }
            }
            else if (child.FullPath is not null && !existing.Add(child.FullPath))
            {
                node.Children.RemoveAt(i);
            }
        }
    }

    private void OnPlaying()
    {
        _player.SetRate(_rate);
        _player.Volume = _volume;
        _player.Mute = _muted;
        ApplyAspect();
        ApplyImage();
        StateChanged?.Invoke(PlayerViewState.Playing);
        TracksChanged?.Invoke();
        PublishPosition();
    }

    private void OnError()
    {
        var name = CurrentItem()?.Name ?? "该文件";
        var detail = string.IsNullOrWhiteSpace(_lastError) ? "无法播放" : _lastError;
        Failed?.Invoke($"{name}：{detail}");
        StateChanged?.Invoke(PlayerViewState.Stopped);
    }

    private void ApplyAspect()
    {
        _player.AspectRatio = Aspect switch
        {
            AspectMode.R16x9 => "16:9",
            AspectMode.R4x3 => "4:3",
            _ => string.Empty
        };
        _player.Scale = 0;
    }

    private void PublishPosition()
    {
        PositionChanged?.Invoke(Math.Max(0, _player.Time), Math.Max(0, _player.Length));
    }

    private void Dispatch(Action action)
    {
        _ = _dispatcher.InvokeAsync(action);
    }

    private static IReadOnlyList<TrackChoice> Describe(TrackDescription[]? tracks, int currentId)
    {
        if (tracks is null || tracks.Length == 0)
        {
            return [];
        }

        return tracks
            .Select(track => new TrackChoice(track.Id, string.IsNullOrWhiteSpace(track.Name) ? $"轨道 {track.Id}" : track.Name, track.Id == currentId))
            .ToArray();
    }
}

public enum PlayerViewState
{
    Opening,
    Playing,
    Paused,
    Stopped
}

public sealed record TrackChoice(int Id, string Name, bool IsSelected);

public sealed record MediaFact(string Label, string Value);

file static class MediaText
{
    public static string FormatName(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(extension) ? "未知" : extension;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string FormatBits(uint bitsPerSecond)
    {
        if (bitsPerSecond == 0)
        {
            return "未知";
        }

        var kbps = bitsPerSecond / 1000d;
        return kbps >= 1000
            ? (kbps / 1000d).ToString("0.00", CultureInfo.InvariantCulture) + " Mbps"
            : kbps.ToString("0", CultureInfo.InvariantCulture) + " kbps";
    }

    public static string FormatByteRate(float bytesPerSecond)
    {
        if (bytesPerSecond <= 1)
        {
            return "未知";
        }

        return FormatBits((uint)(bytesPerSecond * 8));
    }

    public static string FormatFps(uint numerator, uint denominator, float playerFps)
    {
        var fps = denominator == 0 ? 0 : numerator / (double)denominator;
        if (fps < 1 && playerFps > 1)
        {
            fps = playerFps;
        }

        return fps < 1 ? "未知" : fps.ToString("0.##", CultureInfo.InvariantCulture) + " fps";
    }

    public static string CodecLabel(MediaTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.Description))
        {
            return track.Description;
        }

        var code = FourCC(track.Codec).ToLowerInvariant();
        return code switch
        {
            "h264" or "avc1" or "x264" => "H.264 / AVC",
            "hevc" or "hvc1" or "hev1" => "H.265 / HEVC",
            "vp09" or "vp90" => "VP9",
            "vp08" or "vp80" => "VP8",
            "av01" => "AV1",
            "mp4v" => "MPEG-4",
            "mpgv" or "mp2v" => "MPEG-2",
            "wvc1" or "vc-1" => "VC-1",
            "mp4a" or "aac" => "AAC",
            "mpga" or "mp3" or ".mp3" => "MP3",
            "ac-3" or "ac3" => "AC-3",
            "eac3" => "E-AC-3",
            "flac" => "FLAC",
            "opus" => "Opus",
            "vorb" => "Vorbis",
            "dts" => "DTS",
            _ => string.IsNullOrWhiteSpace(code) ? "未知" : code.ToUpperInvariant()
        };
    }

    public static string ChannelName(uint channels)
    {
        return channels switch
        {
            0 => "未知声道",
            1 => "单声道",
            2 => "立体声",
            6 => "5.1 声道",
            8 => "7.1 声道",
            _ => channels + " 声道"
        };
    }

    private static string FourCC(uint value)
    {
        var bytes = new[]
        {
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 24) & 0xFF)
        };
        var text = Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
        var readable = text.Count(ch => ch is >= (char)32 and <= (char)126);
        return readable >= 3 ? text : string.Empty;
    }
}
