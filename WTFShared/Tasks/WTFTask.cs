using System;
using System.Threading;
using WTFShared.Logging;

namespace WTFShared.Tasks
{
    public abstract class WTFTask
    {
        protected WTFTask(TaskType type, uint tryCount)
        {
            Id = Guid.NewGuid();
            Type = type;
            TryCount = tryCount;
        }

        public Guid Id { get; }
        public TaskType Type { get; }

        public DateTime Moment { get; private set; } = DateTime.Now;

        public WTFTaskStatus Status { get; private set; }

        public uint TryCount { get; }

        private uint _currentTryCount = 0;

        public virtual void Reset()
        {
            Status = WTFTaskStatus.None;
        }

        public void Schedule(DateTime when) => Moment = when;

        public void Schedule(TimeSpan delay) => Moment = DateTime.Now.Add(delay);

        public void Abort() => Status = WTFTaskStatus.Abort;

        public WTFTaskStatus Wait(TimeSpan time = default(TimeSpan))
        {
            var till = DateTime.Now;
            if (time.TotalSeconds > 0)
                till = till.Add(time);

            while ((time.TotalSeconds <= 0 ||  DateTime.Now < till) && Status.IsPending())
                Thread.Sleep(50);

            return Status;
        }

        protected abstract WTFTaskStatus DoWork();

        public void SetProcessing()
        {
            Status = WTFTaskStatus.Processing;
        }

        public WTFTaskStatus Execute()
        {
            Status = WTFTaskStatus.Executing;

            ++_currentTryCount;
            try
            {
                Status = DoWork();
            }
            catch (Exception e)
            {
                LoggerFactory.Global.Error($"Task.Execute failed: {e.Message}");
                Status = WTFTaskStatus.Failed;
            }

            if (Status == WTFTaskStatus.Executing)
                Status = WTFTaskStatus.Finished;
            if (Status == WTFTaskStatus.Failed && _currentTryCount < TryCount)
                Status = WTFTaskStatus.Retry;

            return Status;
        }

        public abstract override string ToString();
    }
}