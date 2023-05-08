using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WTFShared.Tasks
{
    public enum WTFTaskStatus
    {
        None,
        Processing,
        Executing,
        Failed,
        Retry,
        Abort,
        Finished,
    }
}
