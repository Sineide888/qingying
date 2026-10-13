using System.Windows.Input;

namespace Qingying;

public readonly record struct ShortcutGesture(ModifierKeys Modifiers, Key Key)
{
    public bool IsEmpty => Key == Key.None;

    public bool Matches(Key key, ModifierKeys modifiers) => Key == key && Modifiers == modifiers;

    public string Format()
    {
        if (IsEmpty)
        {
            return "未设置";
        }

        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        parts.Add(Label(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out ShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text) || text == "未设置")
        {
            return true;
        }

        var modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (var token in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case ",":
                    key = Key.OemComma;
                    break;
                case ".":
                    key = Key.OemPeriod;
                    break;
                case "esc":
                case "escape":
                    key = Key.Escape;
                    break;
                default:
                    if (!Enum.TryParse(token, ignoreCase: true, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        if (key == Key.None)
        {
            return false;
        }

        gesture = new ShortcutGesture(modifiers, key);
        return true;
    }

    public static string Label(Key key) => key switch
    {
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Escape => "Esc",
        Key.Space => "Space",
        Key.MediaStop => "MediaStop",
        _ => key.ToString()
    };
}

public sealed class ShortcutSettings
{
    public string PlayPause { get; set; } = "Space";

    public string SeekBackward { get; set; } = "Left";

    public string SeekForward { get; set; } = "Right";

    public string VolumeUp { get; set; } = "Up";

    public string VolumeDown { get; set; } = "Down";

    public string PreviousItem { get; set; } = "Ctrl+Left";

    public string NextItem { get; set; } = "Ctrl+Right";

    public string Stop { get; set; } = "MediaStop";

    public string Fullscreen { get; set; } = "F";

    public string FramePrevious { get; set; } = ",";

    public string FrameNext { get; set; } = ".";

    public string Snapshot { get; set; } = "F12";

    public string OpenFile { get; set; } = "Ctrl+O";

    public string Settings { get; set; } = "Ctrl+OemComma";

    public string ImagePanel { get; set; } = "Ctrl+I";
}
