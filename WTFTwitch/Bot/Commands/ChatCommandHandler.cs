﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using WTFShared;
using WTFShared.Database;

namespace WTFTwitch.Bot.Commands
{
    class ChatCommandHandler : AbstractCommandHandler
    {
        private readonly WatchedChannel _channel;
        private readonly ChannelProcessor _processor;

        public ChatCommandHandler(WatchedChannel channel, ChannelProcessor processor) : base(processor.Settings)
        {
            _channel = channel;
            _processor = processor;
        }

        private void SendMessage(string message, params object[] args)
        {
            message = string.Format(message, args);

            if (message.Length > 500)
            {
                var part1 = message.Substring(0, 500);
                var part2 = message.Substring(500);

                var lastSpace = part1.LastIndexOf(' ');
                if (lastSpace != -1)
                {
                    part2 = part1.Substring(lastSpace) + part2;
                    part1 = part1.Substring(0, lastSpace);
                }

                _client.SendMessage(_channel.Name, part1);
                _client.SendMessage(_channel.Name, part2);
                return;
            }

            _client.SendMessage(_channel.Name, message);
        }

        private void SendEmote(string message, params object[] args)
        {
            message = string.Format(message, args);

            SendMessage($"/me {message}");
        }

        public void Handle(ChatCommand command)
        {
            if (!_channel.CommandsEnabled && command.ChatMessage.Username.ToLower() != "zakamurite")
                return;

            switch (command.CommandText.ToLower())
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
                case "resolve":
                    HandleResolveCommand(command);
                    break;
                default:
                    break;
            }
        }

        private void HandleResolveCommand(ChatCommand command)
        {
            if (command.ArgumentsAsList.Count == 0)
                return;

            var nameOrId = command.ArgumentsAsList[0];

            var res = _resolveHelper.Resolve(nameOrId);
            if (res.Count == 0)
            {
                SendMessage($"Failed to resolve user '{nameOrId}'");
                return;
            }

            SendMessage(string.Join(", ", res.Select(_ => _.ToString())));
        }

        private void HandleTTSCommand(ChatCommand command)
        {
            _processor.AddVoiceTask(command.ArgumentsAsString);
        }

        class UserStat
        {
            public UserInfo Info;
            public uint WatchTime;
        }

        private void HandleStatsCommand(ChatCommand command)
        {
            if (command.ArgumentsAsList.Count == 0)
            {
                var cacheKey = $"stats_{_channel.Id}";
                if (!CacheHelper.Load(cacheKey, out string stats) || string.IsNullOrEmpty(stats))
                {
                    List<object[]> data;
                    using (var query = new MySqlCommand("SELECT ucs.user_id, ucs.watch_time FROM user_channel_stats ucs " +
                        "LEFT JOIN user_ignore_stats uis ON uis.user_id = ucs.user_id " +
                        "WHERE ucs.channel_id = @channel_id AND ucs.bot_id = @bot_id AND uis.user_id IS NULL AND ucs.user_id != @channel_id ORDER BY ucs.watch_time DESC LIMIT 10", DbConnection.GetConnection()))
                    {
                        query.Parameters.AddWithValue("@channel_id", _channel.Id);
                        query.Parameters.AddWithValue("@bot_id", _channel.BotId);

                        using (var reader = query.ExecuteReader())
                            data = reader.ReadAll();
                    }

                    var userStats = data.Select(_ => new UserStat()
                    {
                        Info = _resolveHelper.GetUserById(_[0] as string),
                        WatchTime = Convert.ToUInt32(_[1])
                    });

                    var counter = 0;
                    var lines = userStats.OrderByDescending(_ => _.WatchTime).Select(_ => string.Format("{0}. {1} ({2})", ++counter, _.Info.DisplayName, TimeSpan.FromSeconds(_.WatchTime).AsPrettyReadable()));
                    stats = string.Join("\r\n", lines);
                    CacheHelper.Save(cacheKey, stats);
                }

                SendMessage(string.IsNullOrEmpty(stats) ? "No stats available" : stats);
            }
            else
            {
                var userName = command.ArgumentsAsList[0];

                var cacheKey = $"stats_{_channel.Id}_{userName}";
                if (!CacheHelper.Load(cacheKey, out int time) || time == 0)
                {
                    var userInfos = _resolveHelper.Resolve(userName);
                    if (userInfos.Count == 0)
                    {
                        SendMessage($"User {userName} not found");
                        CacheHelper.Save(cacheKey, 0);
                        return;
                    }
                    else if (userInfos.Count > 1)
                    {
                        SendMessage($"User {userName} resolves in more than 1 entities, try specifying id instead: " +
                            string.Join(", ", userInfos.Select(_ => _.ToString())));
                        return;
                    }

                    using (var query = new MySqlCommand("SELECT watch_time FROM user_channel_stats WHERE bot_id = @bot_id AND channel_id = @channel_id AND user_id = @user_id", DbConnection.GetConnection()))
                    {
                        query.Parameters.AddWithValue("@bot_id", _channel.BotId);
                        query.Parameters.AddWithValue("@channel_id", _channel.Id);
                        query.Parameters.AddWithValue("@user_id", userInfos[0].Id);

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
            if (!CacheHelper.Load(cacheKey, out DateTime startDate) || startDate.IsEmpty())
            {
                var res = ApiPool.GetApi().V5.Streams.GetStreamByUserAsync(_channel.Id, "live").Result;
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

            var nameOrId = command.ArgumentsAsList[0];
            var userInfos = _resolveHelper.Resolve(nameOrId);
            if (userInfos.Count == 0)
            {
                SendMessage($"User {nameOrId} not found");
                return;
            }
            else if (userInfos.Count > 1)
            {
                SendMessage($"User {nameOrId} resolves in more than 1 entities, try specifying id instead: " +
                    string.Join(", ", userInfos.Select(_ => _.ToString())));
                return;
            }

            if (command.ArgumentsAsList.Count == 1)
            {
                ResolveHelper.AddIgnoreUserStat(userInfos[0]);
                SendMessage($"Added user '{userInfos[0].ToString()}' to ignore stat list");
            }
            else
            {
                ResolveHelper.RemoveIgnoreUserStat(userInfos[0]);
                SendMessage($"Removed user '{userInfos[0].ToString()}' from ignore stat list");
            }
        }

        private void ListIgnoreStats()
        {
            var data = new ResultRows();
            using (var query = new MySqlCommand("SELECT user_id FROM user_ignore_stats", DbConnection.GetConnection()))
            {
                using (var reader = query.ExecuteReader())
                    data = reader.ReadAll();
            }

            var lines = data.Select(_ =>
            {
                var userId = _[0] as string;
                var userInfo = _resolveHelper.GetUserById(userId);
                return userInfo != null ? userInfo.ToString() : $"'{userId}': <unknown>";
            });

            if (!lines.Any())
            {
                SendMessage("No user stats ignored");
                return;
            }

            SendMessage(string.Join(", ", lines));
        }
    }
}
