using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Qingying.Core;

public sealed class PlaylistNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isPlaying;
    private bool _isSelected;
    private bool _isDropTarget;

    public PlaylistNode(string name, string? fullPath, string? folderPath)
    {
        Name = name;
        FullPath = fullPath;
        FolderPath = folderPath;
        Extension = string.IsNullOrEmpty(fullPath)
            ? string.Empty
            : Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant();
    }

    public string Name { get; }

    public string? FullPath { get; }

    public string? FolderPath { get; }

    public string Extension { get; }

    public bool IsFolder => string.IsNullOrEmpty(FullPath);

    public ObservableCollection<PlaylistNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value)
            {
                return;
            }

            _isDropTarget = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class PlaylistSnapshot
{
    public string Name { get; set; } = string.Empty;

    public string? Path { get; set; }

    public string? FolderPath { get; set; }

    public bool Expanded { get; set; } = true;

    public List<PlaylistSnapshot> Children { get; set; } = [];
}
