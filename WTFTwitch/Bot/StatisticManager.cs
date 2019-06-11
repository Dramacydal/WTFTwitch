using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WTFShared.Database;
using WTFShared.Logging;

namespace WTFTwitch.Bot
{
    class StatisticManager
    {
        public WatchedChannel Channel { get; }

        private ConcurrentDictionary<string, UserUpdateData> _updateStorage = new ConcurrentDictionary<string, UserUpdateData>();
        private ConcurrentQueue<UserUpdateData> _removedUpdateQueue = new ConcurrentQueue<UserUpdateData>();

        private CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private Task _updateTask;

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        public StatisticManager(WatchedChannel channel)
        {
            Channel = channel;

            _updateTask = Task.Run(() => UpdateThread(_updateThreadTokenSource.Token));
        }

        public void Update(string userId, bool left)
        {
            var updateData = _updateStorage.GetOrAdd(userId, new UserUpdateData(userId));
            updateData.Update();

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
                    for (; ; )
                    {
                        var updateData = _removedUpdateQueue.Dequeue();
                        if (updateData == null)
                            break;

                        var diff = updateData.GetLifetime();
                        UpdateUserChannelStats(updateData.UserId, updateData.LastSeen, (int)diff.TotalSeconds, false);
                    }

                    foreach (var updateData in _updateStorage)
                    {
                        var diff = updateData.Value.GetLifetime();
                        if (diff.TotalSeconds <= 0)
                            continue;

                        UpdateUserChannelStats(updateData.Value.UserId, updateData.Value.LastSeen, (int)diff.TotalSeconds, true);
                        updateData.Value.Reset();
                    }
                }
                catch(Exception e)
                {
                    Logger.Instance.Error($"Statisticmanager update thread failed: {e.Message}");
                }

                Thread.Sleep(10000);
            }
        }

        private void UpdateUserChannelStats(string userId, DateTime lastSeen, int timeDiff, bool isOnline)
        {
            try
            {
                using (var command = new MySqlCommand("INSERT INTO user_channel_stats (bot_id, channel_id, user_id, first_seen, last_seen, watch_time, is_online) VALUES " +
                    "(@bot_id, @channel_id, @user_id, @first_seen, @last_seen, @watch_time, @is_online) ON DUPLICATE KEY " +
                    "UPDATE last_seen = @last_seen, watch_time = watch_time + @watch_time, is_online = @is_online", DbConnection.GetConnection()))
                {
                    command.Parameters.AddWithValue("@bot_id", Channel.BotId);
                    command.Parameters.AddWithValue("@channel_id", Channel.Id);
                    command.Parameters.AddWithValue("@user_id", userId);
                    command.Parameters.AddWithValue("@first_seen", (int)lastSeen.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - timeDiff);
                    command.Parameters.AddWithValue("@last_seen", (int)lastSeen.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);
                    command.Parameters.AddWithValue("@watch_time", timeDiff);
                    command.Parameters.AddWithValue("@is_online", isOnline ? 1 : 0);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Error: {e.Message}");
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
    }
}
