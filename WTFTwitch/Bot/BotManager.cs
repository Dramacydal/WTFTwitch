using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WTFShared;
using WTFShared.Database;
using WTFShared.Logging;

namespace WTFTwitch.Bot
{
    class BotManager
    {
        List<ChatBot> _bots = new List<ChatBot>();

        public BotManager()
        {
            var settings = LoadBotSettings();

            foreach (var setting in settings)
            {
                try
                {
                    _bots.Add(new ChatBot(setting));
                }
                catch (Exception e)
                {
                   Logger.Instance.Error($"Failed to initialize bot: {e.Info()}");
                }
            }
        }

        public void Start()
        {
            foreach (var bot in _bots)
                bot.Start();
        }

        public void Stop()
        {
            foreach (var bot in _bots)
                bot.Stop();
        }

        private List<BotSettings> LoadBotSettings()
        {
            List<BotSettings> botSettings = new List<BotSettings>();

            try
            {
                using (var command = new MySqlCommand("SELECT id, bot_name, client_id, access_token, telegram_token FROM bots", DbConnection.GetConnection()))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            botSettings.Add(new BotSettings()
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                CliendId = reader.GetString(2),
                                AccessToken = reader.GetString(3),
                                TelegramToken = !reader.IsDBNull(4) ? reader.GetString(4) : null
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
