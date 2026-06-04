using System;
using System.Threading.Tasks;

namespace Capatest.Pad
{
    internal sealed class ReconnectionManager
    {
        private readonly object _syncRoot = new object();
        private bool _reconnecting;
        private int _attempts;
        private bool _connectionRequestedByUser;
        private int _generation;

        public int AttemptsLimit { get; set; } = 5;
        public Reconnection_Options Policy { get; set; } = Reconnection_Options.Till_The_End_Of_Time;

        private readonly Func<bool> _connectAction;
        private readonly Action _deinitializeAction;
        private readonly Action<ConnectionStatus> _updateStatus;
        private readonly Func<bool> _isConnected;
        private readonly Action<Log_Level, string> _log;

        public ReconnectionManager(
            Func<bool> connectAction,
            Action deinitializeAction,
            Action<ConnectionStatus> updateStatus,
            Func<bool> isConnected,
            Action<Log_Level, string> log)
        {
            _connectAction = connectAction;
            _deinitializeAction = deinitializeAction;
            _updateStatus = updateStatus;
            _isConnected = isConnected;
            _log = log;
        }

        public void OnConnectionRequested()
        {
            lock (_syncRoot)
            {
                if (!_connectionRequestedByUser)
                {
                    _generation++;
                }
                _connectionRequestedByUser = true;
            }
        }

        public void OnDisconnectionRequested()
        {
            lock (_syncRoot)
            {
                _connectionRequestedByUser = false;
                _reconnecting = false;
                _generation++;
            }
        }

        public void ResetAttempts()
        {
            lock (_syncRoot)
            {
                _attempts = 0;
            }
        }

        public ConnectionStatus HandleReconnecting()
        {
            int generation;
            bool shouldStartReconnect = false;

            lock (_syncRoot)
            {
                if (!_connectionRequestedByUser)
                {
                    _reconnecting = false;
                    return ConnectionStatus.Disconnected;
                }

                if (!_reconnecting)
                {
                    _reconnecting = true;
                    shouldStartReconnect = true;
                }

                generation = _generation;
            }

            if (shouldStartReconnect)
            {
                _deinitializeAction();
                Reconnect(generation);
            }

            return ConnectionStatus.Reconnecting;
        }

        private void Reconnect(int generation)
        {
            _log(Log_Level.Info, "Reconnect request...");

            bool shouldContinue = false;
            lock (_syncRoot)
            {
                if (!_connectionRequestedByUser || generation != _generation)
                {
                    _reconnecting = false;
                    return;
                }

                switch (Policy)
                {
                    case Reconnection_Options.User_Configured_Limit:
                        if (_attempts < AttemptsLimit)
                        {
                            shouldContinue = true;
                            _attempts++;
                        }
                        break;
                    case Reconnection_Options.Till_The_End_Of_Time:
                        shouldContinue = true;
                        break;
                }
            }

            if (shouldContinue)
            {
                if (_connectAction())
                {
                    lock (_syncRoot)
                    {
                        if (generation == _generation)
                        {
                            _reconnecting = false;
                        }
                    }
                }
                else
                {
                    Task.Delay(2000).ContinueWith(t =>
                    {
                        try
                        {
                            CheckResult(generation);
                        }
                        catch (Exception ex)
                        {
                            _log(Log_Level.Error, $"Reconnect error: {ex.Message}");
                        }
                    });
                }
            }
            else
            {
                lock (_syncRoot)
                {
                    _reconnecting = false;
                }
                _updateStatus(ConnectionStatus.UnableToReconnect);
                _updateStatus(ConnectionStatus.Disconnected);
            }
        }

        private void CheckResult(int generation)
        {
            lock (_syncRoot)
            {
                if (!_connectionRequestedByUser || generation != _generation)
                {
                    _reconnecting = false;
                    return;
                }
            }

            if (_isConnected())
            {
                lock (_syncRoot)
                {
                    if (generation == _generation)
                    {
                        _reconnecting = false;
                    }
                }
                return;
            }

            int attempts;
            lock (_syncRoot)
            {
                attempts = _attempts;
            }
            _log(Log_Level.Info, "Reconnection attempts: " + attempts);
            _deinitializeAction();

            lock (_syncRoot)
            {
                if (!_connectionRequestedByUser || generation != _generation)
                {
                    _reconnecting = false;
                    return;
                }
            }
            Reconnect(generation);
        }
    }
}
