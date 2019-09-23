using System;
using System.Threading;
using Org.BouncyCastle.Crypto.Tls;

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

        public TaskStatus Status { get; private set; }

        public uint TryCount { get; }

        private uint _currentTryCount = 0;

        public virtual void Reset()
        {
            Status = TaskStatus.None;
        }

        public void Schedule(DateTime when) => Moment = when;

        public void Schedule(TimeSpan delay) => Moment = DateTime.Now.Add(delay);

        public void Abort() => Status = TaskStatus.Abort;

        public TaskStatus Wait(TimeSpan time)
        {
            var till = DateTime.Now;
            if (time.TotalSeconds > 0)
                till = till.Add(time);

            while (DateTime.Now < till && Status.IsPending())
                Thread.Sleep(50);

            return Status;
        }

        protected abstract TaskStatus DoWork();

        public void SetProcessing()
        {
            Status = TaskStatus.Processing;
        }

        public TaskStatus Execute()
        {
            Status = TaskStatus.Executing;

            ++_currentTryCount;
            Status = DoWork();

            if (Status == TaskStatus.Executing)
                Status = TaskStatus.Finished;
            if (Status == TaskStatus.Failed && _currentTryCount < TryCount)
                Status = TaskStatus.Retry;

            return Status;
        }
    }
}