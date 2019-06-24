using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using TwitchLib.Api;
using TwitchLib.Api.V5.Models.Streams;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using WTFShared;
using WTFShared.Logging;
using WTFTwitch.Bot.Commands;

namespace WTFTwitch.Bot
{
    using TwitchStream = Stream;

    class ChannelProcessor
    {
        public WatchedChannel Channel { get; }

        public BotSettings Settings { get; }
        private TwitchClient _client => Settings.Client;
        private TwitchAPI _api => Settings.Api;

        private StatisticManager _statisticManager;
        private ChatCommandHandler _chatCommandHandler;

        private ResolveHelper _resolveHelper;

        private CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private Task _updateTask;

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        public bool IsBroadcasting { get; private set; } = false;

        public ChannelProcessor(WatchedChannel channel, BotSettings settings)
        {
            this.Channel = channel;
            this.Settings = settings;

            this._statisticManager = new StatisticManager(channel);
            this._resolveHelper = new ResolveHelper(settings.Api);

            this._chatCommandHandler = new ChatCommandHandler(channel, this);

            IsBroadcasting = CheckIsBroadcasting();

            _updateTask = Task.Run(() => UpdateThread(_updateThreadTokenSource.Token));
        }

        private void UpdateThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UpdateChannel();

                    UpdateChannelUserStats();
                }
                catch (Exception e)
                {
                    Logger.Instance.Error($"Processor {Channel.Name} UpdateThread loop failed: {e.Info()}");
                }

                Thread.Sleep(10000);
            }
        }

        internal void AddVoiceTask(string text)
        {
            throw new NotImplementedException();
        }

        private bool CheckIsBroadcasting()
        {
            return _api.V5.Streams.BroadcasterOnlineAsync(Channel.Id).Result;
        }

        private void UpdateChannel()
        {
            var oldOnline = IsBroadcasting;
            IsBroadcasting = CheckIsBroadcasting();

            if (oldOnline != IsBroadcasting && IsBroadcasting)
            {
                var stream = _api.V5.Streams.GetStreamByUserAsync(Channel.Id).Result;
                if (stream.Stream != null)
                    NotififyOnline(stream.Stream);
            }
        }

        private void NotififyOnline(TwitchStream stream)
        {
            if (Channel.TelegramNotifyChannels.Count == 0)
                return;

            var message = $"{stream.Channel.Status}\r\n{stream.Game}\r\nhttps://www.twitch.tv/{Channel.Name}";

            using (var client = new WebClient())
            {
                using (var file = client.OpenRead(stream.Preview.Large))
                {
                    foreach (var notifyChannel in Channel.TelegramNotifyChannels)
                    {
                        var res = Settings.Telegram?.SendPhotoAsync(new ChatId(notifyChannel), new InputOnlineFile(file), message).Result;
                    }
                }
            }
        }

        private void UpdateChannelUserStats()
        {
            if (!IsBroadcasting)
                return;

            var chatters = _api.Undocumented.GetChattersAsync(Channel.Name).Result;
            if (chatters.Count == 0)
                return;

            var c = _resolveHelper.GetUsersByNames(chatters.Select(_ => _.Username).ToList());
            foreach (var chatter in c)
            {
                if (chatter.Value.Count == 0)
                    Logger.Instance.Warn($"Failed to resolve chatter with name {chatter.Key}");
                else if (chatter.Value.Count > 1)
                {
                    var strEntities = string.Join(", ", chatter.Value.Select(_ => _.ToString()));
                    Logger.Instance.Warn(
                        $"Chatter with name {chatter.Key} resolved in more than 1 entities: {strEntities}");
                }
            }

            var tmp = new List<string>();

            foreach (var e in c)
                tmp.AddRange(e.Value.Select(_ => _.Id));

            _statisticManager.SyncChatters(tmp);
        }

        public void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            _statisticManager.Update(e.ChatMessage.UserId, false);
            Logger.Instance.Info($"Received message: {e.ChatMessage.Message} author: {e.ChatMessage.Username} channel: {e.ChatMessage.Channel}");
        }

        public void OnCommand(object sender, OnChatCommandReceivedArgs e)
        {
            _chatCommandHandler.Handle(e.Command);
        }

        public void OnUserLeft(object sender, OnUserLeftArgs e)
        {
            Logger.Instance.Info($"User left: {e.Username} channel: {Channel.Name}");

            var userInfos = _resolveHelper.GetUsersByName(e.Username);
            if (userInfos.Count == 0)
            {
                Logger.Instance.Warn($"Failed to resolve left user {e.Username} for channel {Channel.Name}");
                return;
            }
            else if (userInfos.Count > 1)
            {
                Logger.Instance.Warn($"Left user {e.Username} for channel {Channel.Name} resolves in more than 1 entities: " +
                    string.Join(", ", userInfos.Select(_ => _.ToString())));
                return;
            }

            _statisticManager.Update(userInfos[0].Id, true);
        }

        public void OnUserJoined(object sender, OnUserJoinedArgs e)
        {
            Logger.Instance.Info($"User joined {e.Username} channel: {Channel.Name}");

            var userInfos = _resolveHelper.GetUsersByName(e.Username);
            if (userInfos.Count == 0)
            {
                Logger.Instance.Warn($"Failed to resolve joined user {e.Username} for channel {Channel.Name}");
                return;
            }
            else if (userInfos.Count > 1)
            {
                Logger.Instance.Warn($"Joined user {e.Username} for channel {Channel.Name} resolves in more than 1 entities: " +
                    string.Join(", ", userInfos.Select(_ => _.ToString())));
                return;
            }

            _statisticManager.Update(userInfos[0].Id, false);
        }

        public void OnJoinedChannel(object sender)
        {
            Logger.Instance.Info($"Channel joined: {this.Channel.Name}");
        }

        public void OnLeftChannel(object sender)
        {
            Logger.Instance.Info($"Channel left: {this.Channel.Name}");
        }

        public void OnExistingUsersDetected(object sender, OnExistingUsersDetectedArgs e)
        {
            var message = $"Existing users: ";
            message += string.Join(", ", e.Users.Take(50));
            if (e.Users.Count > 50)
                message += $" (and {e.Users.Count - 50} more)"; 

            Logger.Instance.Info(message);
        }

        public void Stop(bool async = false)
        {
            _statisticManager.Stop(async);

            _updateThreadTokenSource.Cancel();

            if (!async)
            {
                while (!IsStopped)
                    Thread.Sleep(50);
            }
        }
    }
}
