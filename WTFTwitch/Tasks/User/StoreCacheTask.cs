using System;
using MySql.Data.MySqlClient;
using WTFShared.Tasks;
using WTFTwitch.Bot;

namespace WTFTwitch.Tasks.User
{
    class StoreCacheTask : QueryTask
    {
        private readonly UserInfo _info;

        public StoreCacheTask(UserInfo info, uint tryCount = 0, Func<WTFTask, WTFTaskStatus> callback = null) : base(null, tryCount, callback)
        {
            _info = info;

            _command = new MySqlCommand(
                "INSERT IGNORE INTO user_info (id, name, display_name) VALUE (@id, @name, @display_name)");
            _command.Parameters.AddWithValue("@id", info.Id);
            _command.Parameters.AddWithValue("@name", info.Name);
            _command.Parameters.AddWithValue("@display_name", info.DisplayName);
        }

        public override string ToString()
        {
            return $"{this.GetType().Name} ({_info.ToString()})";
        }
    }
}