using System;
using System.IO;
using System.Text;

namespace GooseDeluxe
{
    /// <summary>
    /// Reads WAV files as they come out of voice tools: PCM 8/16/24/32-bit or 32-bit float, any number of
    /// channels (mixed to mono), and "streaming" headers whose sizes are wrong (0 or 4 GB — some tools write
    /// the header before they know the length; Windows may refuse to play such a file as it is).
    /// </summary>
    internal static class WavFile
    {
        public static float[] Read(string path, out int rate) { return Read(File.ReadAllBytes(path), out rate); }

        public static float[] Read(byte[] b, out int rate)
        {
            rate = 0;
            if (b.Length < 12 || Ascii(b, 0) != "RIFF" || Ascii(b, 8) != "WAVE") throw new InvalidDataException("это не WAV");
            int pos = 12, channels = 1, bits = 16, format = 1;
            bool haveFormat = false;
            while (pos + 8 <= b.Length)
            {
                string id = Ascii(b, pos);
                uint size = BitConverter.ToUInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt " && body + 16 <= b.Length)
                {
                    format = BitConverter.ToUInt16(b, body);
                    channels = Math.Max(1, (int)BitConverter.ToUInt16(b, body + 2));
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (format == 0xFFFE && size >= 26 && body + 26 <= b.Length) format = BitConverter.ToUInt16(b, body + 24); // WAVE_FORMAT_EXTENSIBLE
                    haveFormat = true;
                }
                else if (id == "data")
                {
                    if (!haveFormat || rate <= 0) throw new InvalidDataException("в WAV нет описания формата");
                    long avail = b.Length - body;
                    int n = (int)(size == 0 || size > avail ? avail : size); // a wrong length: everything to the end
                    return Decode(b, body, n, format, channels, bits);
                }
                if (size > (uint)(b.Length - body)) break;
                pos = body + (int)size + (int)(size & 1);
            }
            throw new InvalidDataException("в WAV нет звука");
        }

        private static float[] Decode(byte[] b, int at, int n, int format, int channels, int bits)
        {
            int bytes = bits / 8;
            if (bytes < 1 || bytes > 4 || (format != 1 && format != 3) || (format == 3 && bits != 32))
                throw new InvalidDataException("такой WAV не читаю (формат " + format + ", " + bits + " бит) — сохрани как обычный PCM 16 бит");
            int frame = bytes * channels, frames = n / frame;
            float[] o = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int p = at + f * frame + c * bytes;
                    double v;
                    if (format == 3) v = BitConverter.ToSingle(b, p);
                    else if (bytes == 1) v = (b[p] - 128) / 128.0;
                    else if (bytes == 2) v = BitConverter.ToInt16(b, p) / 32768.0;
                    else if (bytes == 3) v = ((b[p] | (b[p + 1] << 8) | (b[p + 2] << 16)) << 8 >> 8) / 8388608.0;
                    else v = BitConverter.ToInt32(b, p) / 2147483648.0;
                    sum += v;
                }
                double m = sum / channels;
                o[f] = double.IsNaN(m) || double.IsInfinity(m) ? 0f : (float)Math.Max(-1.0, Math.Min(1.0, m));
            }
            return o;
        }

        private static string Ascii(byte[] b, int at) { return at + 4 <= b.Length ? Encoding.ASCII.GetString(b, at, 4) : ""; }
    }
}
