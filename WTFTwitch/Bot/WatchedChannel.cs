using System.Collections.Generic;

namespace WTFTwitch.Bot
{
    class WatchedChannel
    {
        public int BotId { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public int InstallDate { get; set; }

        public bool CommandsEnabled { get; set; }

        public List<string> TelegramNotifyChannels = new List<string>();
    }
}