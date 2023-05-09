using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using WTFShared;
using WTFShared.Database;
using WTFShared.Logging;

namespace WTFTwitch.Bot
{
    class BotManager
    {
        private readonly Dictionary<int, Dictionary<string,ChatBot>> _bots = new();
        private Dictionary<int, Dictionary<string, BotSettings>> _settings = new();

        public void LoadSettings(bool force = false)
        {
            if (!force && _settings.Count > 0)
                return;

            Stop();

            _bots.Clear();

            _settings = LoadBotSettings();
        }

        public ChatBot GetBot(int id, string channelId)
        {
            LoadSettings();

            if (_bots.ContainsKey(id) && _bots[id].ContainsKey(channelId))
                return _bots[id][channelId];

            if (_settings.ContainsKey(id) && _settings[id].ContainsKey(channelId))
            {
                var bot = new ChatBot(_settings[id][channelId]);
                if (!_bots.ContainsKey(id))
                    _bots[id] = new();
                _bots[id][channelId] = bot;
                return bot;
            }

            return null;
        }

        public void Start()
        {
            LoadSettings();

            foreach (var (botId, byBotSettings) in _settings)
            {
                foreach (var (channelId, settings) in byBotSettings)
                {
                    if (!settings.Enabled)
                        continue;

                    GetBot(botId, channelId)?.Start();
                }
            }
        }

        public void Stop()
        {
            foreach (var (botId, botInstances) in _bots)
            {
                foreach (var (channelId, bot) in botInstances)
                    bot.Stop();
            }
        }

        private static Dictionary<int, Dictionary<string, BotSettings>> LoadBotSettings()
        {
            Dictionary<int, Dictionary<string, BotSettings>> botSettings = new();

            try
            {
                var query = "SELECT b.id AS bot_id, b.bot_name AS bot_name, b.enabled AS bot_enabled , " +
                            "bwc.channel_id AS watched_channel_id, bwc.commands_enabled AS commands_enabled, bwc.telegram_channel AS telegram_channel, " +
                            "bwc.telegram_token AS telegram_token, bwc.enabled AS watch_enabled " +
                            "FROM bots b " +
                            "JOIN bot_watched_channels bwc ON bwc.bot_id = b.id";

                using var command = new MySqlCommand(query, DbConnection.GetConnection());
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt32("bot_id");
                    var watchedChannelId = reader.GetString("watched_channel_id");
                    if (!botSettings.ContainsKey(id))
                        botSettings[id] = new();

                    var settings = new BotSettings()
                    {
                        BotId = id,
                        BotName = reader.GetString("bot_name"),
                        Channel = new WatchedChannel()
                        {
                            ChannelId = watchedChannelId,
                            CommandsEnabled = reader.GetInt32("commands_enabled") != 0,
                            TelegramChannel = reader.GetString("telegram_channel"),
                            TelegramToken = reader.GetString("telegram_token"),
                        },
                        Enabled = reader.GetInt32("bot_enabled") != 0 && reader.GetInt32("watch_enabled") != 0,
                    };

                    if (!ResolveChannel(settings.Channel))
                    {
                        LoggerFactory.Global.Error($"Failed to resolve watched channel {settings.Channel.ChannelId}");
                        continue;
                    }

                    botSettings[id][watchedChannelId] = settings;
                }
            }
            catch (Exception e)
            {
                LoggerFactory.Global.Error($"Failed to load bot settings: {e.Info()}");
            }

            return botSettings;
        }

        private static bool ResolveChannel(WatchedChannel channel)
        {
            var resolveResult = ApiPool.Get().Api.Helix.Users
                .GetUsersAsync(new() { channel.ChannelId }).Result;
            if (resolveResult.Users.Length == 0)
                return false;

            channel.ChannelName = resolveResult.Users.First().Login.ToLower();

            return true;
        }
    }
}
