using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using WTFShared.Database;
using WTFShared.Logging;

namespace WTFShared.Tasks
{
    public class QueryTask : CallbackTask
    {
        public QueryTask(MySqlCommand command, uint tryCount = 0, Func<TaskStatus> callback = null) : base(TaskType.General, callback, tryCount)
        {
            _command = command;
        }

        private readonly MySqlCommand _command;

        protected override TaskStatus DoWork()
        {
            try
            {
                _command.Connection = DbConnection.GetConnection();
                using (var reader = _command.ExecuteReader())
                {
                    Rows = reader.ReadAll();
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Failed to execute SQL: {e.Message}");
                return TaskStatus.Failed;
            }

            return base.DoWork();
        }

        public ResultRows Rows { get; private set; }
    }
}
