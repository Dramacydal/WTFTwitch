using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Design;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text.RegularExpressions;
using NLog.Targets.Wrappers;
using TwitchLib.Api;
using TwitchLib.Api.Core.Extensions.System;
using WTFShared;
using WTFShared.Logging;

namespace WTFTwitch.Bot
{
    class UserInfo
    {
        public UserInfo(string id, string name, string displayName)
        {
            Id = id;
            Name = name;
            DisplayName = displayName;
        }

        public readonly string Id;
        public readonly string Name;
        public readonly string DisplayName;

        public override string ToString()
        {
            return $"'{Id}': '{DisplayName}' ('{Name}')";
        }
    }

    class ResolveHelper
    {
        private TwitchAPI _api { get; }

        private static ConcurrentDictionary<string, UserInfo> _usersById = new ConcurrentDictionary<string, UserInfo>();
        private static ConcurrentDictionary<string, List<UserInfo>> _usersByName = new ConcurrentDictionary<string, List<UserInfo>>();

        public ResolveHelper(TwitchAPI api)
        {
            this._api = api;
        }

        public UserInfo GetUserById(string userId)
        {
            var userName = _usersById.Get(userId);
            if (userName != null)
                return userName;

            try
            {
                var user = _api.V5.Users.GetUserByIDAsync(userId).Result;
                if (user == null)
                    return null;

                var userInfo = new UserInfo(userId, user.Name, user.DisplayName);
                Store(userInfo);

                return userInfo;
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Errors: {e.Info()}");
                return null;
            }
        }

        private void Store(UserInfo info)
        {
            _usersById[info.Id] = info;
            if (!_usersByName.ContainsKey(info.Name.ToLower()))
                _usersByName[info.Name.ToLower()] = new List<UserInfo>();

            if (!_usersByName[info.Name.ToLower()].Any(_ => _.Id == info.Id))
                _usersByName[info.Name.ToLower()].Add(info);
        }

        public List<UserInfo> GetUsersByName(string userName)
        {
            var res = GetUsersByNames(new List<string>() {userName});

            return res[userName.ToLower()];
        }

        public List<UserInfo> Resolve(string nameOrId)
        {
            if (Regex.IsMatch(nameOrId, @"^\d+$"))
            {
                var user = GetUserById(nameOrId);

                return user != null ? new List<UserInfo> {user} : new List<UserInfo>();
            }
            else
                return GetUsersByName(nameOrId);
        }

        public Dictionary<string, List<UserInfo>> GetUsersByNames(List<string> userNames)
        {
            var cached = _usersByName.Where(_ =>
                _.Value.Any(_2 => userNames.Any(_3 => _3.ToLower() == _2.Name.ToLower())));

            var uncached =
                userNames.Where(_ => !cached.Any(_2 => _2.Value.Any(_3 => _3.Name.ToLower() == _.ToLower())));

            var result = new Dictionary<string, List<UserInfo>>();
            foreach (var name in userNames)
                result[name.ToLower()] = new List<UserInfo>();

            if (uncached.Any())
            {
                List<UserInfo> requested = new List<UserInfo>();
                try
                {
                    var apiResult = _api.V5.Users.GetUsersByNameAsync(uncached.ToList());
                    if (apiResult.Result.Total > 0)
                        requested.AddRange(apiResult.Result.Matches.Select(_ => new UserInfo(_.Id, _.Name, _.DisplayName)));
                }
                catch (Exception e)
                {
                    Logger.Instance.Error($"Error : {e.Info()}");
                }

                foreach (var info in requested)
                {
                    Store(info);

                    if (!result.ContainsKey(info.Name.ToLower()))
                        result[info.Name.ToLower()] = new List<UserInfo>();

                    result[info.Name.ToLower()].Add(info);
                }
            }

            foreach (var c in cached)
                result[c.Key] = c.Value;

            foreach (var name in userNames)
            {
                if (!result.ContainsKey(name.ToLower()))
                    result.Add(name, new List<UserInfo>());
            }

            return result;
        }
    }
}
