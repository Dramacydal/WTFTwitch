using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using TwitchLib.Api;
using WTFShared;
using WTFShared.Database;

namespace WTFTwitch.Bot
{
    static class ApiPool
    {
        private static readonly List<TwitchAPI> _apiPool = new List<TwitchAPI>();

        static ApiPool()
        {
            ResultRows rows = null;
            using (var command = new MySqlCommand("SELECT client_id, access_token FROM api_pool",
                DbConnection.GetConnection()))
            {
                using (var reader = command.ExecuteReader())
                    rows = reader.ReadAll();
            }

            foreach (var row in rows)
            {
                _apiPool.Add(new TwitchAPI()
                    {Settings = {ClientId = row[0] as string, AccessToken = row[1] as string}});
            }

            ApiEnumerator = ApiEnumerable.GetEnumerator();
        }

        private static IEnumerator<TwitchAPI> ApiEnumerator { get; }

        private static IEnumerable<TwitchAPI> ApiEnumerable
        {
            get
            {
                for (var i = 0;; ++i)
                {
                    if (i >= _apiPool.Count)
                        i = 0;

                    yield return _apiPool[i];
                }
            }
        }

        public static TwitchAPI GetApi()
        {
            ApiEnumerator.MoveNext();

            return ApiEnumerator.Current;
        }
    }
}
