using TwitchLib.Api;
using TwitchLib.Api.Core;

namespace WTFTwitch.Bot
{
    internal class ApiContainer
    {
        public string BotName { get; init; }
        public string UserId { get; init; }
        public string ClientId { get; init; }
        public string AccessToken { get; init; }

        public TwitchAPI Api { get; private set; }

        public void Refresh()
        {
            Api = new TwitchAPI(settings: new ApiSettings { ClientId = this.ClientId, AccessToken = this.AccessToken });
        }
    }
}
