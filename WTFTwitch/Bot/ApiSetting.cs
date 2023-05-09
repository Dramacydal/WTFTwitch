using TwitchLib.Api;
using TwitchLib.Api.Core;
using WTFTwitch.HttpClient;

namespace WTFTwitch.Bot
{
    internal class ApiContainer
    {
        public string BotName;
        public string UserId;
        public string ClientId;
        public string AccessToken;

        public TwitchAPI Api;

        public void Refresh()
        {
            Api = new TwitchAPI(settings: new ApiSettings { ClientId = this.ClientId, AccessToken = this.AccessToken }
                // , http: new WTFTwitchHttpClient()
                );
        }
    }
}
