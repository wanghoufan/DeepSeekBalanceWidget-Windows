using System;
using System.IO;
using System.Media;

namespace DeepSeekBalanceWidget.Services;

/// <summary>
/// 恢复提醒专用提示音：11 种程序化合成的柔和音色，与低量预警的 AlarmSound 警报声完全独立。
/// 全部运行时合成（22.05kHz 16bit PCM），无外部音频文件依赖：
/// - Chime 门铃叮咚（默认）：E5→C5 两音慢衰减；
/// - MusicBox 八音盒：C5-E5-G5-C6 上行短音；
/// - WindChime 清脆风铃：五声音阶高频散落；
/// - WaterDrop 水滴：三声下滑"啵"；
/// - PianoArp 钢琴琶音：C-E-G-C-E 柔和上行；
/// - HarpGliss 竖琴滑音：一个八度快速上行刮奏；
/// - Xylophone 木琴：G5-C6-E6-G6 明亮短句；
/// - Guitar 吉他拨弦：Karplus-Strong 物理建模的 E 和弦分解；
/// - Bell 悠扬钟声：含非谐泛音的双击钟；
/// - BirdChirp 鸟鸣：三声变频啁啾；
/// - SunrisePad 晨光舒缓：大三和弦慢起慢落的_pad。
/// 循环播放（首尾 10ms 淡入淡出保证无缝衔接）。
/// </summary>
public static class RecoverySound
{
    private const int Rate = 22050;

    private static MemoryStream? _stream;
    private static readonly SoundPlayer Player = new();
    private static string _currentStyle = "";

    public static string DefaultStyle => "Chime";

    /// <summary>全部合法风格标签（顺序与设置页下拉框一致）。</summary>
    public static readonly string[] Styles =
    {
        "Chime", "MusicBox", "WindChime", "WaterDrop", "PianoArp", "HarpGliss",
        "Xylophone", "Guitar", "Bell", "BirdChirp", "SunrisePad"
    };

    public static void Play(string style)
    {
        if (Array.IndexOf(Styles, style) < 0) style = DefaultStyle;
        try
        {
            if (_currentStyle != style || _stream is null)
            {
                _stream = BuildWav(style);
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

    // ---------------------------------------------------------------- build

    private static MemoryStream BuildWav(string style)
    {
        double[] samples = style switch
        {
            "MusicBox" => BuildMusicBox(),
            "WindChime" => BuildWindChime(),
            "WaterDrop" => BuildWaterDrop(),
            "PianoArp" => BuildPianoArp(),
            "HarpGliss" => BuildHarpGliss(),
            "Xylophone" => BuildXylophone(),
            "Guitar" => BuildGuitar(),
            "Bell" => BuildBell(),
            "BirdChirp" => BuildBirdChirp(),
            "SunrisePad" => BuildSunrisePad(),
            _ => BuildChime()
        };
        ApplyEdgeFades(samples);
        return ToWav(samples);
    }

    /// <summary>门铃叮咚（默认）：E5 → C5 两声慢衰减。</summary>
    private static double[] BuildChime()
    {
        var buf = NewBuffer(2.4);
        AddPluck(buf, 0.00, 659.26, 15000, 2.0); // E5
        AddPluck(buf, 0.60, 523.25, 15000, 2.0); // C5
        return buf;
    }

    /// <summary>八音盒：C5-E5-G5-C6 上行短句，带轻微回声。</summary>
    private static double[] BuildMusicBox()
    {
        var buf = NewBuffer(1.9);
        double[] notes = { 523.25, 659.26, 783.99, 1046.50 };
        for (int i = 0; i < notes.Length; i++)
        {
            AddPluck(buf, 0.28 * i, notes[i], 11000, 3.2);
            AddPluck(buf, 0.28 * i + 0.12, notes[i], 3500, 4.5); // 回声
        }
        return buf;
    }

    /// <summary>清脆风铃：五声音阶高频散落。</summary>
    private static double[] BuildWindChime()
    {
        var buf = NewBuffer(2.5);
        double[] freqs = { 1318.5, 1046.5, 1760.0, 1174.7, 1568.0, 1318.5 };
        double[] times = { 0.00, 0.32, 0.61, 0.98, 1.35, 1.72 };
        for (int i = 0; i < freqs.Length; i++)
            AddPluck(buf, times[i], freqs[i], 9000, 3.5);
        return buf;
    }

    /// <summary>水滴：三声下滑"啵"。</summary>
    private static double[] BuildWaterDrop()
    {
        var buf = NewBuffer(1.8);
        AddDrop(buf, 0.00, 1250, 640, 0.25, 13000);
        AddDrop(buf, 0.55, 1150, 600, 0.25, 11500);
        AddDrop(buf, 1.10, 1050, 560, 0.25, 10000);
        return buf;
    }

    /// <summary>钢琴琶音：C4-E4-G4-C5-E5 柔和上行。</summary>
    private static double[] BuildPianoArp()
    {
        var buf = NewBuffer(2.4);
        double[] notes = { 261.63, 329.63, 392.00, 523.25, 659.26 };
        for (int i = 0; i < notes.Length; i++)
            AddPluck(buf, 0.22 * i, notes[i], 12000, 2.2);
        return buf;
    }

    /// <summary>竖琴滑音：一个八度 13 个半音快速上行刮奏。</summary>
    private static double[] BuildHarpGliss()
    {
        var buf = NewBuffer(1.8);
        for (int k = 0; k < 13; k++)
        {
            double freq = 261.63 * Math.Pow(2, k / 12.0);
            double amp = 14000 - 400 * k; // 越高越轻
            AddPluck(buf, 0.07 * k, freq, amp, 5.0);
        }
        return buf;
    }

    /// <summary>木琴：G5-C6-E6-G6 明亮短句（含 2.76 倍频泛音）。</summary>
    private static double[] BuildXylophone()
    {
        var buf = NewBuffer(1.7);
        double[] notes = { 783.99, 1046.50, 1318.51, 1567.98 };
        for (int i = 0; i < notes.Length; i++)
        {
            AddPluck(buf, 0.30 * i, notes[i], 12000, 9.0);
            AddPluck(buf, 0.30 * i, notes[i] * 2.76, 3600, 13.0);
        }
        return buf;
    }

    /// <summary>吉他拨弦：Karplus-Strong 物理建模的 E 和弦分解。</summary>
    private static double[] BuildGuitar()
    {
        var buf = NewBuffer(2.6);
        double[] notes = { 164.81, 246.94, 329.63, 415.30, 493.88 }; // E3 B3 E4 G#4 B4
        for (int i = 0; i < notes.Length; i++)
            AddKarplus(buf, (int)(0.18 * i * Rate), notes[i], 11000, (int)(2.2 * Rate));
        return buf;
    }

    /// <summary>悠扬钟声：含非谐泛音的双击钟。</summary>
    private static double[] BuildBell()
    {
        var buf = NewBuffer(2.7);
        AddBellStrike(buf, 0.00, 523.25, 13000, 1.5);
        AddBellStrike(buf, 1.35, 659.26, 11000, 1.5);
        return buf;
    }

    /// <summary>鸟鸣：三声变频啁啾。</summary>
    private static double[] BuildBirdChirp()
    {
        var buf = NewBuffer(1.5);
        AddChirp(buf, 0.00, 2600, 3400, 2900, 0.22, 9000);
        AddChirp(buf, 0.50, 3200, 2400, 2800, 0.18, 8000);
        AddChirp(buf, 0.90, 2700, 3300, 3000, 0.15, 7000);
        return buf;
    }

    /// <summary>晨光舒缓：大三和弦慢起慢落。</summary>
    private static double[] BuildSunrisePad()
    {
        var buf = NewBuffer(2.8);
        double[] freqs = { 523.25, 659.26, 783.99 }; // C5 E5 G5
        for (int i = 0; i < buf.Length; i++)
        {
            double p = i / (double)buf.Length;
            double env = Math.Pow(Math.Sin(Math.PI * p), 1.5); // 慢起慢落
            double v = 0;
            foreach (double f in freqs)
                v += Math.Sin(2 * Math.PI * f * i / Rate)
                   + 0.5 * Math.Sin(2 * Math.PI * (f + 0.7) * i / Rate); // 轻微失谐增加暖感
            buf[i] = v / (freqs.Length * 1.5) * 11000 * env;
        }
        return buf;
    }

    // -------------------------------------------------------------- helpers

    private static double[] NewBuffer(double seconds) => new double[(int)(seconds * Rate)];

    /// <summary>拨音：4ms 快攻击 + 指数衰减正弦，一直衰减到缓冲区末尾。</summary>
    private static void AddPluck(double[] buf, double startSec, double freq, double amp, double decayRate)
    {
        int start = (int)(startSec * Rate);
        int attack = (int)(Rate * 0.004);
        for (int i = start; i < buf.Length; i++)
        {
            double t = (i - start) / (double)Rate;
            double env = Math.Exp(-t * decayRate);
            if (i - start < attack) env *= (i - start) / (double)attack;
            buf[i] += Math.Sin(2 * Math.PI * freq * t) * amp * env;
        }
    }

    /// <summary>水滴：频率从 f0 平滑下滑到 f1 的短促"啵"。</summary>
    private static void AddDrop(double[] buf, double startSec, double f0, double f1, double durSec, double amp)
    {
        int start = (int)(startSec * Rate);
        int len = (int)(durSec * Rate);
        int attack = (int)(Rate * 0.004);
        double phase = 0;
        for (int i = 0; i < len && start + i < buf.Length; i++)
        {
            double p = i / (double)len;
            phase += 2 * Math.PI * (f0 + (f1 - f0) * p) / Rate;
            double env = Math.Exp(-p * 4.0);
            if (i < attack) env *= i / (double)attack;
            buf[start + i] += Math.Sin(phase) * amp * env;
        }
    }

    /// <summary>鸟鸣啁啾：频率 f0→fPeak→fEnd 的弯音短句。</summary>
    private static void AddChirp(double[] buf, double startSec, double f0, double fPeak, double fEnd, double durSec, double amp)
    {
        int start = (int)(startSec * Rate);
        int len = (int)(durSec * Rate);
        int attack = (int)(Rate * 0.005);
        double phase = 0;
        for (int i = 0; i < len && start + i < buf.Length; i++)
        {
            double p = i / (double)len;
            double freq = p < 0.5
                ? f0 + (fPeak - f0) * (p * 2)
                : fPeak + (fEnd - fPeak) * ((p - 0.5) * 2);
            phase += 2 * Math.PI * freq / Rate;
            double env = Math.Sin(Math.PI * p); // 中间最响
            if (i < attack) env *= i / (double)attack;
            buf[start + i] += Math.Sin(phase) * amp * env;
        }
    }

    /// <summary>钟击：基频 + 非谐泛音（2 / 2.76 / 5.4 倍频），高泛音衰减更快。</summary>
    private static void AddBellStrike(double[] buf, double startSec, double freq, double amp, double decayRate)
    {
        (double ratio, double gain, double decayMul)[] partials =
        {
            (1.00, 1.00, 1.00),
            (2.00, 0.55, 1.60),
            (2.76, 0.30, 2.20),
            (5.40, 0.12, 3.00)
        };
        int start = (int)(startSec * Rate);
        int attack = (int)(Rate * 0.006);
        foreach (var (ratio, gain, decayMul) in partials)
        {
            for (int i = start; i < buf.Length; i++)
            {
                double t = (i - start) / (double)Rate;
                double env = Math.Exp(-t * decayRate * decayMul);
                if (i - start < attack) env *= (i - start) / (double)attack;
                buf[i] += Math.Sin(2 * Math.PI * freq * ratio * t) * amp * gain * env;
            }
        }
    }

    /// <summary>Karplus-Strong 拨弦物理建模：噪声激励环 + 延迟反馈低通，音色接近真实吉他。</summary>
    private static void AddKarplus(double[] buf, int startSample, double freq, double amp, int durSamples)
    {
        int n = Math.Max(2, (int)(Rate / freq));
        var ring = new double[n];
        var rnd = new Random(freq.GetHashCode());
        for (int i = 0; i < n; i++) ring[i] = rnd.NextDouble() * 2 - 1;

        int idx = 0;
        for (int i = 0; i < durSamples && startSample + i < buf.Length; i++)
        {
            double cur = ring[idx];
            double nxt = ring[(idx + 1) % n];
            ring[idx] = 0.996 * 0.5 * (cur + nxt);
            buf[startSample + i] += cur * amp;
            idx = (idx + 1) % n;
        }
    }

    /// <summary>首尾 10ms 淡入淡出，保证 PlayLooping 无缝衔接。</summary>
    private static void ApplyEdgeFades(double[] buf)
    {
        int fade = Rate / 100;
        for (int i = 0; i < fade && i < buf.Length; i++)
        {
            buf[i] *= i / (double)fade;
            buf[buf.Length - 1 - i] *= i / (double)fade;
        }
    }

    private static MemoryStream ToWav(double[] samples)
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            int dataLen = samples.Length * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + dataLen);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(Rate);
            writer.Write(Rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataLen);
            foreach (double s in samples)
            {
                short v = (short)Math.Clamp(s, short.MinValue, short.MaxValue);
                writer.Write(v);
            }
        }
        ms.Position = 0;
        return ms;
    }
}
