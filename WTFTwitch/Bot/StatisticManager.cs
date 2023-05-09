using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WTFShared;
using WTFShared.Database;
using WTFShared.Logging;

namespace WTFTwitch.Bot
{
    class StatisticManager
    {
        private ChatBot _bot;

        private ConcurrentDictionary<string, UserUpdateData> _updateStorage = new ConcurrentDictionary<string, UserUpdateData>();
        private ConcurrentQueue<UserUpdateData> _removedUpdateQueue = new ConcurrentQueue<UserUpdateData>();

        private CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private Task _updateTask;

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        public StatisticManager(ChatBot bot)
        {
            _bot = bot;

            _updateTask = Task.Run(() => UpdateThread(_updateThreadTokenSource.Token));
        }

        public void Update(string userId, bool left)
        {
            if (left && !_updateStorage.ContainsKey(userId))
                return;

            var updateData = _updateStorage.GetOrAdd(userId, new UserUpdateData(userId));
            updateData.UpdateLastSeen(DateTime.UtcNow);

            if (left)
            {
                _updateStorage.Remove(userId);
                _removedUpdateQueue.Enqueue(updateData);
            }
        }

        private void UpdateThread(CancellationToken token)
        {
            for (; !token.IsCancellationRequested; )
            {
                try
                {
                    for (;;)
                    {
                        var updateData = _removedUpdateQueue.Dequeue();
                        if (updateData == null)
                            break;

                        var diff = updateData.LifeTime();
                        if (diff.TotalSeconds > 0)
                        {
                            if (!UpdateUserChannelStats(updateData.UserId, updateData.LastSeen, (int) diff.TotalSeconds, false))
                                _removedUpdateQueue.Enqueue(updateData);
                        }
                    }

                    foreach (var updateData in _updateStorage)
                    {
                        var diff = updateData.Value.LifeTime();
                        if (diff.TotalSeconds <= 0)
                        {
                            _removedUpdateQueue.Enqueue(updateData.Value);
                            continue;
                        }

                        if (UpdateUserChannelStats(updateData.Value.UserId, updateData.Value.LastSeen, (int)diff.TotalSeconds, true))
                            updateData.Value.UpdateLastUpdate();
                    }
                }
                catch(Exception e)
                {
                    _bot.Logger.Error($"Statisticmanager update thread failed: {e.Info()}");
                }

                Thread.Sleep(10000);
            }
        }

        private bool UpdateUserChannelStats(string userId, DateTime lastSeen, int timeDiff, bool isOnline)
        {
            try
            {
                using (var command = new MySqlCommand("INSERT INTO user_channel_stats (bot_id, channel_id, user_id, first_seen, last_seen, watch_time, is_online) VALUES " +
                    "(@bot_id, @channel_id, @user_id, @first_seen, @last_seen, @watch_time, @is_online) ON DUPLICATE KEY " +
                    "UPDATE last_seen = @last_seen, watch_time = watch_time + @watch_time, is_online = @is_online", DbConnection.GetConnection()))
                {
                    command.Parameters.AddWithValue("@bot_id", _bot.Settings.BotId);
                    command.Parameters.AddWithValue("@channel_id", _bot.Settings.Channel.ChannelId);
                    command.Parameters.AddWithValue("@user_id", userId);
                    command.Parameters.AddWithValue("@first_seen", (int)lastSeen.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - timeDiff);
                    command.Parameters.AddWithValue("@last_seen", (int)lastSeen.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);
                    command.Parameters.AddWithValue("@watch_time", timeDiff);
                    command.Parameters.AddWithValue("@is_online", isOnline ? 1 : 0);

                    command.ExecuteNonQuery();

                    return true;
                }
            }
            catch (Exception e)
            {
                _bot.Logger.Error($"Error: {e.Info()}");
                return false;
            }
        }

        public void Stop(bool async = false)
        {
            _updateThreadTokenSource.Cancel();
            if (!async)
            {
                while (!IsStopped)
                    Thread.Sleep(50);
            }
        }

        public void SyncChatters(IEnumerable<string> chatters)
        {
            var missings = _updateStorage.Where(_ => !chatters.Contains(_.Key));
            foreach (var missing in missings)
                Update(missing.Key, true);

            foreach (var chatter in chatters)
                Update(chatter, false);
        }
    }
}
