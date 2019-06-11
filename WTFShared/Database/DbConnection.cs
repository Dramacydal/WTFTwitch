using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WTFShared.Database
{
    public static class DbConnection
    {
        private static ConcurrentDictionary<int, MySqlConnection> _connections = new ConcurrentDictionary<int, MySqlConnection>();

        static DbConnection()
        {
        }

        public static MySqlConnection GetConnection()
        {
            var _connection = _connections.GetOrAdd(Thread.CurrentThread.ManagedThreadId, new MySqlConnection());
            if (_connection.ConnectionString.Length == 0)
            {
                _connection.ConnectionString = $"Server={Settings.Host};database={Settings.Database};UID={Settings.User};password={Settings.Password}";
                _connection.Open();
                _connections[Thread.CurrentThread.ManagedThreadId] = _connection;
            }

            if (_connection.State != ConnectionState.Open)
                _connection.Open();

            return _connection;
        }
    }
}
