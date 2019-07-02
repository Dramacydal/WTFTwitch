using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using System.Data;
using System.Threading;
using WTFShared.Configuration;

namespace WTFShared.Database
{
    public static class DbConnection
    {
        private static readonly ConcurrentDictionary<int, MySqlConnection> Connections = new ConcurrentDictionary<int, MySqlConnection>();

        static DbConnection()
        {
        }

        public static MySqlConnection GetConnection()
        {
            var connection = Connections.GetOrAdd(Thread.CurrentThread.ManagedThreadId, new MySqlConnection());
            if (connection.ConnectionString.Length == 0)
            {
                var config = ConfigLoader<DbConfig>.Load();

                connection.ConnectionString = $"Server={config.Host};Port={config.Port};database={config.Database};UID={config.User};password={config.Password}";
                connection.Open();
                Connections[Thread.CurrentThread.ManagedThreadId] = connection;
            }

            if (connection.State != ConnectionState.Open)
                connection.Open();

            return connection;
        }
    }
}
