using System;
using MySql.Data.MySqlClient;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using WTFShared.Configuration;

namespace WTFShared.Database
{
    public static class DbConnection
    {
        private static readonly ConcurrentDictionary<int, MySqlConnection> Connections = new ConcurrentDictionary<int, MySqlConnection>();
        private static readonly DbConfig Config;

        static DbConnection()
        {
            Config = ConfigLoader<DbConfig>.Load();
        }

        public static MySqlConnection GetConnection()
        {
            var connection = Connections.GetOrAdd(Thread.CurrentThread.ManagedThreadId);
            if (string.IsNullOrEmpty(connection.ConnectionString))
            {
                connection.ConnectionString =
                    $"Server={Config.Host};Port={Config.Port};database={Config.Database};UID={Config.User};password={Config.Password}";
            }

            if (connection.State != ConnectionState.Open)
                connection.Open();

            return connection;
        }
    }
}
