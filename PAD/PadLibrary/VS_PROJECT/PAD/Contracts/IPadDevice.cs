using System.Collections.Generic;
using System.Net;

namespace Capatest.Pad
{
    /// <summary>
    /// Consumer-facing interface for any PAD device, regardless of transport or model.
    /// </summary>
    public interface IPadDevice
    {
        PAD_Models Model { get; }
        string ConnectionAddress { get; }
        int RelayCount { get; }
        int CounterCount { get; }
        int AnalogInputCount { get; }

        WorkingMode CurrentWorkingMode { get; }
        int StatusRequestIntervalMs { get; }
        IReadOnlyList<RelayStatus> Relays { get; }
        Reconnection_Options Reconnection { get; set; }
        int ReconnectionAttemptsLimit { get; set; }

        event PADDataEventHandler DataChanged;
        event PADStatusChangedEventHandler ConnectionStatusChanged;
        event PADScopeEventHandler ScopeReceived;
        event PADFastAcquisitionEventHandler FastAcquisitionReceived;
        event WrongPADModelEventHandler WrongModelDetected;
        event PADLogEventHandler LogChanged;

        bool Connect(Counters_Mode counterMode, int statusFrequency = 250, int counterPeriodCount = 250);
        bool Disconnect();
        bool IsConnected();
        void SetStatusRequestInterval(int ms);

        void SetRelays(RelayAction[] actions);
        void CloseAllRelays();
        void SetAnalogOutput(double ch0, double ch1);
        void SetRelaysAndOutputVoltage(RelayAction[] actions, double ch0, double ch1);

        bool ConfigureCounters(Counters_Mode mode, int periodCount = 0);
        bool ConfigureCountersAndRelay(Counters_Mode mode, int relay, int relayCount, int periodCount = 0);
        bool ClearCounters(int mask = 0xFFF);

        void StartScope(int channel, int sampling, int interval);
        void StopScope();
        void StartAutoStatus(short ms = 80);
        void StopAutoStatus();
        void StartFastStatus(int channel);
        void StopFastStatus();

        bool Reset();
        void ChangeAddress(string ip, int port = 1999, int mac1 = 0, int mac2 = 0);
        void ChangeAddressWifi(string ip, int port = 1999, int mac1 = 0, int mac2 = 0);
        void SetLed(byte r, byte g, byte b, Led_Blink_Modes blink = Led_Blink_Modes.None);
        IPAddress GetDeviceIp(int serialNumber);
        Dictionary<string, string> DiscoverDevices();
    }
}
