using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using WTFShared.Logging;
using WTFShared.Tasks;

namespace WTFTwitch.Bot
{
    static class ApiPool
    {
        private static readonly List<ApiContainer> _apiPool = new();
        private static readonly string _poolAccessLock = "reloadLock";
        private static DateTime LastReloadTime { get; set; }
        private static int _lastPoolIndex = 0;

        static ApiPool()
        {
            Reload();
        }
        
        public static void Reload()
        {
            _lastPoolIndex = 0;
            LastReloadTime = DateTime.UtcNow;

            lock (_poolAccessLock)
            {
                var newPool = new List<ApiContainer>();

                for (var i = 0; i < 10; ++i)
                {
                    try
                    {
                        using (var command = new MySqlCommand(@"SELECT
                            p.bot_name AS bot_name,
                            p.user_id AS user_id,
                            p.client_id AS client_id,
                            p.access_token AS access_token
                            FROM api_pool p 
                            WHERE p.enabled = 1"))
                        {
                            var t = new QueryTask(command, 0, (task) =>
                            {
                                try
                                {
                                    foreach (var row in ((QueryTask)task).Rows)
                                    {
                                        var container = new ApiContainer()
                                        {
                                            BotName = row[0] as string,
                                            UserId = row[1] as string,
                                            ClientId = row[2] as string,
                                            AccessToken = row[3] as string,
                                        };
                                        container.Refresh();
                                        newPool.Add(container);
                                    }
                                }
                                catch (Exception e)
                                {
                                    LoggerFactory.Global.Error(e.Message);
                                }

                                return WTFTaskStatus.Finished;
                            });
                            t.Execute();

                            t.Wait();
                        }

                        if (newPool.Count > 0)
                        {
                            _apiPool.Clear();
                            newPool.ForEach(_ => _apiPool.Add(_));
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to reload API pool: {ex.Message}");
                    }
                }
            }
        }

        public static ApiContainer FindByBotName(string name)
        {
            lock (_poolAccessLock)
            {
                foreach (var api in _apiPool)
                {
                    if (api.BotName.ToLower() == name.ToLower())
                        return api;
                }
            }

            return null;
        }


        public static ApiContainer Get()
        {
            if (!Check())
                Reload();

            lock (_poolAccessLock)
            {
                var api = _apiPool[_lastPoolIndex++];
                if (_lastPoolIndex >= _apiPool.Count)
                    _lastPoolIndex = 0;
                return api;
            }
        }

        private static bool Check()
        {
            return _apiPool.Count == 0 || DateTime.UtcNow.Subtract(LastReloadTime).TotalSeconds < 300;
        }

        // private static void CheckCandidate(ApiContainer container)
        // {
        //     var res = container.Api.Auth.ValidateAccessTokenAsync().Result;
        //
        //     if (res == null)
        //         return;
        //
        //     if (res.ExpiresIn > 600)
        //         return;
        //
        //     Logger.Instance.Warn($"API '{container.BotName}' expires in {res.ExpiresIn} secs, refreshing token");
        //
        //     var res3 = container.Api.Auth.RefreshAuthTokenAsync(container.RefreshToken, container.Secret).Result;
        //
        //     container.AccessToken = res3.AccessToken;
        //     container.RefreshToken = res3.RefreshToken;
        //     container.Refresh();
        //
        //     for (int i = 0; i < 10; ++i)
        //     {
        //         try
        //         {
        //             using (var command = new MySqlCommand("UPDATE api_pool " +
        //                     "SET access_token = @access_token, " +
        //                     "refresh_token = @refresh_token, " +
        //                     "expires_at = FROM_UNIXTIME(@expires_at) " +
        //                     "WHERE client_id = @client_id", DbConnection.GetConnection()))
        //             {
        //                 command.Parameters.AddWithValue("@access_token", container.AccessToken);
        //                 command.Parameters.AddWithValue("@refresh_token", container.RefreshToken);
        //                 command.Parameters.AddWithValue("@client_id", container.ClientId);
        //
        //                 var expiration = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds + res3.ExpiresIn;
        //
        //                 command.Parameters.AddWithValue("@expires_at", (uint)expiration);
        //
        //                 if (command.ExecuteNonQuery() > 0)
        //                     break;
        //             }
        //         }
        //         catch (Exception ex)
        //         {
        //             Logger.Instance.Error($"Failed to update access token: {ex.Message}, retrying");
        //         }
        //     }
        //
        //     Logger.Instance.Warn($"API '{container.BotName}' new token expires in {res3.ExpiresIn} secs");
        // }
    }
}
