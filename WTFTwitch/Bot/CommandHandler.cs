using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using WTFTwitch.Database;

namespace WTFTwitch.Bot
{
    class CommandHandler
    {
        private ResolveHelper _resolveHelper;

        private WatchedChannel _channel;
        private BotSettings _settings;
        private TwitchAPI _api => _settings.Api;
        private TwitchClient _client => _settings.Client;

        public CommandHandler(WatchedChannel channel, BotSettings settings)
        {
            this._channel = channel;
            this._settings = settings;

            this._resolveHelper = new ResolveHelper(_api);
        }

        public void SendMessage(string message, params object[] args)
        {
            message = string.Format(message, args);

            _client.SendMessage(_channel.Name, message);
        }

        public void Handle(ChatCommand command)
        {
            switch (command.CommandText)
            {
                case "stats":
                    HandleStatsCommand(command.ArgumentsAsList);
                    break;
                case "uptime":
                    HandleUptimeCommand();
                    break;
                default:
                    break;
            }
        }

        class UserStat
        {
            public string UserId;
            public string UserName;
            public int WatchTime;
        }

        private void HandleStatsCommand(List<string> arguments)
        {
            if (arguments.Count == 0)
            {
                var cacheKey = $"stats_{_channel.Id}";
                var stats = "";
                if (!CacheHelper.Load(cacheKey, out stats))
                {
                    var userStats = new List<UserStat>();

                    using (var command = new MySqlCommand("SELECT user_id, watch_time FROM user_channel_stats WHERE channel_id = @channel_id AND bot_id = @bot_id ORDER BY watch_time DESC LIMIT 10", DbConnection.GetConnection()))
                    {
                        command.Parameters.AddWithValue("@channel_id", _channel.Id);
                        command.Parameters.AddWithValue("@bot_id", _channel.BotId);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                userStats.Add(new UserStat()
                                {
                                    UserId = reader.GetString(0),
                                    UserName = _resolveHelper.GetUserById(reader.GetString(0)),
                                    WatchTime = reader.GetInt32(1)
                                });
                            }
                        }
                    }

                    var counter = 0;
                    var lines = userStats.OrderByDescending(_ => _.WatchTime).Select(_ => string.Format("{0}. {1} ({2})", ++counter, _.UserName, TimeSpan.FromSeconds(_.WatchTime).AsPrettyReadable()));
                    stats = string.Join("\r\n", lines);
                    CacheHelper.Save(cacheKey, stats);
                }

                if (string.IsNullOrEmpty(stats))
                    SendMessage("No stats available");
                else
                    SendMessage(stats);
            }
            else
            {
                var userName = arguments[0];

                var cacheKey = $"stats_{_channel.Id}_{userName}";
                var time = 0;
                if (!CacheHelper.Load(cacheKey, out time))
                {
                    var userId = _resolveHelper.GetUserByName(userName);
                    if (userId == null)
                    {
                        SendMessage($"User {userName} not found");
                        CacheHelper.Save(cacheKey, 0);
                        return;
                    }

                    using (var command = new MySqlCommand("SELECT watch_time FROM user_channel_stats WHERE bot_id = @bot_id AND channel_id = @channel_id AND user_id = @user_id", DbConnection.GetConnection()))
                    {
                        command.Parameters.AddWithValue("@bot_id", _channel.BotId);
                        command.Parameters.AddWithValue("@channel_id", _channel.Id);
                        command.Parameters.AddWithValue("@user_id", userId);
                        

                        time = Convert.ToInt32(command.ExecuteScalar());
                        CacheHelper.Save(cacheKey, time);
                    }
                }

                if (time > 0)
                    SendMessage($"Total watch time for user '{userName}': {TimeSpan.FromSeconds(Convert.ToInt32(time)).AsPrettyReadable()}");
                else
                    SendMessage($"No statistics for '{userName}'");
            }
        }

        private void HandleUptimeCommand()
        {
            var cacheKey = $"uptime_{_channel.Id}";
            DateTime startDate;
            if (!CacheHelper.Load(cacheKey, out startDate))
            {
                var res = _api.V5.Streams.GetStreamByUserAsync(_channel.Id, "live").Result;
                if (res.Stream == null)
                {
                    SendMessage("Uptime info not available");
                    CacheHelper.Save(cacheKey, default(DateTime), TimeSpan.FromMinutes(1));
                    return;
                }

                startDate = res.Stream.CreatedAt;
                CacheHelper.Save(cacheKey, startDate, TimeSpan.FromMinutes(1));
            }

            SendMessage("Stream is up for: {0}", (DateTime.UtcNow - startDate).AsPrettyReadable());
        }
    }
}
