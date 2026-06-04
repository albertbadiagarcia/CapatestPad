using System;
using System.Collections.Generic;

namespace Capatest.Pad
{
    internal static class FastAcquisitionParser
    {
        public static List<List<double>> Parse(byte[] data, int byteCount, int channels)
        {
            List<List<double>> voltages = new List<List<double>>(channels);
            for (int c = 0; c < channels; c++)
            {
                voltages.Add(new List<double>());
            }

            int channel = 0;
            for (int i = 0; i + 1 < byteCount; i += 2)
            {
                int hi = Convert.ToInt32(data[i + 1]);
                double v;
                if (hi > 127)
                {
                    hi -= 128;
                    double raw = (data[i] + 256.0 * hi) * (5.0 / 32768.0);
                    v = (5.0 - raw) * -1.0;
                }
                else
                {
                    v = (data[i] + 256.0 * hi) * (5.0 / 32768.0);
                }

                voltages[channel].Add(v);
                channel = (channel + 1) % channels;
            }

            return voltages;
        }
    }
}
