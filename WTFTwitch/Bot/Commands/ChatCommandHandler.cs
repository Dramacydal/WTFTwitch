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

namespace WTFTwitch.Bot.Commands
{
    class ChatCommandHandler : AbstractCommandHandler
    {
        private WatchedChannel _channel;
        private ChannelProcessor _processor;

        public ChatCommandHandler(WatchedChannel channel, ChannelProcessor processor) : base(processor.Settings)
        {
            _channel = channel;
            _processor = processor;
        }

        private void SendMessage(string message, params object[] args)
        {
            message = string.Format(message, args);

            _client.SendMessage(_channel.Name, message);
        }

        private void SendEmote(string message, params object[] args)
        {
            message = string.Format(message, args);

            SendMessage($"/me {message}");
        }

        public void Handle(ChatCommand command)
        {
            switch (command.CommandText)
            {
                case "stats":
                    HandleStatsCommand(command);
                    break;
                case "uptime":
                    HandleUptimeCommand();
                    break;
                case "ignorestat":
                    HandleIgnoreStatCommand(command);
                    break;
                case "tts":
                    HandleTTSCommand(command);
                    break;
                default:
                    break;
            }
        }

        private void HandleTTSCommand(ChatCommand command)
        {
            _processor.AddVoiceTask(command.ArgumentsAsString);
        }

        class UserStat
        {
            public string UserId;
            public string UserName;
            public int WatchTime;
        }

        private void HandleStatsCommand(ChatCommand command)
        {
            if (command.ArgumentsAsList.Count == 0)
            {
                var cacheKey = $"stats_{_channel.Id}";
                var stats = "";
                if (!CacheHelper.Load(cacheKey, out stats) || string.IsNullOrEmpty(stats))
                {
                    var userStats = new List<UserStat>();

                    using (var query = new MySqlCommand("SELECT ucs.user_id, ucs.watch_time FROM user_channel_stats ucs " +
                        "LEFT JOIN user_ignore_stats uis ON uis.user_id = ucs.user_id " +
                        "WHERE ucs.channel_id = @channel_id AND ucs.bot_id = @bot_id AND uis.user_id IS NULL AND ucs.user_id != @channel_id ORDER BY ucs.watch_time DESC LIMIT 10", DbConnection.GetConnection()))
                    {
                        query.Parameters.AddWithValue("@channel_id", _channel.Id);
                        query.Parameters.AddWithValue("@bot_id", _channel.BotId);

                        using (var reader = query.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                userStats.Add(new UserStat()
                                {
                                    UserId = reader.GetString(0),
                                    UserName = _resolveHelper.GetUserNameById(reader.GetString(0)),
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
                var userName = command.ArgumentsAsList[0];

                var cacheKey = $"stats_{_channel.Id}_{userName}";
                var time = 0;
                if (!CacheHelper.Load(cacheKey, out time) || time == 0)
                {
                    var userId = _resolveHelper.GetUserIdByName(userName);
                    if (userId == null)
                    {
                        SendMessage($"User {userName} not found");
                        CacheHelper.Save(cacheKey, 0);
                        return;
                    }

                    using (var query = new MySqlCommand("SELECT watch_time FROM user_channel_stats WHERE bot_id = @bot_id AND channel_id = @channel_id AND user_id = @user_id", DbConnection.GetConnection()))
                    {
                        query.Parameters.AddWithValue("@bot_id", _channel.BotId);
                        query.Parameters.AddWithValue("@channel_id", _channel.Id);
                        query.Parameters.AddWithValue("@user_id", userId);

                        time = Convert.ToInt32(query.ExecuteScalar());
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
            if (!_processor.IsBroadcasting)
            {
                SendEmote("Stream is offline");
                return;
            }

            var cacheKey = $"uptime_{_channel.Id}";
            DateTime startDate;
            if (!CacheHelper.Load(cacheKey, out startDate) || startDate.IsEmpty())
            {
                var res = _api.V5.Streams.GetStreamByUserAsync(_channel.Id, "live").Result;
                if (res.Stream == null)
                {
                    SendEmote("Uptime info not available");
                    CacheHelper.Save(cacheKey, default(DateTime), TimeSpan.FromMinutes(1));
                    return;
                }

                startDate = res.Stream.CreatedAt;
                CacheHelper.Save(cacheKey, startDate, TimeSpan.FromMinutes(1));
            }

            var diff = DateTime.UtcNow - startDate;
            if (diff.TotalSeconds < 0)
                diff = default(TimeSpan);

            SendEmote("Stream is up for: {0}", diff.AsPrettyReadable());
        }

        private void HandleIgnoreStatCommand(ChatCommand command)
        {
            if (command.ChatMessage.Username.ToLower() != "zakamurite")
                return;

            if (command.ArgumentsAsList.Count < 1)
            {
                ListIgnoreStats();
                return;
            }

            var userName = command.ArgumentsAsList[0];
            var userId = _resolveHelper.GetUserIdByName(userName);
            if (userId == null)
            {
                SendMessage($"User {userName} not found");
                return;
            }

            if (command.ArgumentsAsList.Count == 1)
            {
                AddIgnoreUserStat(userId);
                SendMessage($"Added user '{userName}' to ignore stat list");
            }
            else
            {
                RemoveIgnoreUserStat(userId);
                SendMessage($"Removed user '{userName}' from ignore stat list");
            }
        }

        private void ListIgnoreStats()
        {
            List<string> lines = new List<string>();
            using (var query = new MySqlCommand("SELECT user_id FROM user_ignore_stats", DbConnection.GetConnection()))
            {
                
                using (var reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var userId = reader.GetString(0);

                        var userName = _resolveHelper.GetUserNameById(userId);
                        if (userName != null)
                            lines.Add($"{userName} - {userId}");
                        else
                            lines.Add($"<unknown> - {userId}");
                    }
                }
            }

            if (lines.Count == 0)
            {
                SendMessage("No user stats ignored");
                return;
            }

            SendMessage(string.Join(", ", lines));
        }

        private void RemoveIgnoreUserStat(string userId)
        {
            using (var query = new MySqlCommand("DELETE FROM user_ignore_stats WHERE user_id = @user_id", DbConnection.GetConnection()))
            {
                query.Parameters.AddWithValue("@user_id", userId);

                query.ExecuteNonQuery();
            }
        }

        private void AddIgnoreUserStat(string userId)
        {
            using (var query = new MySqlCommand("REPLACE INTO user_ignore_stats (user_id) VALUE (@user_id)", DbConnection.GetConnection()))
            {
                query.Parameters.AddWithValue("@user_id", userId);

                query.ExecuteNonQuery();
            }
        }
    }
}
