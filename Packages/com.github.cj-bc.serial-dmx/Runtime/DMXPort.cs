using System;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;

/// シリアル to DMX変換ケーブルを用いたDMXPortを扱うためのクラス。
public class DMXPort
{
    private byte[] _buf;
    private int _nextFreeChannel = 1;
    private string _portName;
    private int _baudRate;

    public DMXPort(string portName, int baudRate, int initialMaxChannels)
    {
        _portName = portName;
        _buf = new byte[initialMaxChannels];
        _baudRate = baudRate;
    }

    // 動的にチャンネルの切り分け等をしたい可能性があるかなと思ったた
    // め、Builderパターン等を用いて最初からチャンネルの振り分けを行う
    // のではなく後から動的に使用するチャンネルを増やせるようにした。

    /// Request rights to write to set of channels.
    public bool TryRequestChannel(int amount, out Action<byte[]> writer, out int headChannel)
    {
        if (_buf.Length < _nextFreeChannel + amount)
        {
            // 本来倍々で増やしたりするものだが、DMXに限っては「基本的
            // に伸びることはない」ことと「長くなればなるほど送信レー
            // トが下がってしまって困る」ことからピッタリのresizeにし
            // ている。
            Array.Resize(ref _buf, _buf.Length + amount);
        }

        int head = headChannel = _nextFreeChannel;
        writer = d => {
            var s = _buf.AsSpan(head, amount);
            for (int i = 0; i < s.Length; i++)
            {
                s[i] = d[i];
            }
        };

        _nextFreeChannel += amount;

        return true;
    }

    /// シリアル通信を開始する。
    public async UniTask Begin(CancellationToken cancellationToken)
    {
        using var port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.Two);
        port.Open();

        while (!cancellationToken.IsCancellationRequested)
        {
            port.BreakState = true;
            await UniTask.Delay(TimeSpan.FromMilliseconds(1), cancellationToken: cancellationToken);
            port.BreakState = false;
            await UniTask.Delay(TimeSpan.FromMilliseconds(1), cancellationToken: cancellationToken);
            port.Write(_buf, 0, _buf.Length);
            await UniTask.NextFrame();
        }
        port.Close();
    }
}

