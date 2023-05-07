using Telegram.Bot;
using TwitchLib.Client;

namespace WTFTwitch.Bot
{
    class BotSettings
    {
        public int Id;
        public string Name;
        public string TelegramToken;

        public int ExplicitChannelId;

        public ChatBot Bot;
        public TwitchClient Client => Bot?.Client;
        public TelegramBotClient Telegram => Bot?.Telegram;
    }
}
