﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Telegram.Bot;
using TwitchLib.Api;
using TwitchLib.Client;

namespace WTFTwitch.Bot
{
    class BotSettings
    {
        public int Id;
        public string Name;
        public string CliendId;
        public string AccessToken;
        public string TelegramToken;

        public ChatBot Bot;
        public TwitchAPI Api => Bot?.Api;
        public TwitchClient Client => Bot?.Client;
        public TelegramBotClient Telegram => Bot?.Telegram;
    }
}
