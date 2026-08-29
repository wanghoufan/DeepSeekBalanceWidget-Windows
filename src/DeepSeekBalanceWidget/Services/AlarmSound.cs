using System;
using System.IO;
using System.Media;

namespace DeepSeekBalanceWidget.Services;

/// <summary>
/// 程序化合成的循环警报音。三种风格可切换：
/// - Soft 柔和：单音 440Hz 缓起伏，不刺耳；
/// - Standard 标准：880/660Hz 双音交替（原默认，节奏偏紧）；
/// - Urgent 急促：短促的 1000Hz 脉冲，节奏最快，适合强提醒。
/// 不依赖任何外部音频文件：运行时生成 8kHz 16bit PCM WAV 写入内存流，
/// 用 SoundPlayer.PlayLooping 循环播放；Stop 后即静音。
/// </summary>
public static class AlarmSound
{
    private const int SampleRate = 8000;
    private static MemoryStream? _stream;
    private static readonly SoundPlayer Player = new();
    private static string _currentStyle = "";

    public static void Play(string style)
    {
        if (string.IsNullOrEmpty(style)) style = "Standard";
        try
        {
            if (_currentStyle != style || _stream is null)
            {
                _stream = BuildAlarmWav(style);
                _currentStyle = style;
            }
            _stream.Position = 0;
            Player.Stream = _stream;
            Player.PlayLooping();
        }
        catch
        {
            // 无声环境（无声卡/被策略禁用）时静默降级，只保留弹窗
        }
    }

    public static void Stop()
    {
        try { Player.Stop(); } catch { }
    }

    /// <summary>生成对应风格的循环 WAV（已做淡入淡出，可无缝衔接）。</summary>
    private static MemoryStream BuildAlarmWav(string style)
    {
        var pcm = style switch
        {
            "Soft" => BuildSoft(),
            "Urgent" => BuildUrgent(),
            _ => BuildStandard()
        };
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            int dataLen = pcm.Length;
            int byteRate = SampleRate * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + dataLen);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(byteRate);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataLen);
            writer.Write(pcm);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>柔和：单音 440Hz，整体缓慢起伏，音量更低。</summary>
    private static byte[] BuildSoft()
    {
        // 1.6s 一个循环
        int total = SampleRate * 8 / 5;
        int fade = SampleRate / 50; // 20ms 淡入淡出
        var pcm = new byte[total * 2];
        for (int i = 0; i < total; i++)
        {
            // 整体包络呈正弦慢呼吸：约 0.4Hz
            double breath = 0.55 + 0.45 * Math.Sin(2 * Math.PI * 0.4 * i / SampleRate);
            double env = 1.0;
            if (i < fade) env = i / (double)fade;
            else if (i >= total - fade) env = (total - i) / (double)fade;
            short value = (short)(Math.Sin(2 * Math.PI * 440 * i / SampleRate) * 18000 * breath * env);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return pcm;
    }

    /// <summary>标准：880/660Hz 双音交替（原默认）。</summary>
    private static byte[] BuildStandard()
    {
        int toneSamples = SampleRate * 4 / 5; // 0.8s 一段
        int fade = SampleRate / 100; // 10ms
        var pcm = new byte[(toneSamples * 2) * 2];
        WriteTone(pcm, 0, toneSamples, fade, 880, 26000);
        WriteTone(pcm, toneSamples, toneSamples, fade, 660, 26000);
        return pcm;
    }

    /// <summary>急促：4 段短促 1000Hz 脉冲，每段 0.3s 含 30ms 静音间隔，节奏最快。</summary>
    private static byte[] BuildUrgent()
    {
        int pulse = SampleRate * 3 / 10; // 0.3s 一段
        int silence = SampleRate * 3 / 100; // 30ms 静音
        int fade = SampleRate / 100; // 10ms
        int total = pulse * 4 + silence * 3;
        var pcm = new byte[total * 2];
        int pos = 0;
        for (int s = 0; s < 4; s++)
        {
            for (int i = 0; i < pulse; i++)
            {
                double env = 1.0;
                if (i < fade) env = i / (double)fade;
                else if (i >= pulse - fade) env = (pulse - i) / (double)fade;
                short value = (short)(Math.Sin(2 * Math.PI * 1000 * (pos + i) / SampleRate) * 26000 * env);
                int idx = (pos + i) * 2;
                pcm[idx] = (byte)(value & 0xFF);
                pcm[idx + 1] = (byte)((value >> 8) & 0xFF);
            }
            pos += pulse;
            if (s < 3) pos += silence; // 段间静音
        }
        return pcm;
    }

    private static void WriteTone(byte[] pcm, int offsetSamples, int count, int fade, double freq, double amplitude)
    {
        for (int i = 0; i < count; i++)
        {
            double env = 1.0;
            if (i < fade) env = i / (double)fade;
            else if (i >= count - fade) env = (count - i) / (double)fade;
            short value = (short)(Math.Sin(2 * Math.PI * freq * i / SampleRate) * amplitude * env);
            int index = (offsetSamples + i) * 2;
            pcm[index] = (byte)(value & 0xFF);
            pcm[index + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
