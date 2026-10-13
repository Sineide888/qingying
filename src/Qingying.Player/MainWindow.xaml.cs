using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using Qingying.Core;

namespace Qingying;

public partial class MainWindow : Window
{
    private enum TrackKind
    {
        Audio,
        Video,
        Subtitle
    }

    private readonly UserSettings _settings;
    private readonly SettingsStore _store = new();
    private readonly PlaybackSession _session;
    private readonly InstanceBroker _broker;
    private bool _seeking;
    private bool _syncingVolume;
    private bool _fullscreen;
    private bool _playlistBeforeFullscreen;
    private WindowState _stateBeforeFullscreen;
    private bool _suppressSpeedEvent = true;
    private bool _syncingImage = true;
    private const string PlaylistDragFormat = "Qingying.PlaylistMove";
    private PlaylistNode? _selectionAnchor;
    private PlaylistNode? _pressNode;
    private Point _pressPoint;
    private bool _dragMoved;
    private bool _keepMultiSelection;
    private PlaylistNode? _dropHighlight;

    public MainWindow(UserSettings settings, string[] startupFiles, InstanceBroker broker)
    {
        _settings = settings;
        _settings.Shortcuts ??= new ShortcutSettings();
        _broker = broker;
        InitializeComponent();

        Width = Math.Clamp(settings.WindowWidth, MinWidth, 1600);
        Height = Math.Clamp(settings.WindowHeight, MinHeight, 1000);
        PlaylistColumn.Width = new GridLength(Math.Clamp(settings.PlaylistWidth, 220, 480));

        _session = new PlaybackSession(Dispatcher);
        _session.Volume = Math.Clamp(settings.Volume, 0, 100);
        _session.IsMuted = settings.IsMuted;
        _session.Rate = settings.Rate <= 0 ? 1 : settings.Rate;
        _session.RepeatMode = settings.Repeat;
        _session.Aspect = settings.Aspect;
        _session.Brightness = (float)settings.Brightness;
        _session.Contrast = (float)settings.Contrast;
        _session.Saturation = (float)settings.Saturation;
        _session.Gamma = (float)settings.Gamma;
        _session.Hue = settings.Hue;
        _session.StateChanged += OnStateChanged;
        _session.PositionChanged += OnPositionChanged;
        _session.MediaChanged += OnMediaChanged;
        _session.Failed += message => Dispatcher.Invoke(() => ShowError(message));
        _session.PlaylistChanged += () => Dispatcher.Invoke(UpdatePlaylistChrome);

        VideoSurface.MediaPlayer = _session.MediaPlayer;
        PlaylistView.ItemsSource = _session.Roots;
        VolumeSlider.Value = _session.Volume;
        LoadImageSliders();
        UpdateMuteIcon();
        UpdateRepeatButton();
        UpdateShortcutHints();
        SelectSpeed(_session.Rate);
        _suppressSpeedEvent = false;
        _syncingImage = false;
        SetPlaylistVisible(settings.PlaylistVisible);

        _broker.FilesReceived += files => Dispatcher.Invoke(() => AcceptPaths(files, append: true));

        if (startupFiles.Length > 0)
        {
            AcceptPaths(startupFiles, append: false);
        }
        else
        {
            _session.Restore(settings.PlaylistTree, settings.Playlist, settings.LastIndex);
            UpdatePlaylistChrome();
            var current = _session.CurrentIndex >= 0 ? CurrentItem() : null;
            if (current is not null)
            {
                TitleFile.Text = current.Name;
                Reveal(current);
            }
        }
    }

    private void AcceptPaths(IEnumerable<string> paths, bool append)
    {
        var files = new List<string>();
        var folders = new List<string>();
        var subtitles = new List<string>();
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (Directory.Exists(raw))
            {
                folders.Add(raw);
            }
            else if (File.Exists(raw))
            {
                if (MediaKinds.IsSubtitle(raw))
                {
                    subtitles.Add(raw);
                }
                else if (MediaKinds.IsMedia(raw))
                {
                    files.Add(raw);
                }
            }
        }

        if (!append && (files.Count > 0 || folders.Count > 0))
        {
            _session.Clear();
        }

        var start = PlaylistTree.CountFiles(_session.Roots);
        var added = _session.AddFiles(files);
        foreach (var folder in folders)
        {
            if (_session.AddFolder(folder))
            {
                added++;
            }
        }

        foreach (var subtitle in subtitles)
        {
            if (!_session.AddSubtitle(subtitle) && added == 0)
            {
                ShowError("请先播放视频，再加载字幕。");
            }
        }

        if (added > 0 && !_session.IsPlaying && files.Count > 0)
        {
            _session.PlayIndex(start);
        }
        else if (added == 0 && subtitles.Count == 0)
        {
            ShowError(folders.Count > 0 ? "这些文件夹里没有可播放的影音文件。" : "没有找到可播放的影音文件。");
        }

        UpdatePlaylistChrome();
    }

    private void OnStateChanged(PlayerViewState state)
    {
        PlayIcon.Text = state == PlayerViewState.Playing ? "\uE769" : "\uE768";
        TitleStatus.Text = state switch
        {
            PlayerViewState.Playing => "",
            PlayerViewState.Paused => "已暂停",
            _ => "已停止"
        };

        if (state == PlayerViewState.Playing)
        {
            HideError();
            var item = CurrentItem();
            if (item?.FullPath is not null && MediaKinds.IsAudio(item.FullPath))
            {
                ShowAudio(item.Name);
            }
            else
            {
                var size = _session.VideoSize();
                if (size.Width == 0 || size.Height == 0)
                {
                    ShowAudio(item?.Name ?? "音频");
                }
                else
                {
                    ShowVideo();
                }
            }
        }
        else
        {
            StopDisc();
        }

        UpdateMuteIcon();
    }

    private void OnPositionChanged(long time, long length)
    {
        if (!_seeking)
        {
            TimeSlider.Maximum = Math.Max(length, 1);
            TimeSlider.Value = Math.Clamp(time, 0, TimeSlider.Maximum);
        }

        CurrentTimeText.Text = TimeFormat.Format(time);
        TotalTimeText.Text = length > 0 ? TimeFormat.Format(length) : "--:--";
    }

    private void OnMediaChanged(PlaylistNode? item)
    {
        TitleFile.Text = item?.Name ?? "本地影音播放器";
        if (item is null)
        {
            return;
        }

        Reveal(item);
        if (item.FullPath is not null && MediaKinds.IsAudio(item.FullPath))
        {
            ShowAudio(item.Name);
        }
        else
        {
            ShowVideo();
        }
    }

    private PlaylistNode? CurrentItem()
    {
        var files = PlaylistTree.Flatten(_session.Roots);
        return _session.CurrentIndex >= 0 && _session.CurrentIndex < files.Count ? files[_session.CurrentIndex] : null;
    }

    private void UpdatePlaylistChrome()
    {
        var count = PlaylistTree.CountFiles(_session.Roots);
        PlaylistCount.Text = count.ToString();
        EmptyPlaylist.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyState.Visibility == Visibility.Visible)
        {
            EmptyTitle.Text = count == 0 ? "把影片拖到这里" : "双击列表开始播放";
            EmptySubtitle.Text = count == 0
                ? "支持 MP4、MKV、AVI、MOV、FLV、RMVB、MP3、FLAC 等常见格式"
                : "文件夹可以展开或收起";
        }
    }

    private void Reveal(PlaylistNode node)
    {
        foreach (var ancestor in PlaylistTree.Ancestors(_session.Roots, node))
        {
            ancestor.IsExpanded = true;
        }

        PlaylistView.UpdateLayout();
        if (FindContainer(PlaylistView, node) is TreeViewItem item)
        {
            item.IsSelected = true;
            item.BringIntoView();
        }
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, object target)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(target) is TreeViewItem direct)
        {
            return direct;
        }

        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem container)
            {
                container.UpdateLayout();
                var found = FindContainer(container, target);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private void ShowVideo()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        AudioState.Visibility = Visibility.Collapsed;
        VideoSurface.Visibility = Visibility.Visible;
        VideoSurface.UpdateLayout();
        BringVideoOverlayForward();
        StopDisc();
    }

    private void ShowAudio(string name)
    {
        VideoSurface.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        AudioState.Visibility = Visibility.Visible;
        AudioTitle.Text = name;
        if (FindResource("DiscSpin") is Storyboard spin)
        {
            spin.Begin(this, true);
        }
    }

    private void ShowEmpty()
    {
        VideoSurface.Visibility = Visibility.Collapsed;
        AudioState.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        StopDisc();
    }

    private void StopDisc()
    {
        if (FindResource("DiscSpin") is Storyboard spin)
        {
            spin.Stop(this);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
    }

    private void OpenFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = MediaKinds.DialogFilter,
            Multiselect = true,
            Title = "打开影音文件"
        };
        if (dialog.ShowDialog(this) == true)
        {
            AcceptPaths(dialog.FileNames, append: true);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "打开文件夹" };
        if (dialog.ShowDialog(this) == true && dialog.FolderName is not null)
        {
            AcceptPaths([dialog.FolderName], append: true);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = PlaylistTree.Walk(_session.Roots).Where(node => node.IsSelected).ToList();
        if (selected.Count == 0 && PlaylistView.SelectedItem is PlaylistNode current)
        {
            selected.Add(current);
        }

        if (selected.Count == 0)
        {
            return;
        }

        _session.RemoveMany(selected);
        _selectionAnchor = null;
        _pressNode = null;
        if (PlaylistTree.CountFiles(_session.Roots) == 0)
        {
            ShowEmpty();
            TitleFile.Text = "本地影音播放器";
        }
    }

    private void Playlist_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || FindAncestor<ToggleButton>(source) is not null
            || FindAncestor<ScrollBar>(source) is not null)
        {
            _pressNode = null;
            return;
        }

        if (FindAncestor<TreeViewItem>(source) is not { DataContext: PlaylistNode node })
        {
            ClearPlaylistSelection();
            _pressNode = null;
            return;
        }

        if (e.ClickCount > 1)
        {
            _pressNode = null;
            return;
        }

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (shift && _selectionAnchor is not null)
        {
            SelectPlaylistRange(_selectionAnchor, node);
            e.Handled = true;
        }
        else if (ctrl)
        {
            node.IsSelected = !node.IsSelected;
            if (node.IsSelected)
            {
                _selectionAnchor = node;
            }

            e.Handled = true;
        }
        else
        {
            _keepMultiSelection = node.IsSelected && PlaylistTree.Walk(_session.Roots).Count(item => item.IsSelected) > 1;
            if (!_keepMultiSelection)
            {
                SelectPlaylistSingle(node);
            }

            _selectionAnchor = node;
        }

        _pressNode = node;
        _pressPoint = e.GetPosition(PlaylistView);
        _dragMoved = false;
    }

    private void Playlist_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressNode is null || _dragMoved || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(PlaylistView);
        if (Math.Abs(pos.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragMoved = true;
        _keepMultiSelection = false;
        DragDrop.DoDragDrop(PlaylistView, new DataObject(PlaylistDragFormat, "move"), DragDropEffects.Move);
        _pressNode = null;
        _dragMoved = false;
        HideDropMarker();
    }

    private void Playlist_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_keepMultiSelection && !_dragMoved && _pressNode is not null)
        {
            SelectPlaylistSingle(_pressNode);
        }

        _pressNode = null;
        _keepMultiSelection = false;
    }

    private void Playlist_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PlaylistDragFormat))
        {
            return;
        }

        var (target, kind) = HitPlaylistDrop(e);
        var selected = SelectedPlaylistNodes();
        e.Effects = PlaylistTree.CanMove(_session.Roots, selected, target, kind) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        ShowDropMarker(target, kind, e);
    }

    private void Playlist_DragLeave(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PlaylistDragFormat))
        {
            HideDropMarker();
        }
    }

    private void Playlist_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PlaylistDragFormat))
        {
            return;
        }

        var (target, kind) = HitPlaylistDrop(e);
        _session.MoveNodes(SelectedPlaylistNodes(), target, kind);
        HideDropMarker();
        e.Handled = true;
    }

    private List<PlaylistNode> SelectedPlaylistNodes()
    {
        var selected = PlaylistTree.Walk(_session.Roots).Where(node => node.IsSelected).ToList();
        if (selected.Count == 0 && _pressNode is not null)
        {
            selected.Add(_pressNode);
        }

        return selected;
    }

    private void SelectPlaylistSingle(PlaylistNode node)
    {
        ClearPlaylistSelection();
        node.IsSelected = true;
    }

    private void SelectPlaylistRange(PlaylistNode anchor, PlaylistNode node)
    {
        var visible = PlaylistTree.Visible(_session.Roots);
        var start = visible.IndexOf(anchor);
        var end = visible.IndexOf(node);
        if (start < 0 || end < 0)
        {
            SelectPlaylistSingle(node);
            return;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        ClearPlaylistSelection();
        for (var i = start; i <= end; i++)
        {
            visible[i].IsSelected = true;
        }
    }

    private void ClearPlaylistSelection()
    {
        foreach (var node in PlaylistTree.Walk(_session.Roots))
        {
            node.IsSelected = false;
        }
    }

    private (PlaylistNode? Target, PlaylistDropKind Kind) HitPlaylistDrop(DragEventArgs e)
    {
        var position = e.GetPosition(PlaylistView);
        if (VisualTreeHelper.HitTest(PlaylistView, position)?.VisualHit is not DependencyObject hit)
        {
            return (null, PlaylistDropKind.After);
        }

        if (FindAncestor<TreeViewItem>(hit) is not { DataContext: PlaylistNode node } item)
        {
            return (null, PlaylistDropKind.After);
        }

        var local = e.GetPosition(item);
        if (node.IsFolder && local.Y > item.ActualHeight * 0.25 && local.Y < item.ActualHeight * 0.75)
        {
            return (node, PlaylistDropKind.Into);
        }

        return (node, local.Y > item.ActualHeight / 2 ? PlaylistDropKind.After : PlaylistDropKind.Before);
    }

    private void ShowDropMarker(PlaylistNode? target, PlaylistDropKind kind, DragEventArgs e)
    {
        if (kind == PlaylistDropKind.Into && target is not null)
        {
            if (!ReferenceEquals(_dropHighlight, target))
            {
                if (_dropHighlight is not null)
                {
                    _dropHighlight.IsDropTarget = false;
                }

                _dropHighlight = target;
                target.IsDropTarget = true;
            }

            DropLine.Visibility = Visibility.Collapsed;
            return;
        }

        if (_dropHighlight is not null)
        {
            _dropHighlight.IsDropTarget = false;
            _dropHighlight = null;
        }

        var position = e.GetPosition(PlaylistView);
        if (VisualTreeHelper.HitTest(PlaylistView, position)?.VisualHit is not DependencyObject hit
            || FindAncestor<TreeViewItem>(hit) is not { } item)
        {
            DropLine.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var y = kind == PlaylistDropKind.After ? item.ActualHeight - 1 : 0;
            var point = item.TransformToAncestor(PlaylistHost).Transform(new Point(0, y));
            DropLine.Width = Math.Max(40, PlaylistHost.ActualWidth - 24);
            Canvas.SetLeft(DropLine, 12);
            Canvas.SetTop(DropLine, point.Y);
            DropLine.Visibility = Visibility.Visible;
        }
        catch (InvalidOperationException)
        {
            DropLine.Visibility = Visibility.Collapsed;
        }
    }

    private void HideDropMarker()
    {
        if (_dropHighlight is not null)
        {
            _dropHighlight.IsDropTarget = false;
            _dropHighlight = null;
        }

        DropLine.Visibility = Visibility.Collapsed;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : current is FrameworkContentElement element ? element.Parent : null;
        }

        return null;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _session.Clear();
        ShowEmpty();
        TitleFile.Text = "本地影音播放器";
        TitleStatus.Text = "就绪";
    }

    private void PlaylistItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem { DataContext: PlaylistNode node } || node.IsFolder)
        {
            return;
        }

        _session.PlayNode(node);
        e.Handled = true;
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _session.TogglePlayPause();

    private void Previous_Click(object sender, RoutedEventArgs e) => _session.Previous();

    private void Next_Click(object sender, RoutedEventArgs e) => _session.Next();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _session.Stop();
        ShowEmpty();
        TitleStatus.Text = "已停止";
    }

    private void TimeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _seeking = true;

    private void TimeSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        _session.Seek((long)TimeSlider.Value);
    }

    private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seeking)
        {
            CurrentTimeText.Text = TimeFormat.Format((long)e.NewValue);
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingVolume || _session is null)
        {
            return;
        }

        _session.Volume = (int)Math.Round(e.NewValue);
        if (_session.IsMuted && _session.Volume > 0)
        {
            _session.IsMuted = false;
        }

        UpdateMuteIcon();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _session.IsMuted = !_session.IsMuted;
        UpdateMuteIcon();
    }

    private void UpdateMuteIcon()
    {
        MuteButton.Content = _session.IsMuted || _session.Volume == 0 ? "\uE74F" : "\uE767";
    }

    private void SpeedBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSpeedEvent || SpeedBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        if (double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
        {
            _session.Rate = rate;
            if (_session.IsPlaying)
            {
                _session.MediaPlayer.SetRate((float)rate);
            }
        }
    }

    private void SelectSpeed(double rate)
    {
        foreach (ComboBoxItem item in SpeedBox.Items)
        {
            if (item.Tag is string tag && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && Math.Abs(value - rate) < 0.01)
            {
                SpeedBox.SelectedItem = item;
                return;
            }
        }

        SpeedBox.SelectedIndex = 2;
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _session.RepeatMode = _session.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.List,
            RepeatMode.List => RepeatMode.One,
            _ => RepeatMode.Off
        };
        UpdateRepeatButton();
    }

    private void UpdateRepeatButton()
    {
        RepeatButton.Content = _session.RepeatMode switch
        {
            RepeatMode.List => "列表",
            RepeatMode.One => "单曲",
            _ => "顺序"
        };
        RepeatButton.ToolTip = _session.RepeatMode switch
        {
            RepeatMode.List => "列表循环",
            RepeatMode.One => "单曲循环",
            _ => "顺序播放，播完停止"
        };
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_fullscreen)
        {
            ExitFullscreen();
        }
        else
        {
            EnterFullscreen();
        }
    }

    private void EnterFullscreen()
    {
        _stateBeforeFullscreen = WindowState;
        _playlistBeforeFullscreen = PlaylistPanel.Visibility == Visibility.Visible;
        _fullscreen = true;
        TitleBar.Visibility = Visibility.Collapsed;
        SetPlaylistVisible(false);
        WindowState = WindowState.Maximized;
        FullScreenButton.Content = "\uE73F";
    }

    private void ExitFullscreen()
    {
        _fullscreen = false;
        TitleBar.Visibility = Visibility.Visible;
        WindowState = _stateBeforeFullscreen == WindowState.Minimized ? WindowState.Normal : _stateBeforeFullscreen;
        if (_playlistBeforeFullscreen)
        {
            SetPlaylistVisible(true);
        }

        FullScreenButton.Content = "\uE740";
    }

    private void TogglePlaylist_Click(object sender, RoutedEventArgs e)
    {
        SetPlaylistVisible(PlaylistPanel.Visibility != Visibility.Visible);
    }

    private void SetPlaylistVisible(bool visible)
    {
        PlaylistPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Splitter.Visibility = PlaylistPanel.Visibility;
        SplitColumn.Width = visible ? new GridLength(4) : new GridLength(0);
        if (!visible)
        {
            PlaylistColumn.Width = new GridLength(0);
        }
        else if (PlaylistColumn.Width.Value < 220)
        {
            PlaylistColumn.Width = new GridLength(Math.Clamp(_settings.PlaylistWidth, 220, 480));
        }
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        OpenPlaybackMenu(MoreMenuButton, PlacementMode.Top);
    }

    private void Stage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_session.MediaPlayer.State is not (VLCState.Playing or VLCState.Paused))
        {
            return;
        }

        var target = sender as FrameworkElement ?? VideoHitTarget;
        target.ContextMenu = BuildPlaybackMenu();
        target.ContextMenu.PlacementTarget = target;
        target.ContextMenu.Placement = PlacementMode.MousePoint;
        target.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private static void BringVideoOverlayForward()
    {
        var overlay = NativeWindow.Find(null, "LibVLCSharp.WPF");
        if (overlay == IntPtr.Zero)
        {
            return;
        }

        NativeWindow.MoveTop(overlay);
    }

    private void OpenPlaybackMenu(FrameworkElement target, PlacementMode placement)
    {
        var menu = BuildPlaybackMenu();
        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    private static MenuItem ActionItem(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private ContextMenu BuildPlaybackMenu()
    {
        var hasFile = !string.IsNullOrWhiteSpace(_session.CurrentPath);
        var menu = new ContextMenu { MinWidth = 188 };
        menu.Items.Add(ActionItem(_session.IsPlaying ? "暂停" : "播放", () => _session.TogglePlayPause()));
        menu.Items.Add(ActionItem("停止", () => Stop_Click(this, new RoutedEventArgs())));
        menu.Items.Add(ActionItem("上一帧", () => StepFrame(-1), hasFile));
        menu.Items.Add(ActionItem("下一帧", () => StepFrame(1), hasFile));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("上一个", () => _session.PreviousItem()));
        menu.Items.Add(ActionItem("下一个", () => _session.Next()));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem(_fullscreen ? "退出全屏" : "全屏", ToggleFullscreen));

        var aspect = new MenuItem { Header = "画面比例" };
        aspect.Items.Add(AspectItem("适应窗口", AspectMode.Fit));
        aspect.Items.Add(AspectItem("16:9", AspectMode.R16x9));
        aspect.Items.Add(AspectItem("4:3", AspectMode.R4x3));
        menu.Items.Add(aspect);

        var speed = new MenuItem { Header = "播放速度" };
        foreach (var rate in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 })
        {
            var item = new MenuItem
            {
                Header = rate.ToString("0.##", CultureInfo.InvariantCulture) + "x",
                IsCheckable = true,
                IsChecked = Math.Abs(_session.Rate - rate) < 0.01
            };
            var chosen = rate;
            item.Click += (_, _) =>
            {
                _session.Rate = chosen;
                if (_session.IsPlaying || _session.MediaPlayer.State == VLCState.Paused)
                {
                    _session.MediaPlayer.SetRate((float)chosen);
                }

                SelectSpeed(chosen);
            };
            speed.Items.Add(item);
        }

        menu.Items.Add(speed);

        var repeat = new MenuItem { Header = "循环方式" };
        repeat.Items.Add(RepeatItem("顺序播放", RepeatMode.Off));
        repeat.Items.Add(RepeatItem("列表循环", RepeatMode.List));
        repeat.Items.Add(RepeatItem("单个循环", RepeatMode.One));
        menu.Items.Add(repeat);
        menu.Items.Add(ActionItem(_session.IsMuted ? "取消静音" : "静音", () => Mute_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator());

        var videoTracks = _session.VideoTracks();
        if (videoTracks.Count > 1)
        {
            var video = new MenuItem { Header = "视频轨" };
            foreach (var track in videoTracks)
            {
                video.Items.Add(TrackItem(track.Name, track.Id, track.IsSelected, TrackKind.Video));
            }

            menu.Items.Add(video);
        }

        var audioTracks = _session.AudioTracks();
        var audio = new MenuItem { Header = "音轨" };
        if (audioTracks.Count == 0)
        {
            audio.Items.Add(new MenuItem { Header = "无音轨", IsEnabled = false });
        }
        else
        {
            foreach (var track in audioTracks)
            {
                audio.Items.Add(TrackItem(track.Name, track.Id, track.IsSelected, TrackKind.Audio));
            }
        }

        menu.Items.Add(audio);

        var subtitles = new MenuItem { Header = "字幕" };
        var tracks = _session.SubtitleTracks();
        subtitles.Items.Add(TrackItem("关闭字幕", -1, tracks.All(track => !track.IsSelected), TrackKind.Subtitle));
        if (tracks.Count == 0)
        {
            subtitles.Items.Add(new MenuItem { Header = "无内嵌字幕", IsEnabled = false });
        }
        else
        {
            foreach (var track in tracks)
            {
                subtitles.Items.Add(TrackItem(track.Name, track.Id, track.IsSelected, TrackKind.Subtitle));
            }
        }

        subtitles.Items.Add(new Separator());
        subtitles.Items.Add(ActionItem("加载字幕...", LoadSubtitle));
        menu.Items.Add(subtitles);
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("截图", () => Snapshot_Click(this, new RoutedEventArgs()), hasFile));
        menu.Items.Add(ActionItem("画面调节", () => ToggleImage_Click(this, new RoutedEventArgs())));
        menu.Items.Add(ActionItem(PlaylistPanel.Visibility == Visibility.Visible ? "隐藏播放列表" : "显示播放列表", () => TogglePlaylist_Click(this, new RoutedEventArgs())));
        menu.Items.Add(ActionItem(Topmost ? "取消置顶" : "窗口置顶", () => Topmost = !Topmost));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("打开文件", () => OpenFiles_Click(this, new RoutedEventArgs())));
        menu.Items.Add(ActionItem("打开所在文件夹", OpenContainingFolder, hasFile));
        menu.Items.Add(ActionItem("视频属性", ShowProperties, hasFile));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("关于 Lite Shadow", ShowAbout));
        return menu;
    }

    private MenuItem RepeatItem(string header, RepeatMode mode)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = _session.RepeatMode == mode };
        item.Click += (_, _) =>
        {
            _session.RepeatMode = mode;
            UpdateRepeatButton();
        };
        return item;
    }

    private void LoadSubtitle()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "字幕|*.srt;*.ass;*.ssa;*.sub;*.vtt|所有文件|*.*",
            Title = "加载字幕"
        };
        if (dialog.ShowDialog(this) == true)
        {
            AcceptPaths([dialog.FileName], append: true);
        }
    }

    private void OpenContainingFolder()
    {
        var path = _session.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + path + "\"",
            UseShellExecute = true
        });
    }

    private void ShowProperties()
    {
        var facts = _session.ReadFacts();
        if (facts.Count == 0)
        {
            return;
        }

        var dialog = new MediaInfoWindow(facts) { Owner = this };
        dialog.ShowDialog();
    }

    private MenuItem AspectItem(string header, AspectMode mode)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = _session.Aspect == mode };
        item.Click += (_, _) =>
        {
            _session.Aspect = mode;
            if (_session.IsPlaying)
            {
                _session.MediaPlayer.AspectRatio = mode switch
                {
                    AspectMode.R16x9 => "16:9",
                    AspectMode.R4x3 => "4:3",
                    _ => string.Empty
                };
                _session.MediaPlayer.Scale = 0;
            }
        };
        return item;
    }

    private MenuItem TrackItem(string header, int id, bool selected, TrackKind kind)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = selected };
        item.Click += (_, _) =>
        {
            switch (kind)
            {
                case TrackKind.Subtitle:
                    _session.SelectSubtitle(id);
                    break;
                case TrackKind.Video:
                    _session.SelectVideoTrack(id);
                    break;
                default:
                    _session.SelectAudioTrack(id);
                    break;
            }
        };
        return item;
    }

    private void ShowAbout()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
        MessageBox.Show(
            $"Lite Shadow\n轻影播放器 {version}\n\n播放本地视频和音频。快捷键可以在设置里修改。\n默认：空格播放，左右快进，上下音量，逗号和句号逐帧，F12 截图，Ctrl+I 画面调节。\n\n播放引擎 LibVLC，以 LGPL-2.1 许可随程序分发。",
            "关于 Lite Shadow",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (Hit(_settings.Shortcuts.PlayPause))
        {
            _session.TogglePlayPause();
        }
        else if (Hit(_settings.Shortcuts.Stop))
        {
            Stop_Click(this, new RoutedEventArgs());
        }
        else if (key == Key.Escape && _fullscreen)
        {
            ExitFullscreen();
        }
        else if (Hit(_settings.Shortcuts.Fullscreen) || key == Key.F11)
        {
            ToggleFullscreen();
        }
        else if (Hit(_settings.Shortcuts.FramePrevious))
        {
            StepFrame(-1);
        }
        else if (Hit(_settings.Shortcuts.FrameNext))
        {
            StepFrame(1);
        }
        else if (Hit(_settings.Shortcuts.Snapshot))
        {
            Snapshot_Click(this, new RoutedEventArgs());
        }
        else if (Hit(_settings.Shortcuts.PreviousItem))
        {
            _session.PreviousItem();
        }
        else if (Hit(_settings.Shortcuts.NextItem))
        {
            _session.Next();
        }
        else if (Hit(_settings.Shortcuts.SeekBackward))
        {
            _session.Seek(_session.MediaPlayer.Time - 5000);
        }
        else if (Hit(_settings.Shortcuts.SeekForward))
        {
            _session.Seek(_session.MediaPlayer.Time + 5000);
        }
        else if (Hit(_settings.Shortcuts.VolumeUp))
        {
            SetVolume(_session.Volume + 5);
        }
        else if (Hit(_settings.Shortcuts.VolumeDown))
        {
            SetVolume(_session.Volume - 5);
        }
        else if (Hit(_settings.Shortcuts.OpenFile))
        {
            OpenFiles_Click(this, new RoutedEventArgs());
        }
        else if (Hit(_settings.Shortcuts.ImagePanel))
        {
            ToggleImage_Click(this, new RoutedEventArgs());
        }
        else if (Hit(_settings.Shortcuts.Settings))
        {
            Settings_Click(this, new RoutedEventArgs());
        }
        else if (key == Key.Delete && PlaylistView.IsKeyboardFocusWithin)
        {
            Remove_Click(this, new RoutedEventArgs());
        }
        else
        {
            return;
        }

        e.Handled = true;
        return;

        bool Hit(string gesture) => ShortcutGesture.TryParse(gesture, out var shortcut) && shortcut.Matches(key, modifiers);
    }

    private void LoadImageSliders()
    {
        BrightnessSlider.Value = _session.Brightness;
        ContrastSlider.Value = _session.Contrast;
        SaturationSlider.Value = _session.Saturation;
        GammaSlider.Value = _session.Gamma;
        HueSlider.Value = _session.Hue;
        RefreshImageLabels();
    }

    private void ImageSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingImage || _session is null)
        {
            return;
        }

        _session.Brightness = (float)BrightnessSlider.Value;
        _session.Contrast = (float)ContrastSlider.Value;
        _session.Saturation = (float)SaturationSlider.Value;
        _session.Gamma = (float)GammaSlider.Value;
        _session.Hue = (int)Math.Round(HueSlider.Value);
        _session.ApplyImage();
        RefreshImageLabels();
    }

    private void RefreshImageLabels()
    {
        BrightnessValue.Text = BrightnessSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
        ContrastValue.Text = ContrastSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
        SaturationValue.Text = SaturationSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
        GammaValue.Text = GammaSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
        HueValue.Text = ((int)Math.Round(HueSlider.Value)).ToString(CultureInfo.InvariantCulture);
    }

    private void ResetImage_Click(object sender, RoutedEventArgs e)
    {
        _syncingImage = true;
        BrightnessSlider.Value = 1;
        ContrastSlider.Value = 1;
        SaturationSlider.Value = 1;
        GammaSlider.Value = 1;
        HueSlider.Value = 0;
        _syncingImage = false;
        ImageSlider_ValueChanged(this, null!);
    }

    private void ToggleImage_Click(object sender, RoutedEventArgs e)
    {
        ImagePanel.Visibility = ImagePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FrameBack_Click(object sender, RoutedEventArgs e) => StepFrame(-1);

    private void FrameNext_Click(object sender, RoutedEventArgs e) => StepFrame(1);

    private void StepFrame(int direction)
    {
        _session.StepFrame(direction);
        TitleStatus.Text = direction < 0 ? "上一帧" : "下一帧";
    }

    private void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        var folder = _settings.SnapshotFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "轻影播放器");
        }

        if (_session.TrySnapshot(folder, out var path))
        {
            HideError();
            TitleStatus.Text = "已保存 " + Path.GetFileName(path);
        }
        else
        {
            ShowError("请先播放或暂停视频，再截图。");
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var draft = CopyShortcuts(_settings.Shortcuts);
        var dialog = new SettingsWindow(draft, string.IsNullOrWhiteSpace(_settings.SnapshotFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "轻影播放器")
            : _settings.SnapshotFolder)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _settings.Shortcuts = draft;
            _settings.SnapshotFolder = dialog.SnapshotFolder;
            UpdateShortcutHints();
        }
    }

    private void UpdateShortcutHints()
    {
        PlayButton.ToolTip = "播放 / 暂停  " + Hint(_settings.Shortcuts.PlayPause);
        PreviousButton.ToolTip = "上一个  " + Hint(_settings.Shortcuts.PreviousItem);
        NextButton.ToolTip = "下一个  " + Hint(_settings.Shortcuts.NextItem);
        StopButton.ToolTip = "停止  " + Hint(_settings.Shortcuts.Stop);
        FrameBackButton.ToolTip = "上一帧  " + Hint(_settings.Shortcuts.FramePrevious);
        FrameNextButton.ToolTip = "下一帧  " + Hint(_settings.Shortcuts.FrameNext);
        SnapshotButton.ToolTip = "截图  " + Hint(_settings.Shortcuts.Snapshot);
        FullScreenButton.ToolTip = "全屏  " + Hint(_settings.Shortcuts.Fullscreen);
        ImageButton.ToolTip = "画面调节  " + Hint(_settings.Shortcuts.ImagePanel);
        SettingsButton.ToolTip = "设置  " + Hint(_settings.Shortcuts.Settings);
    }

    private static string Hint(string gesture)
    {
        return ShortcutGesture.TryParse(gesture, out var shortcut) && !shortcut.IsEmpty ? shortcut.Format() : "未设置";
    }

    private static ShortcutSettings CopyShortcuts(ShortcutSettings source)
    {
        return new ShortcutSettings
        {
            PlayPause = source.PlayPause,
            Stop = source.Stop,
            SeekBackward = source.SeekBackward,
            SeekForward = source.SeekForward,
            VolumeUp = source.VolumeUp,
            VolumeDown = source.VolumeDown,
            PreviousItem = source.PreviousItem,
            NextItem = source.NextItem,
            FramePrevious = source.FramePrevious,
            FrameNext = source.FrameNext,
            Fullscreen = source.Fullscreen,
            Snapshot = source.Snapshot,
            OpenFile = source.OpenFile,
            ImagePanel = source.ImagePanel,
            Settings = source.Settings
        };
    }

    private void SetVolume(int volume)
    {
        _syncingVolume = true;
        VolumeSlider.Value = Math.Clamp(volume, 0, 100);
        _syncingVolume = false;
        _session.Volume = (int)VolumeSlider.Value;
        if (_session.Volume > 0)
        {
            _session.IsMuted = false;
        }

        UpdateMuteIcon();
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        SetVolume(_session.Volume + (e.Delta > 0 ? 5 : -5));
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AcceptPaths(files, append: true);
        }
    }

    private void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE739";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }

        if (PlaylistPanel.Visibility == Visibility.Visible && PlaylistColumn.Width.Value >= 220)
        {
            _settings.PlaylistWidth = PlaylistColumn.Width.Value;
        }

        _settings.PlaylistVisible = _fullscreen
            ? _playlistBeforeFullscreen
            : PlaylistPanel.Visibility == Visibility.Visible;
        _settings.Volume = _session.Volume;
        _settings.IsMuted = _session.IsMuted;
        _settings.Rate = _session.Rate;
        _settings.Repeat = _session.RepeatMode;
        _settings.Aspect = _session.Aspect;
        _settings.LastIndex = _session.CurrentIndex;
        _settings.PlaylistTree = _session.CaptureTree();
        _settings.Playlist = [];
        _settings.Brightness = BrightnessSlider.Value;
        _settings.Contrast = ContrastSlider.Value;
        _settings.Saturation = SaturationSlider.Value;
        _settings.Gamma = GammaSlider.Value;
        _settings.Hue = (int)Math.Round(HueSlider.Value);
        try
        {
            _store.Save(_settings);
        }
        catch (IOException)
        {
        }

        VideoSurface.MediaPlayer = null;
        _session.Dispose();
    }
}

file static class NativeWindow
{
    public static IntPtr Find(string? className, string windowName)
    {
        return FindWindow(className, windowName);
    }

    public static void MoveTop(IntPtr window)
    {
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0013 | 0x0040);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
