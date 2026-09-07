using System;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>Simple PCM16 mono linear upsample/downsample for Azure STT (expects 16 kHz).</summary>
internal static class PcmResampler
{
    public static byte[] ResamplePcm16Mono(byte[] pcm, int fromRate, int toRate)
    {
        if (pcm == null || pcm.Length < 2 || fromRate <= 0 || toRate <= 0 || fromRate == toRate)
            return pcm ?? Array.Empty<byte>();

        var inSamples = pcm.Length / 2;
        var outSamples = (int)((long)inSamples * toRate / fromRate);
        if (outSamples <= 0)
            return Array.Empty<byte>();

        var output = new byte[outSamples * 2];
        for (var i = 0; i < outSamples; i++)
        {
            var srcPos = (double)i * fromRate / toRate;
            var i0 = (int)srcPos;
            var i1 = Math.Min(i0 + 1, inSamples - 1);
            var frac = srcPos - i0;
            var s0 = ReadSample(pcm, i0);
            var s1 = ReadSample(pcm, i1);
            var mixed = (short)Math.Round(s0 + (s1 - s0) * frac);
            var o = i * 2;
            output[o] = (byte)(mixed & 0xFF);
            output[o + 1] = (byte)((mixed >> 8) & 0xFF);
        }

        return output;
    }

    private static short ReadSample(byte[] pcm, int index)
    {
        var o = index * 2;
        if (o + 1 >= pcm.Length) return 0;
        return (short)(pcm[o] | (pcm[o + 1] << 8));
    }
}
