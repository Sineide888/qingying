using System.Text;
using LibVLCSharp.Shared;
using Qingying.Core;

var failures = new List<string>();

void Expect(bool condition, string message)
{
    if (!condition)
    {
        failures.Add(message);
    }
}

Expect(PlaylistLogic.Resolve(-1, 3, RepeatMode.Off, PlaylistMove.Next) == 0, "空选择时下一个应为第一项");
Expect(PlaylistLogic.Resolve(0, 3, RepeatMode.Off, PlaylistMove.Next) == 1, "顺序播放应进入下一项");
Expect(PlaylistLogic.Resolve(2, 3, RepeatMode.Off, PlaylistMove.Ended) == -1, "顺序播放到结尾应停止");
Expect(PlaylistLogic.Resolve(2, 3, RepeatMode.List, PlaylistMove.Ended) == 0, "列表循环到结尾应回到第一项");
Expect(PlaylistLogic.Resolve(1, 3, RepeatMode.One, PlaylistMove.Ended) == 1, "单曲循环应留在当前项");
Expect(PlaylistLogic.Resolve(0, 3, RepeatMode.Off, PlaylistMove.Previous) == 0, "第一项再向前应留在第一项");
Expect(PlaylistLogic.Resolve(0, 3, RepeatMode.List, PlaylistMove.Previous) == 2, "列表循环的第一项向前应到最后一项");
Expect(TimeFormat.Format(65_000) == "01:05", "分秒格式");
Expect(TimeFormat.Format(3_661_000) == "01:01:01", "时分秒格式");
Expect(MediaKinds.IsVideo("movie.MKV") && MediaKinds.IsAudio("song.Flac") && MediaKinds.IsSubtitle("movie.srt"), "扩展名识别");

var library = Path.Combine(Path.GetTempPath(), "qingying-tree-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(library, "子目录"));
File.WriteAllBytes(Path.Combine(library, "a.mp4"), [0]);
File.WriteAllBytes(Path.Combine(library, "子目录", "b.mkv"), [0]);
File.WriteAllText(Path.Combine(library, "note.txt"), "skip");
var folderNode = PlaylistTree.CreateFolder(library);
Expect(folderNode is not null && folderNode.IsFolder && folderNode.Name == new DirectoryInfo(library).Name, "文件夹成为分组");
Expect(folderNode is not null && PlaylistTree.CountFiles(folderNode) == 2, "分组里只收录影音文件");
Expect(folderNode is not null && folderNode.Children.Any(child => child.IsFolder && child.Children.Count == 1), "子文件夹可以折叠");
if (folderNode is not null)
{
    folderNode.IsExpanded = false;
    var visible = PlaylistTree.Visible([folderNode]);
    Expect(visible.Count == 1 && visible[0] == folderNode, "收起后连选范围不包含隐藏文件");
    folderNode.IsExpanded = true;
}

var first = new PlaylistNode("a.mp4", "a.mp4", null);
var second = new PlaylistNode("b.mp4", "b.mp4", null);
var third = new PlaylistNode("c.mp4", "c.mp4", null);
var order = new List<PlaylistNode> { first, second, third };
Expect(PlaylistTree.TryMove(order, [third], first, PlaylistDropKind.Before), "文件可以拖到另一项前面");
Expect(order[0] == third && order[1] == first && order[2] == second, "拖到前面后顺序改变");
Expect(PlaylistTree.TryMove(order, [third, first], second, PlaylistDropKind.After), "多项可以一起拖动");
Expect(order[0] == second && order[1] == third && order[2] == first, "多项拖动保持原来的相对顺序");
var group = new PlaylistNode("分组", null, "group");
var loose = new PlaylistNode("d.mp4", "d.mp4", null);
var mixed = new List<PlaylistNode> { loose, group };
Expect(PlaylistTree.TryMove(mixed, [loose], group, PlaylistDropKind.Into), "文件可以拖进文件夹");
Expect(mixed.Count == 1 && group.Children.Count == 1 && group.Children[0] == loose, "拖进文件夹后成为子项");
Expect(!PlaylistTree.TryMove(mixed, [group], group, PlaylistDropKind.Into), "文件夹不能拖进自己");
Expect(PlaylistTree.TopMost([group, loose]).Count == 1 && PlaylistTree.TopMost([group, loose])[0] == group, "选中文件夹时不重复删除其中的文件");
try
{
    Directory.Delete(library, recursive: true);
}
catch (IOException)
{
}

var media = new List<string>();
var subs = new List<string>();
var temp = Path.Combine(Path.GetTempPath(), "qingying-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var wav = Path.Combine(temp, "tone.wav");
    File.WriteAllBytes(wav, MakeToneWav());
    File.WriteAllText(Path.Combine(temp, "note.txt"), "skip");
    MediaKinds.Collect([temp], media, subs);
    Expect(media.Count == 1 && media[0].EndsWith("tone.wav", StringComparison.OrdinalIgnoreCase), "文件夹只收集影音文件");

    LibVLCSharp.Shared.Core.Initialize();
    using var libvlc = new LibVLC("--no-video-title-show", "--aout=adummy", "--vout=dummy");
    using var player = new MediaPlayer(libvlc);
    using var source = new Media(libvlc, wav, FromType.FromPath);
    var opened = new TaskCompletionSource<VLCState>(TaskCreationOptions.RunContinuationsAsynchronously);
    player.Playing += (_, _) => opened.TrySetResult(VLCState.Playing);
    player.EndReached += (_, _) => opened.TrySetResult(VLCState.Ended);
    player.EncounteredError += (_, _) => opened.TrySetResult(VLCState.Error);
    player.Play(source);
    var finished = await Task.WhenAny(opened.Task, Task.Delay(8000));
    if (finished != opened.Task)
    {
        failures.Add("播放测试超时，LibVLC 没有进入播放或结束状态");
    }
    else if (opened.Task.Result == VLCState.Error)
    {
        failures.Add("LibVLC 无法播放测试音频");
    }
}
finally
{
    try
    {
        Directory.Delete(temp, recursive: true);
    }
    catch (IOException)
    {
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("轻影播放引擎检查通过");
return 0;

static byte[] MakeToneWav()
{
    const int sampleRate = 8000;
    const short seconds = 1;
    var sampleCount = sampleRate * seconds;
    var data = new byte[sampleCount * 2];
    for (var i = 0; i < sampleCount; i++)
    {
        var sample = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 8000);
        data[i * 2] = (byte)(sample & 0xff);
        data[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
    }

    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
    writer.Write(Encoding.ASCII.GetBytes("RIFF"));
    writer.Write(36 + data.Length);
    writer.Write(Encoding.ASCII.GetBytes("WAVE"));
    writer.Write(Encoding.ASCII.GetBytes("fmt "));
    writer.Write(16);
    writer.Write((short)1);
    writer.Write((short)1);
    writer.Write(sampleRate);
    writer.Write(sampleRate * 2);
    writer.Write((short)2);
    writer.Write((short)16);
    writer.Write(Encoding.ASCII.GetBytes("data"));
    writer.Write(data.Length);
    writer.Write(data);
    return stream.ToArray();
}
