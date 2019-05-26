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

namespace WTFTwitch.Bot
{
    class ChannelProcessor
    {
        public WatchedChannel Channel { get; }

        private BotSettings _settings;
        private TwitchClient _client => _settings.Client;
        private TwitchAPI _api => _settings.Api;

        private StatisticManager _statisticManager;
        private CommandHandler _commandHandler;

        private ResolveHelper _resolveHelper;

        private CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private Task _updateTask;

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        public bool IsOnline { get; private set; } = false;

        public ChannelProcessor(WatchedChannel channel, BotSettings settings)
        {
            this.Channel = channel;
            this._settings = settings;

            this._statisticManager = new StatisticManager(channel);
            this._commandHandler = new CommandHandler(channel, settings);
            this._resolveHelper = new ResolveHelper(settings.Api);

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

        private void UpdateChannel()
        {
            var OldOnline = IsOnline;
            IsOnline = _api.V5.Streams.BroadcasterOnlineAsync(Channel.Id).Result;

            if (OldOnline != IsOnline)
            {
                var stream = _api.V5.Streams.GetStreamByUserAsync(Channel.Id).Result;
                if (stream.Stream != null)
                {
                    var message = $"{stream.Stream.Channel.Status}\r\n{stream.Stream.Game}";
                    foreach (var notifyChannel in Channel.TelegramNotifyChannels)
                    {
                        var res = _settings.Telegram?.SendPhotoAsync(new ChatId(notifyChannel), new InputOnlineFile(stream.Stream.Preview.Large), message).Result;
                    }
                }
            }
        }

        private void UpdateChannelUserStats()
        {
            if (!IsOnline)
                return;

            var chatters = _api.Undocumented.GetChattersAsync(Channel.Name).Result;
            foreach (var chatter in chatters)
            {
                var userId = _resolveHelper.GetUserByName(chatter.Username);
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
            _commandHandler.Handle(e.Command);
        }

        public void OnUserLeft(object sender, OnUserLeftArgs e)
        {
            Console.WriteLine($"User left: {e.Username} channel: {Channel.Name}");

            var userId = _resolveHelper.GetUserByName(e.Username);
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

            var userId = _resolveHelper.GetUserByName(e.Username);
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
