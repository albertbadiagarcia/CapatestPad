using System;
using System.Collections.Generic;

namespace Capatest.Pad
{
    internal sealed class RelayController
    {
        private readonly RelayStatus[] _relays;
        private readonly object _lock = new object();

        public RelayController(int count = 8)
        {
            _relays = new RelayStatus[count];
        }

        public IReadOnlyList<RelayStatus> Relays
        {
            get { return Snapshot(); }
        }

        public RelayStatus this[int index]
        {
            get
            {
                lock (_lock)
                {
                    return _relays[index];
                }
            }
        }

        public int Count
        {
            get { return _relays.Length; }
        }

        public void Reset()
        {
            lock (_lock)
            {
                for (int i = 0; i < _relays.Length; i++)
                {
                    _relays[i] = RelayStatus.Closed;
                }
            }
        }

        public IReadOnlyList<RelayStatus> ResetAndSnapshot()
        {
            lock (_lock)
            {
                for (int i = 0; i < _relays.Length; i++)
                {
                    _relays[i] = RelayStatus.Closed;
                }
                return (RelayStatus[])_relays.Clone();
            }
        }

        public void Apply(RelayAction[] actions)
        {
            lock (_lock)
            {
                int count = Math.Min(actions.Length, _relays.Length);
                for (int i = 0; i < count; i++)
                {
                    _relays[i] = ApplyAction(actions[i], _relays[i]);
                }
            }
        }

        public IReadOnlyList<RelayStatus> ApplyAndSnapshot(RelayAction[] actions)
        {
            lock (_lock)
            {
                int count = Math.Min(actions.Length, _relays.Length);
                for (int i = 0; i < count; i++)
                {
                    _relays[i] = ApplyAction(actions[i], _relays[i]);
                }
                return (RelayStatus[])_relays.Clone();
            }
        }

        public void DecodeFromBitmask(int digitalOutput)
        {
            lock (_lock)
            {
                for (int i = 0; i < _relays.Length; i++)
                {
                    _relays[i] = (digitalOutput & (1 << i)) != 0 ? RelayStatus.Opened : RelayStatus.Closed;
                }
            }
        }

        private static RelayStatus ApplyAction(RelayAction action, RelayStatus current)
        {
            if (action == RelayAction.Close)
            {
                return RelayStatus.Closed;
            }
            if (action == RelayAction.Open)
            {
                return RelayStatus.Opened;
            }
            return current;
        }

        private IReadOnlyList<RelayStatus> Snapshot()
        {
            lock (_lock)
            {
                return (RelayStatus[])_relays.Clone();
            }
        }
    }
}
