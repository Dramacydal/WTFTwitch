using System;
using System.Collections.Concurrent;
using System.Linq;
using TwitchLib.Api;
using WTFTwitch.Logging;

namespace WTFTwitch.Bot
{
    class ResolveHelper
    {
        private TwitchAPI _api { get; }

        private static ConcurrentDictionary<string, string> _userNamesById = new ConcurrentDictionary<string, string>();
        private static ConcurrentDictionary<string, string> _userIdsByName = new ConcurrentDictionary<string, string>();

        public ResolveHelper(TwitchAPI api)
        {
            this._api = api;
        }

        public string GetUserNameById(string userId)
        {
            var userName = _userNamesById.Get(userId);
            if (userName != null)
                return userName;

            try
            {
                var user = _api.V5.Users.GetUserByIDAsync(userId).Result;
                if (user == null)
                    return null;

                _userNamesById[userId] = user.Name;
                _userIdsByName[user.Name] = userId;

                return user.Name;
            }
            catch(Exception e)
            {
                Logger.Instance.Error($"Errors: {e.Message}");
                return null;
            }
        }

        public string GetUserIdByName(string userName)
        {
            var userId = _userIdsByName.Get(userName);
            if (userId != null)
                return userId;

            try
            {
                var user = _api.V5.Users.GetUserByNameAsync(userName).Result;
                if (user.Matches.Length == 0)
                    return null;

                _userNamesById[user.Matches[0].Name] = userName;
                _userIdsByName[userName] = user.Matches[0].Id;

                return user.Matches[0].Id;
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Errors: {e.Message}");
                return null;
            }
        }
    }
}
