using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WTFShared.Logging;

namespace WTFShared.Tasks
{
    public class CallbackTask : WTFTask
    {
        public CallbackTask(TaskType type, Func<TaskStatus> callback, uint tryCount = 0) : base(type, tryCount)
        {
            this._callback = callback;
        }

        private readonly Func<TaskStatus> _callback;

        protected override TaskStatus DoWork()
        {
            try
            {
                return _callback?.Invoke() ?? TaskStatus.Finished;
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Error executing wtfTask: {e.Message}");
                return TaskStatus.Failed;
            }
        }
    }
}
