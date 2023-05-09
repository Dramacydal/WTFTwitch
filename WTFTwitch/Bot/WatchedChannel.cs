namespace WTFTwitch.Bot
{
    class WatchedChannel
    {
        public string ChannelId { get; set; }
        public string ChannelName { get; set; }
        public string TelegramChannel { get; set; }
        public string TelegramToken { get; set; }
        public bool CommandsEnabled { get; set; }
        public bool WhispersEnabled { get; set; }
    }
}