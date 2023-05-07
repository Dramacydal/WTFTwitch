using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using WTFShared;
using WTFShared.Database;
using WTFShared.Logging;

namespace WTFTwitch.Bot
{
    class BotManager
    {
        private readonly List<ChatBot> _bots = new List<ChatBot>();

        public BotManager()
        {
        }

        public void LoadSettings(int botId = 0, int channelId = 0)
        {
            var settings = LoadBotSettings(botId);

            foreach (var setting in settings)
            {
                if (botId != 0 && channelId != 0)
                    setting.ExplicitChannelId = channelId;

                try
                {
                    var bot = new ChatBot(setting);
                    _bots.Add(bot);
                }
                catch (Exception e)
                {
                    Logger.Instance.Error($"Failed to initialize bot: {e.Info()}");
                }
            }
        }

        public void Start()
        {
            if (_bots.Count == 0)
                LoadSettings();

            foreach (var bot in _bots)
                bot.Start();
        }

        public void Stop()
        {
            foreach (var bot in _bots)
                bot.Stop();
        }

        private static IEnumerable<BotSettings> LoadBotSettings(int botId = 0)
        {
            List<BotSettings> botSettings = new List<BotSettings>();

            try
            {
                var query = "SELECT id, bot_name, telegram_token FROM bots WHERE ";
                if (botId != 0)
                    query += $"id = {botId}";
                else
                    query += "enabled = 1";

                using (var command = new MySqlCommand(query, DbConnection.GetConnection()))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            botSettings.Add(new BotSettings()
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                TelegramToken = !reader.IsDBNull(2) ? reader.GetString(2) : null,
                            });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Failed to load bot settings: {e.Info()}");
            }

            return botSettings;
        }
    }
}
