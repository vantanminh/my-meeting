namespace MeetingAssistant.Services;

/// <summary>
/// Turns a WASAPI buffer into a 0–1 meter reading. Capture devices commonly
/// deliver 32-bit float; treating those bytes as 16-bit PCM pins the meter.
/// </summary>
public static class AudioLevelMeter
{
    public static double FromBuffer(byte[] buffer, int count, int bitsPerSample)
    {
        if (buffer is null || count < 2) return 0;

        double sum = 0;
        var samples = 0;
        if (bitsPerSample >= 32)
        {
            var limit = Math.Min(count, buffer.Length) - 3;
            for (var index = 0; index <= limit; index += 4)
            {
                var sample = BitConverter.ToSingle(buffer, index);
                if (float.IsFinite(sample))
                    sum += sample * sample;
                samples++;
            }
        }
        else
        {
            var limit = Math.Min(count, buffer.Length) - 1;
            for (var index = 0; index <= limit; index += 2)
            {
                var sample = (short)(buffer[index] | (buffer[index + 1] << 8));
                var normalized = sample / 32768d;
                sum += normalized * normalized;
                samples++;
            }
        }

        if (samples == 0) return 0;
        var rms = Math.Sqrt(sum / samples);
        var decibels = 20 * Math.Log10(Math.Max(rms, 1e-7));
        var normalizedLevel = (decibels + 60d) / 60d;
        return Math.Clamp(normalizedLevel, 0, 1);
    }

    public static double Smooth(double current, double target)
    {
        var rate = target >= current ? 0.62 : 0.18;
        return current + ((target - current) * rate);
    }
}
