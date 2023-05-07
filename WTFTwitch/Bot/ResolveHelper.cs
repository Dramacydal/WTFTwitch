using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using WTFShared;
using WTFShared.Database;
using WTFShared.Logging;
using WTFShared.Tasks;
using WTFTwitch.Tasks.User;

namespace WTFTwitch.Bot
{
    class UserInfo
    {
        private readonly User _user;

        public UserInfo(User user)
        {
            this._user = user;

            Id = user.Id;
            Name = user.Login;
            DisplayName = user.DisplayName;
        }

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
            return $"'{DisplayName}' ('{Name}', {Id})";
        }

        public string ToFullString()
        {
            if (_user == null)
                return _user.ToString();

            return $"'{DisplayName}' ('{Name}', {Id}, created: {_user.CreatedAt})";
        }

        public static readonly UserInfo Empty = new UserInfo("<Unknown>", "<Unknown>", "<Unknown>");
    }

    static class ResolveHelper
    {
        private static readonly ConcurrentDictionary<string, UserInfo> UsersById = new ConcurrentDictionary<string, UserInfo>();
        private static readonly ConcurrentDictionary<string, List<UserInfo>> UsersByName = new ConcurrentDictionary<string, List<UserInfo>>();
        private static readonly List<string> BotUsers = new List<string>();

        static ResolveHelper()
        {
            try
            {
                LoadFromStorage();
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Failed to initialize Resolver database: {e.Info()}");
            }
        }

        private static void LoadFromStorage()
        {
            UsersByName.Clear();
            UsersById.Clear();
            using (var query = new MySqlCommand("SELECT id, name, display_name FROM user_info",
                DbConnection.GetConnection()))
            {
                using (var reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var info = new UserInfo(
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetString(2)
                        );

                        UsersById[info.Id] = info;
                        UsersByName.GetOrAdd(info.Name).Add(info);
                    }
                }
            }

            BotUsers.Clear();
            using (var query = new MySqlCommand("SELECT user_id FROM user_ignore_stats",
                DbConnection.GetConnection()))
            {
                using (var reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        BotUsers.Add(reader.GetString(0));
                    }
                }
            }
        }

        public static UserInfo GetUserById(string userId, bool useCache)
        {
            if (useCache)
            {
                var userName = UsersById.Get(userId);
                if (userName != null)
                    return userName;
            }

            try
            {
                var userResult = ApiPool.GetContainer().API.Helix.Users.GetUsersAsync(ids:new List<string> { userId }).Result;
                if (userResult == null || userResult.Users.Length == 0)
                    return null;

                var userInfo = new UserInfo(userResult.Users[0]);
                Store(userInfo);

                return userInfo;
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Errors: {e.Info()}");
                return null;
            }
        }

        private static void Store(UserInfo info)
        {
            UsersById[info.Id] = info;
            if (!UsersByName.ContainsKey(info.Name.ToLower()))
                UsersByName[info.Name.ToLower()] = new List<UserInfo>();

            // ReSharper disable once SimplifyLinqExpression
            if (!UsersByName[info.Name.ToLower()].Any(_ => _.Id == info.Id))
                UsersByName[info.Name.ToLower()].Add(info);

            TaskManager.AddTask(new StoreCacheTask(info));
        }

        public static List<UserInfo> GetUsersByName(string userName, bool useCache)
        {
            var res = GetUsersByNames(new List<string>() {userName}, useCache);

            return res[userName.ToLower()];
        }

        public static List<UserInfo> Resolve(string nameOrId, bool useCache)
        {
            List<UserInfo> result;
            if (Regex.IsMatch(nameOrId, @"^\d+$"))
            {
                var user = GetUserById(nameOrId, useCache);

                result = user != null ? new List<UserInfo> {user} : new List<UserInfo>();
            }
            else
                result = GetUsersByName(nameOrId, useCache);

            if (result.Count > 1)
            {
                Logger.Instance.Debug($"Name or id [{nameOrId}] resolves in more than 1 entities: " +
                                      string.Join(", ", result.Select(_ => _.ToString())));
            }

            return result;
        }

        public static Dictionary<string, List<UserInfo>> GetUsersByNames(IEnumerable<string> userNames, bool useCache)
        {
            var cached = 
                !useCache ? 
                    new List<KeyValuePair<string, List<UserInfo>>>() : UsersByName.Where(_ =>
                _.Value.Any(_2 => userNames.Any(_3 => _3.ToLower() == _2.Name.ToLower())));

            var uncached =
                userNames.Where(_ => !cached.Any(_2 => _2.Value.Any(_3 => _3.Name.ToLower() == _.ToLower())));

            var result = new Dictionary<string, List<UserInfo>>();
            foreach (var name in userNames)
                result[name.ToLower()] = new List<UserInfo>();

            if (uncached.Any())
            {
                List<UserInfo> requested = new List<UserInfo>();
                if (uncached.Count() > 25)
                {
                    var split = uncached.Split(25);

                    foreach (var s in split)
                    {
                        var res = GetUsersByNames(s, useCache);
                        foreach (var r in res)
                            if (r.Value.Count > 0)
                                requested.Add(r.Value[0]);
                    }
                }
                else
                {
                    try
                    {
                        var apiResult2 = ApiPool.GetContainer().API.Helix.Users.GetUsersAsync(logins:uncached.ToList()).Result;
                        if (apiResult2.Users.Length > 0)
                            requested.AddRange(
                                apiResult2.Users.Select(_ => new UserInfo(_)));
                    }
                    catch (Exception e)
                    {
                        Logger.Instance.Error($"Error : {e.Info()}");
                    }
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

        public static bool IsIgnoredUser(string userId)
        {
            return BotUsers.Contains(userId);
        }

        public static void RemoveIgnoreUserStat(UserInfo info)
        {
            BotUsers.Remove(info.Id);

            using (var query = new MySqlCommand("DELETE FROM user_ignore_stats WHERE user_id = @user_id", DbConnection.GetConnection()))
            {
                query.Parameters.AddWithValue("@user_id", info.Id);

                query.ExecuteNonQuery();
            }
        }

        public static void AddIgnoreUserStat(UserInfo info)
        {
            if (!BotUsers.Contains(info.Id))
                BotUsers.Add(info.Id);

            using (var query = new MySqlCommand("REPLACE INTO user_ignore_stats (user_id) VALUE (@user_id)", DbConnection.GetConnection()))
            {
                query.Parameters.AddWithValue("@user_id", info.Id);

                query.ExecuteNonQuery();
            }
        }

        public static string GetInfo(string nameOrId)
        {
            var infos = Resolve(nameOrId, true);
            if (infos.Count == 0)
                return UserInfo.Empty.ToString();

            return infos.First().ToString();
        }
    }
}
