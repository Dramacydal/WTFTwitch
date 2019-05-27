using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using WTFTwitch.Bot.Commands;

namespace WTFTwitch.Bot
{
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
                    Console.WriteLine($"Processor {Channel.Name} Update loop failed: {e.Message}");
                }

                Thread.Sleep(10000);
            }
        }

        private bool CheckIsBroadcasting()
        {
            return _api.V5.Streams.BroadcasterOnlineAsync(Channel.Id).Result;
        }

        private void UpdateChannel()
        {
            var OldOnline = IsBroadcasting;
            IsBroadcasting = CheckIsBroadcasting();

            if (OldOnline != IsBroadcasting)
            {
                var stream = _api.V5.Streams.GetStreamByUserAsync(Channel.Id).Result;
                if (stream.Stream != null)
                {
                    var message = $"{stream.Stream.Channel.Status}\r\n{stream.Stream.Game}";
                    foreach (var notifyChannel in Channel.TelegramNotifyChannels)
                    {
                        var res = Settings.Telegram?.SendPhotoAsync(new ChatId(notifyChannel), new InputOnlineFile(stream.Stream.Preview.Large), message).Result;
                    }
                }
            }
        }

        private void UpdateChannelUserStats()
        {
            if (!IsBroadcasting)
                return;

            var chatters = _api.Undocumented.GetChattersAsync(Channel.Name).Result;
            foreach (var chatter in chatters)
            {
                var userId = _resolveHelper.GetUserIdByName(chatter.Username);
                if (userId == null)
                {
                    Console.WriteLine($"Failed to resolve chatter {chatter.Username}");
                    continue;
                }

                _statisticManager.Update(userId, false);
            }
        }

        public void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            _statisticManager.Update(e.ChatMessage.UserId, false);
            Console.WriteLine($"Received message: {e.ChatMessage.Message} channel: {e.ChatMessage.Channel}");
        }

        public void OnCommand(object sender, OnChatCommandReceivedArgs e)
        {
            _chatCommandHandler.Handle(e.Command);
        }

        public void OnUserLeft(object sender, OnUserLeftArgs e)
        {
            Console.WriteLine($"User left: {e.Username} channel: {Channel.Name}");

            var userId = _resolveHelper.GetUserIdByName(e.Username);
            if (userId == null)
            {
                Console.WriteLine($"Failed to resolve left user {e.Username} for channel {Channel.Name}");
                return;
            }

            _statisticManager.Update(userId, true);
        }

        public void OnUserJoined(object sender, OnUserJoinedArgs e)
        {
            Console.WriteLine($"User joined {e.Username} channel: {Channel.Name}");

            var userId = _resolveHelper.GetUserIdByName(e.Username);
            if (userId == null)
            {
                Console.WriteLine($"Failed to resolve left user {e.Username} for channel {Channel.Name}");
                return;
            }

            _statisticManager.Update(userId, false);
        }

        public void OnJoinedChannel(object sender)
        {
            Console.WriteLine($"Channel joined: {this.Channel}");
        }

        public void OnLeftChannel(object sender)
        {
            Console.WriteLine($"Channel left: {this.Channel}");
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
