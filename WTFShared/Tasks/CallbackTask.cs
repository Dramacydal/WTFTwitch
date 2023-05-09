using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WTFShared.Logging;

namespace WTFShared.Tasks
{
    public abstract class CallbackTask : WTFTask
    {
        public CallbackTask(TaskType type, Func<WTFTask, WTFTaskStatus> callback, uint tryCount = 0) : base(type, tryCount)
        {
            this._callback = callback;
        }

        private readonly Func<WTFTask, WTFTaskStatus> _callback;

        protected override WTFTaskStatus DoWork()
        {
            try
            {
                return _callback?.Invoke(this) ?? WTFTaskStatus.Finished;
            }
            catch (Exception e)
            {
                LoggerFactory.Global.Error($"Error executing wtfTask: {e.Message}");
                return WTFTaskStatus.Failed;
            }
        }
    }
}
