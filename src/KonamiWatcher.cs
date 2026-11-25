using System.Collections.Generic;
using System.Windows.Input;

namespace BlockLauncher;

/// <summary>
/// 上上下下左右左右 BA。
/// 单独拿出来是为了能测：窗口只负责把按键喂进来。
/// </summary>
public sealed class KonamiWatcher
{
    private static readonly Key[] Code =
    {
        Key.Up, Key.Up, Key.Down, Key.Down,
        Key.Left, Key.Right, Key.Left, Key.Right,
        Key.B, Key.A
    };

    private readonly List<Key> _buffer = new();

    /// <summary>喂一个按键；凑齐了返回 true 并清空缓冲</summary>
    public bool Feed(Key key)
    {
        _buffer.Add(key);
        if (_buffer.Count > Code.Length)
            _buffer.RemoveAt(0);

        if (_buffer.Count != Code.Length) return false;

        for (var i = 0; i < Code.Length; i++)
        {
            if (_buffer[i] != Code[i]) return false;
        }

        _buffer.Clear();
        return true;
    }

    public void Reset() => _buffer.Clear();
}
