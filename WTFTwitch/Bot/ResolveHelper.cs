using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using WTFShared;
using WTFShared.Database;
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
            return $"'{DisplayName}' ('{Name}', {Id})";
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

        public static UserInfo GetUserById(string userId)
        {
            var userName = UsersById.Get(userId);
            if (userName != null)
                return userName;

            try
            {
                var user = ApiPool.GetApi().V5.Users.GetUserByIDAsync(userId).Result;
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

        private static void Store(UserInfo info)
        {
            UsersById[info.Id] = info;
            if (!UsersByName.ContainsKey(info.Name.ToLower()))
                UsersByName[info.Name.ToLower()] = new List<UserInfo>();

            // ReSharper disable once SimplifyLinqExpression
            if (!UsersByName[info.Name.ToLower()].Any(_ => _.Id == info.Id))
                UsersByName[info.Name.ToLower()].Add(info);

            try
            {
                using (var query =
                    new MySqlCommand(
                        "INSERT IGNORE INTO user_info (id, name, display_name) VALUE (@id, @name, @display_name)",
                        DbConnection.GetConnection()))
                {
                    query.Parameters.AddWithValue("@id", info.Id);
                    query.Parameters.AddWithValue("@name", info.Name);
                    query.Parameters.AddWithValue("@display_name", info.DisplayName);

                    query.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Failed to save to database resolved user {info.ToString()}: {e.Info()}");

            }
        }

        public static List<UserInfo> GetUsersByName(string userName)
        {
            var res = GetUsersByNames(new List<string>() {userName});

            return res[userName.ToLower()];
        }

        public static List<UserInfo> Resolve(string nameOrId)
        {
            List<UserInfo> result;
            if (Regex.IsMatch(nameOrId, @"^\d+$"))
            {
                var user = GetUserById(nameOrId);

                result = user != null ? new List<UserInfo> {user} : new List<UserInfo>();
            }
            else
                result = GetUsersByName(nameOrId);

            Logger.Instance.Debug($"Name or id [{nameOrId}] resolves in more than 1 entities: " +
                string.Join(", ", result.Select(_ => _.ToString())));

            return result;
        }

        public static Dictionary<string, List<UserInfo>> GetUsersByNames(List<string> userNames)
        {
            var cached = UsersByName.Where(_ =>
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
                    var apiResult = ApiPool.GetApi().V5.Users.GetUsersByNameAsync(uncached.ToList());
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
            var infos = Resolve(nameOrId);
            if (infos.Count == 0)
                return UserInfo.Empty.ToString();

            return infos.First().ToString();
        }
    }
}
