using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using WTFTwitch.Bot;

namespace WTFTwitch.Bot.Commands
{
    abstract class AbstractCommandHandler
    {
        protected ChatBot _bot;
        protected TwitchClient _client => _bot.Client;
        protected WatchedChannel _channel => _bot.Settings.Channel;

        protected AbstractCommandHandler(ChatBot bot)
        {
            this._bot = bot;
        }
    }
}
