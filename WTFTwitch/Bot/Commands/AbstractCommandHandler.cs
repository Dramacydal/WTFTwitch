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
        protected BotSettings _settings;
        protected TwitchClient _client => _settings.Client;

        protected AbstractCommandHandler(BotSettings settings)
        {
            this._settings = settings;
        }
    }
}
