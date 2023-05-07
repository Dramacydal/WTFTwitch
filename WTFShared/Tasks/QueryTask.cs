using System;
using System.Threading;
using MySql.Data.MySqlClient;
using WTFShared.Database;

namespace WTFShared.Tasks
{
    public class QueryTask : CallbackTask
    {
        public QueryTask(MySqlCommand command, uint tryCount = 0, Func<WTFTask, WTFTaskStatus> callback = null) : base(TaskType.Db, callback, tryCount)
        {
            _command = command;
        }

        protected MySqlCommand _command;

        protected override WTFTaskStatus DoWork()
        {
            bool ready = false;
            DbManager.AddTask(_command, rows =>
            {
                Rows = rows;
                ready = true;
            });
            DbManager.Update();

            while (!ready)
            {
                Thread.Sleep(10);
                DbManager.Update();
            }
            
            return base.DoWork();
        }

        public ResultRows Rows { get; private set; }

        public override string ToString()
        {
            return this.GetType().Name;
        }
    }
}
