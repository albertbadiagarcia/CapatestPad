using System.Collections.Generic;

namespace Capatest.Pad
{
    public class PadData
    {
        public IReadOnlyList<double> AnalogInputs { get; }
        public IReadOnlyList<InputChannelReading> InputChannels { get; }
        public IReadOnlyList<double> Counters { get; }
        public IReadOnlyList<bool> Presences { get; }
        public IReadOnlyList<RelayStatus> Relays { get; }
        public int DigitalInput { get; }
        public int DigitalOutput { get; }
        public uint RemoteCommand { get; }
        public string Error { get; }
        public int ErrorCode { get; }

        public PadData(
            double[] analogInputs,
            int digitalInput,
            int digitalOutput,
            ulong[] counters,
            uint remoteCommand,
            string error = "",
            int errorCode = 0)
        {
            AnalogInputs = analogInputs;
            DigitalInput = digitalInput;
            DigitalOutput = digitalOutput;
            RemoteCommand = remoteCommand;
            Error = error;
            ErrorCode = errorCode;
            Relays = DecodeRelays(digitalOutput);

            bool[] pres = new bool[8];
            for (int i = 0; i < 8; i++)
            {
                pres[i] = (digitalInput & (1 << i)) != 0;
            }
            Presences = pres;

            int n = counters?.Length ?? 0;
            double[] counts = new double[n];
            InputChannelReading[] channels = new InputChannelReading[n];
            for (int i = 0; i < n; i++)
            {
                counts[i] = counters[i];
                channels[i] = new InputChannelReading(pres[i], counters[i]);
            }
            Counters = counts;
            InputChannels = channels;
        }

        public PadData(
            double[] analogInputs,
            int digitalInput,
            int digitalOutput,
            InputChannelReading[] counterChannels,
            uint remoteCommand,
            string error = "",
            int errorCode = 0)
        {
            AnalogInputs = analogInputs;
            DigitalInput = digitalInput;
            DigitalOutput = digitalOutput;
            RemoteCommand = remoteCommand;
            Error = error;
            ErrorCode = errorCode;
            InputChannels = counterChannels ?? new InputChannelReading[0];
            Relays = DecodeRelays(digitalOutput);

            int n = counterChannels?.Length ?? 0;
            double[] counts = new double[n];
            for (int i = 0; i < n; i++)
            {
                counts[i] = counterChannels[i].FrequencyHz > 0
                    ? counterChannels[i].FrequencyHz
                    : counterChannels[i].Count;
            }
            Counters = counts;

            bool[] pres = new bool[8];
            for (int i = 0; i < 8; i++)
            {
                pres[i] = (digitalInput & (1 << i)) != 0;
            }
            Presences = pres;
        }

        public PadData(
            double[] analogInputs,
            int digitalOutput,
            InputChannelReading[] inputChannels,
            uint remoteCommand,
            bool[] channelIsFrequency = null,
            string error = "",
            int errorCode = 0)
        {
            AnalogInputs = analogInputs;
            DigitalOutput = digitalOutput;
            RemoteCommand = remoteCommand;
            Error = error;
            ErrorCode = errorCode;
            InputChannels = inputChannels ?? new InputChannelReading[0];
            Relays = DecodeRelays(digitalOutput);

            int n = inputChannels?.Length ?? 0;
            double[] counts = new double[n];
            bool[] pres = new bool[n];
            int digIn = 0;
            for (int i = 0; i < n; i++)
            {
                bool isFreq = channelIsFrequency != null &&
                              i < channelIsFrequency.Length &&
                              channelIsFrequency[i];
                counts[i] = isFreq
                    ? inputChannels[i].FrequencyHz
                    : inputChannels[i].Count;
                pres[i] = inputChannels[i].Level;
                if (inputChannels[i].Level)
                {
                    digIn |= (1 << i);
                }
            }
            Counters = counts;
            Presences = pres;
            DigitalInput = digIn;
        }

        private static RelayStatus[] DecodeRelays(int digitalOutput)
        {
            RelayStatus[] relays = new RelayStatus[8];
            for (int i = 0; i < 8; i++)
            {
                relays[i] = (digitalOutput & (1 << i)) != 0 ? RelayStatus.Opened : RelayStatus.Closed;
            }
            return relays;
        }
    }
}
