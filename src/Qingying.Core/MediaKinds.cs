namespace Qingying.Core;

public static class MediaKinds
{
    public static readonly string[] VideoExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".ts", ".m2ts", ".mts", ".mpg", ".mpeg", ".mpe", ".3gp", ".3g2",
        ".rmvb", ".rm", ".ogv", ".vob", ".asf", ".divx", ".f4v", ".dat"
    ];

    public static readonly string[] AudioExtensions =
    [
        ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".oga", ".wma",
        ".ape", ".opus", ".aiff", ".aif", ".ac3", ".dts", ".mka", ".cue"
    ];

    public static readonly string[] SubtitleExtensions =
    [
        ".srt", ".ass", ".ssa", ".sub", ".vtt"
    ];

    public static bool IsVideo(string path) => HasExtension(path, VideoExtensions);

    public static bool IsAudio(string path) => HasExtension(path, AudioExtensions);

    public static bool IsSubtitle(string path) => HasExtension(path, SubtitleExtensions);

    public static bool IsMedia(string path) => IsVideo(path) || IsAudio(path);

    public static void Collect(IEnumerable<string> paths, ICollection<string> media, ICollection<string> subtitles)
    {
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (Directory.Exists(raw))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(raw);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    Classify(file, media, subtitles);
                }
            }
            else if (File.Exists(raw))
            {
                Classify(raw, media, subtitles);
            }
        }
    }

    public static string DialogFilter
    {
        get
        {
            var media = string.Join(";", VideoExtensions.Concat(AudioExtensions).Select(ext => "*" + ext));
            var video = string.Join(";", VideoExtensions.Select(ext => "*" + ext));
            var audio = string.Join(";", AudioExtensions.Select(ext => "*" + ext));
            var subs = string.Join(";", SubtitleExtensions.Select(ext => "*" + ext));
            return $"影音文件|{media}|视频|{video}|音频|{audio}|字幕|{subs}|所有文件|*.*";
        }
    }

    private static void Classify(string path, ICollection<string> media, ICollection<string> subtitles)
    {
        if (IsMedia(path))
        {
            media.Add(Path.GetFullPath(path));
        }
        else if (IsSubtitle(path))
        {
            subtitles.Add(Path.GetFullPath(path));
        }
    }

    private static bool HasExtension(string path, IEnumerable<string> extensions)
    {
        var extension = Path.GetExtension(path);
        return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
