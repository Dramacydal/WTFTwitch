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
        protected ResolveHelper _resolveHelper;

        protected BotSettings _settings;
        protected TwitchAPI _api => _settings.Api;
        protected TwitchClient _client => _settings.Client;

        public AbstractCommandHandler(BotSettings settings)
        {
            this._settings = settings;

            this._resolveHelper = new ResolveHelper(_api);
        }
    }
}
