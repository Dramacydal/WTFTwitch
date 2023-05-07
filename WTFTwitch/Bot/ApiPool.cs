using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using WTFShared.Database;
using WTFShared.Logging;
using WTFShared.Tasks;

namespace WTFTwitch.Bot
{
    static class ApiPool
    {
        private static SynchronizedCollection<ApiContainer> _apiPool = new SynchronizedCollection<ApiContainer>();
        private static string reloadLock = "reloadLock";
        private static DateTime lastReloadTime { get; set; }


        static ApiPool()
        {
            Reload();
            ApiEnumerator = ApiEnumerable.GetEnumerator();
        }
        public static void Reload()
        {
            lastReloadTime = DateTime.UtcNow;

            lock (reloadLock)
            {
                var newPool = new List<ApiContainer>();

                for (var i = 0; i < 10; ++i)
                {
                    try
                    {
                        using (var command = new MySqlCommand(@"SELECT
                            p.bot_name AS bot_name,
                            p.client_id AS client_id,
                            p.access_token AS access_token,
                            a.secret AS secret,
                            p.refresh_token AS refresh_token
                            FROM api_pool p 
                            JOIN apps a ON a.client_id = p.client_id
                            WHERE p.enabled = 1"))
                        {
                            if (true)
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
                                                ClientId = row[1] as string,
                                                AccessToken = row[2] as string,
                                                Secret = row[3] as string,
                                                RefreshToken = row[4] as string,
                                            };
                                            container.Refresh();
                                            newPool.Add(container);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Logger.Instance.Error(e.Message);
                                    }

                                    return WTFTaskStatus.Finished;
                                });
                                t.Execute();

                                t.Wait();
                            }
                            else
                            {
                                var t = TaskManager.AddTask(new QueryTask(command, 0, (task) =>
                                {
                                    try
                                    {
                                        foreach (var row in ((QueryTask)task).Rows)
                                        {
                                            var container = new ApiContainer()
                                            {
                                                BotName = row[0] as string,
                                                ClientId = row[1] as string,
                                                AccessToken = row[2] as string,
                                                Secret = row[3] as string,
                                                RefreshToken = row[4] as string,
                                            };
                                            container.Refresh();
                                            _apiPool.Add(container);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Logger.Instance.Error(e.Message);
                                    }

                                    return WTFTaskStatus.Finished;
                                }));
                                t.Wait();
                                Console.WriteLine(1);
                            }
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
            foreach (var container in _apiPool)
            {
                if (container.BotName.ToLower() == name.ToLower())
                    return container;
            }

            return null;
        }

        private static IEnumerator<ApiContainer> ApiEnumerator { get; }

        private static IEnumerable<ApiContainer> ApiEnumerable
        {
            get
            {
                for (var i = 0; ; ++i)
                {
                    if (DateTime.UtcNow.Subtract(lastReloadTime).TotalSeconds > 300)
                    {
                        i = 0;
                        Reload();
                    }
                    else if (i >= _apiPool.Count)
                        i = 0;

                    var candidate = _apiPool[i];
                    //CheckCandidate(candidate);

                    yield return candidate;
                }
            }
        }

        public static ApiContainer GetContainer()
        {
            lock (reloadLock)
            {
                ApiEnumerator.MoveNext();

                return ApiEnumerator.Current;
            }
        }

        private static void CheckCandidate(ApiContainer container)
        {
            var res = container.API.Auth.ValidateAccessTokenAsync().Result;

            if (res == null)
                return;

            if (res.ExpiresIn > 600)
                return;

            Logger.Instance.Warn($"API '{container.BotName}' expires in {res.ExpiresIn} secs, refreshing token");

            var res3 = container.API.Auth.RefreshAuthTokenAsync(container.RefreshToken, container.Secret).Result;

            container.AccessToken = res3.AccessToken;
            container.RefreshToken = res3.RefreshToken;
            container.Refresh();

            for (int i = 0; i < 10; ++i)
            {
                try
                {
                    using (var command = new MySqlCommand("UPDATE api_pool " +
                            "SET access_token = @access_token, " +
                            "refresh_token = @refresh_token, " +
                            "expires_at = FROM_UNIXTIME(@expires_at) " +
                            "WHERE client_id = @client_id", DbConnection.GetConnection()))
                    {
                        command.Parameters.AddWithValue("@access_token", container.AccessToken);
                        command.Parameters.AddWithValue("@refresh_token", container.RefreshToken);
                        command.Parameters.AddWithValue("@client_id", container.ClientId);

                        var expiration = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds + res3.ExpiresIn;

                        command.Parameters.AddWithValue("@expires_at", (uint)expiration);

                        if (command.ExecuteNonQuery() > 0)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.Error($"Failed to update access token: {ex.Message}, retrying");
                }
            }

            Logger.Instance.Warn($"API '{container.BotName}' new token expires in {res3.ExpiresIn} secs");
        }
    }
}
