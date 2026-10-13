using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Qingying;

public partial class SettingsWindow : Window
{
    private readonly ShortcutSettings _shortcuts;
    private readonly List<(string Label, Func<string> Get, Action<string> Set, TextBox Box)> _rows = [];

    public SettingsWindow(ShortcutSettings shortcuts, string snapshotFolder)
    {
        _shortcuts = shortcuts;
        InitializeComponent();
        SnapshotFolderBox.Text = snapshotFolder;
        AddRow("播放 / 暂停", () => _shortcuts.PlayPause, value => _shortcuts.PlayPause = value);
        AddRow("停止", () => _shortcuts.Stop, value => _shortcuts.Stop = value);
        AddRow("快退 5 秒", () => _shortcuts.SeekBackward, value => _shortcuts.SeekBackward = value);
        AddRow("快进 5 秒", () => _shortcuts.SeekForward, value => _shortcuts.SeekForward = value);
        AddRow("音量增大", () => _shortcuts.VolumeUp, value => _shortcuts.VolumeUp = value);
        AddRow("音量减小", () => _shortcuts.VolumeDown, value => _shortcuts.VolumeDown = value);
        AddRow("上一个", () => _shortcuts.PreviousItem, value => _shortcuts.PreviousItem = value);
        AddRow("下一个", () => _shortcuts.NextItem, value => _shortcuts.NextItem = value);
        AddRow("上一帧", () => _shortcuts.FramePrevious, value => _shortcuts.FramePrevious = value);
        AddRow("下一帧", () => _shortcuts.FrameNext, value => _shortcuts.FrameNext = value);
        AddRow("全屏", () => _shortcuts.Fullscreen, value => _shortcuts.Fullscreen = value);
        AddRow("截图", () => _shortcuts.Snapshot, value => _shortcuts.Snapshot = value);
        AddRow("打开文件", () => _shortcuts.OpenFile, value => _shortcuts.OpenFile = value);
        AddRow("画面调节", () => _shortcuts.ImagePanel, value => _shortcuts.ImagePanel = value);
        AddRow("打开设置", () => _shortcuts.Settings, value => _shortcuts.Settings = value);
    }

    public string SnapshotFolder => SnapshotFolderBox.Text.Trim();

    private void AddRow(string label, Func<string> get, Action<string> set)
    {
        var box = new TextBox
        {
            Text = Display(get()),
            IsReadOnly = true,
            Width = 180,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(28, 35, 48)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(44, 52, 68)),
            CaretBrush = Brushes.White
        };
        box.PreviewKeyDown += ShortcutBox_PreviewKeyDown;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(box, Dock.Right);
        row.Children.Add(box);
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(243, 246, 250))
        });
        ShortcutList.Children.Add(row);
        _rows.Add((label, get, set, box));
    }

    private void ShortcutBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        if (sender is not TextBox box)
        {
            return;
        }

        var row = _rows.First(item => ReferenceEquals(item.Box, box));
        if (key == Key.Escape)
        {
            box.Text = Display(row.Get());
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            row.Set("未设置");
            box.Text = "未设置";
            return;
        }

        var gesture = new ShortcutGesture(Keyboard.Modifiers, key);
        var text = gesture.Format();
        row.Set(text);
        box.Text = text;
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new ShortcutSettings();
        _shortcuts.PlayPause = defaults.PlayPause;
        _shortcuts.Stop = defaults.Stop;
        _shortcuts.SeekBackward = defaults.SeekBackward;
        _shortcuts.SeekForward = defaults.SeekForward;
        _shortcuts.VolumeUp = defaults.VolumeUp;
        _shortcuts.VolumeDown = defaults.VolumeDown;
        _shortcuts.PreviousItem = defaults.PreviousItem;
        _shortcuts.NextItem = defaults.NextItem;
        _shortcuts.FramePrevious = defaults.FramePrevious;
        _shortcuts.FrameNext = defaults.FrameNext;
        _shortcuts.Fullscreen = defaults.Fullscreen;
        _shortcuts.Snapshot = defaults.Snapshot;
        _shortcuts.OpenFile = defaults.OpenFile;
        _shortcuts.ImagePanel = defaults.ImagePanel;
        _shortcuts.Settings = defaults.Settings;
        foreach (var row in _rows)
        {
            row.Box.Text = Display(row.Get());
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择截图文件夹" };
        if (Directory.Exists(SnapshotFolderBox.Text))
        {
            dialog.InitialDirectory = SnapshotFolderBox.Text;
        }

        if (dialog.ShowDialog(this) == true && dialog.FolderName is not null)
        {
            SnapshotFolderBox.Text = dialog.FolderName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string Display(string value)
    {
        return ShortcutGesture.TryParse(value, out var gesture) && !gesture.IsEmpty ? gesture.Format() : "未设置";
    }
}
