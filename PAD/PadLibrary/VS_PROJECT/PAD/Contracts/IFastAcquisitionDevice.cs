namespace Capatest.Pad
{
    public interface IFastAcquisitionDevice : IPadDevice
    {
        bool IsFastAdcRunning { get; }

        /// <param name="frequency">Sampling frequency in Hz (1000–10000).</param>
        /// <param name="samples">Total samples per channel (-1 = infinite).</param>
        /// <param name="channels">Active channels (1–8). PadRTX2 always uses all 8.</param>
        /// <param name="bufferSize">Block size in samples (max 20480). PadRTX2 ignores this.</param>
        /// <param name="socketType">TCP or UDP. PadRTX2 ignores this (uses serial).</param>
        /// <param name="port">Local port. PadRTX2 ignores this.</param>
        /// <param name="localIp">Local IP to bind. PadRTX2 ignores this.</param>
        bool StartFastAdc(int frequency, int samples, int channels = 8,
                          int bufferSize = 1024, Socket_Type socketType = Socket_Type.TCP,
                          int port = 0, string localIp = "");

        void StopFastAdc();
        void SetFastAdcSamplingRate(int samplingRate);
    }
}
