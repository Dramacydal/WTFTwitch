using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using WTFShared.Configuration;

namespace WTFShared.Database
{
    class WTFConnection
    {
        public MySqlConnection Connection;
        public bool IsUsed = false;
    }

    static class DbManager
    {
        private const int _poolSize = 5;

        private static WTFConnection[] _connectionPool = new WTFConnection[_poolSize];

        private static ConcurrentQueue<Tuple<MySqlCommand, Action<ResultRows>>> _tasks = new ConcurrentQueue<Tuple<MySqlCommand, Action<ResultRows>>>();
        private static DbConfig _config;

        static DbManager()
        {
            _config = ConfigLoader<DbConfig>.Load();

            for (var i = 0; i < _poolSize; ++i)
                _connectionPool[i] = new WTFConnection() { Connection = CreateConnection(), IsUsed = false};
        }

        public static WTFConnection GetFreeConnection()
        {
            lock ("123")
            {
                var connection = _connectionPool.FirstOrDefault(_ => _.IsUsed == false);
                if (connection == null)
                    return null;

                connection.IsUsed = true;

                return connection;
            }
        }

        private static MySqlConnection CreateConnection()
        {
             return new MySqlConnection($"Server={_config.Host};Port={_config.Port};database={_config.Database};UID={_config.User};password={_config.Password}");
        }

        public static void AddTask(MySqlCommand command, Action<ResultRows> callback = null)
        {
            _tasks.Enqueue(new Tuple<MySqlCommand, Action<ResultRows>>(command, callback));
        }

        public static void Update()
        {
            while (_tasks.Count > 0)
            {
                var connection = GetFreeConnection();
                if (connection == null)
                    continue;

                if (connection.Connection.State != ConnectionState.Open)
                    connection.Connection.Open();

                connection.IsUsed = true;

                var task = _tasks.Dequeue();
                if (task == null)
                    continue;
                Task.Run(() =>
                {
                    task.Item1.Connection = connection.Connection;
                    using (var reader = task.Item1.ExecuteReader())
                        task.Item2(reader.ReadAll());

                    connection.IsUsed = false;
                });
            }
        }
    }
}
