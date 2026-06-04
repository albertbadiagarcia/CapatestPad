using System.Collections.Generic;

namespace Capatest.Pad
{
    public interface IPadProtocol
    {
        PAD_Models Model { get; }
        int RelayCount { get; }
        int CounterCount { get; }
        int AnalogInputCount { get; }

        byte[] BuildStatusRequest();
        byte[] BuildConfigureCounters(Counters_Mode mode, int periodCount);
        byte[] BuildConfigureCountersAndRelay(Counters_Mode mode, int periodCount, int relay, int relayCount);
        byte[] BuildSetRelays(IReadOnlyList<RelayStatus> relays);
        byte[] BuildSetRelaysAndOutputVoltage(IReadOnlyList<RelayStatus> relays, double ch0, double ch1);
        byte[] BuildSetAnalogOutput(double ch0, double ch1);
        byte[] BuildStartScope(int channel, int sampling, int interval);
        byte[] BuildStopScope();
        byte[] BuildClearCounters(int mask = 0xFFF);
        byte[] BuildReset();
        byte[] BuildChangeLed(byte r, byte g, byte b, Led_Blink_Modes blinkMode);
        byte[] BuildChangeIp(string ip, int port, int mac1, int mac2);
        byte[] BuildChangeIpWifi(string ip, int port, int mac1, int mac2);

        bool TryParseStatus(byte[] frame, out PadData data);
        bool TryParseScope(byte[] frame, out List<double> samples);
    }
}
