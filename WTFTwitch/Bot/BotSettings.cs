namespace WTFTwitch.Bot
{
    class BotSettings
    {
        public int BotId { get; set; }
        public string BotName { get; set; }
        public WatchedChannel Channel { get; set; }
        public bool Enabled { get; set; }
    }
}
