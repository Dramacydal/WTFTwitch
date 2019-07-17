using System;
using System.Threading;
using WTFShared.Logging;

namespace WTFShared
{
    class WTFTimer
    {
        private Timer _timer = null;
        private TimeSpan _period;
        private TimeSpan _delay;

        public WTFTimer(TimeSpan period, bool autoStart = false, TimeSpan delay = default(TimeSpan))
        {
            _period = period;
            _delay = delay;

            _timer = new Timer((_) => Tick(), null, delay, autoStart ? period : TimeSpan.FromMilliseconds(-1));
        }

        public TimeSpan Period
        {
            get => _period;
            set
            {
                _period = value;
                _timer.Change(_delay, _period);
            }
        }

        public EventHandler OnTick;

        public void Start()
        {
            _timer.Change(_delay, _period);
        }

        public void Stop()
        {
            _timer.Change(_delay, TimeSpan.FromMilliseconds(-1));
        }

        private void Tick()
        {
            try
            {
                if (OnTick != null)
                    OnTick(this, new EventArgs());
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Error while executing timer: {e.Info()}");
            }
        }
    }
}
