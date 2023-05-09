using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TwitchLib.Api.Helix.Models.Chat.GetChatters;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using WTFShared;
using WTFShared.Logging;
using WTFTwitch.Bot.Commands;

namespace WTFTwitch.Bot
{
    using TwitchStream = TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream;

    class ChannelProcessor
    {
        private ChatBot _bot;

        public WatchedChannel Channel => _bot.Settings.Channel;

        public BotSettings Settings => _bot.Settings;
        
        private TwitchClient Client => _bot.Client;

        private readonly StatisticManager _statisticManager;
        private readonly ChatCommandHandler _chatCommandHandler;

        private readonly CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private readonly Task _updateTask;
        private bool _needNotify = false;
        private TelegramBotClient _telegram;

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        public bool IsBroadcasting { get; private set; } = false;

        public ChannelProcessor(ChatBot bot)
        {
            _bot = bot;
            _statisticManager = new StatisticManager(bot);
            _chatCommandHandler = new ChatCommandHandler(bot, this);

            IsBroadcasting = CheckIsBroadcasting();

            _updateTask = Task.Run(() => UpdateThread(_updateThreadTokenSource.Token));

            if (!string.IsNullOrEmpty(Settings.Channel.TelegramToken))
                _telegram = new TelegramBotClient(Settings.Channel.TelegramToken);
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
                    _bot.Logger.Error($"Processor {Channel.ChannelName} UpdateThread loop failed: {e.Info()}");
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
            var res = ApiPool.Get().Api.Helix.Streams.GetStreamsAsync(userIds: new List<string> { Channel.ChannelId }).Result;
            return res.Streams.Length > 0;
        }

        private void UpdateChannel()
        {
            var oldOnline = IsBroadcasting;
            IsBroadcasting = CheckIsBroadcasting();

            if (_needNotify || IsBroadcasting && oldOnline != IsBroadcasting)
            {
                try
                {
                    NotifyOnline();
                }
                catch (PreviewNotReadyException)
                {
                    _needNotify = true;
                    _bot.Logger.Warn("Channel previews load failed, postponing online notification");
                }
                catch (Exception e)
                {
                    _bot.Logger.Error($"Online notification failed: {e.Message}");
                }
            }
            else
                _needNotify = false;
        }

        public void NotifyOnline(string message = "", bool force = false)
        {
            _needNotify = false;
            
            if (string.IsNullOrEmpty(Channel.TelegramChannel))
                return;
            
            var streamResult = ApiPool.Get().Api.Helix.Streams.GetStreamsAsync(userIds:new List<string> { Channel.ChannelId }).Result;
            if (streamResult.Streams.Length == 0)
                return;

            var stream = streamResult.Streams[0];

            if (string.IsNullOrEmpty(message))
                message = $"{stream.Title}";
            message += $"\r\n{stream.GameName}\r\nhttps://www.twitch.tv/{Channel.ChannelName}";

            var priorityDimensions = new List<ValueTuple<int,int>>
            {
                (1920,1080), // telegram compresses pictures this size too much, no reason to use this dimension
                (1600,900),
                (1366,768),
                (1280,720),
                (1152,648),
                (1024,576),
            };

            var found = false;
            using (var memoryStream = new MemoryStream())
            {
                foreach (var (width, height) in priorityDimensions)
                {
                    var checkUrl = stream.ThumbnailUrl.Replace("{width}", width.ToString())
                        .Replace("{height}", height.ToString());

                    var client = new System.Net.Http.HttpClient();
                    var response = client.GetAsync(new Uri(checkUrl)).Result;

                    // var response = request.GetResponse() as HttpWebResponse;
                    if (response.StatusCode != HttpStatusCode.OK)
                        continue;

                    response.Content.ReadAsStream().CopyTo(memoryStream);

                    _bot.Logger.Debug($"Found {width}x{height} preview for channel {Channel.ChannelName}");

                    using (var memoryStream2 = new MemoryStream())
                    {
                        var outImage = ResizeImage(memoryStream, width, height);
                        outImage.Save(memoryStream2, ImageFormat.Jpeg);
                        memoryStream2.Position = 0;
                        
                        var res = _telegram?.SendPhotoAsync(new ChatId(Settings.Channel.TelegramChannel),
                            new InputFileStream(memoryStream2), null, message).Result;
                    }
                    found = true;

                    break;
                }
            }

            if (!found)
                throw new PreviewNotReadyException();
        }

        private Image ResizeImage(MemoryStream stream, int width, int height)
        {
            var image = Image.FromStream(stream);
            if (width <= 1600)
                return image;

            //return new Bitmap(image, new Size(1600, 900));
            return image.GetThumbnailImage(1600, 900, null, IntPtr.Zero);
        }

        private List<Chatter> GetChatters()
        {
            List<Chatter> chatters = new();
            for (string after = null;;)
            {
                var api = ApiPool.Get();

                var chattersResult = api.Api.Helix.Chat
                    .GetChattersAsync(Channel.ChannelId, api.UserId, 1000, after).Result;
                if (chattersResult.Total == 0)
                    break;

                chatters.AddRange(chattersResult.Data);
                after = chattersResult.Pagination.Cursor;
                if (string.IsNullOrEmpty(after))
                    break;
            }

            return chatters;
        }

        private void UpdateChannelUserStats()
        {
            if (!IsBroadcasting)
            {
                _statisticManager.SyncChatters(new List<string>());
                return;
            }

            var chatters = GetChatters();
            if (chatters.Count == 0)
                return;

            _statisticManager.SyncChatters(chatters.Select(_ => _.UserId));
        }

        public void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            _statisticManager.Update(e.ChatMessage.UserId, false);
            _bot.Logger.Info($"Received message: {e.ChatMessage.Message}, author: {ResolveHelper.GetInfo(e.ChatMessage.UserId)}, channel: {e.ChatMessage.Channel}");
        }

        public void OnCommand(object sender, OnChatCommandReceivedArgs e)
        {
            _chatCommandHandler.Handle(e.Command);
        }

        public void OnUserJoined(object sender, OnUserJoinedArgs e)
        {
            var userInfos = ResolveHelper.GetUsersByName(e.Username, true);
            if (userInfos.Count == 0)
            {
                _bot.Logger.Warn($"Failed to resolve joined user {e.Username} for channel {Channel.ChannelName}");
                return;
            }
            else if (userInfos.Count > 1)
            {
                _bot.Logger.Warn($"Joined user {e.Username} for channel {Channel.ChannelName} resolves in more than 1 entities " +
                    string.Join(", ", userInfos.Select(_ => _.ToString())));
                return;
            }

            var info = userInfos.FirstOrDefault(_ => !ResolveHelper.IsIgnoredUser(_.Id));
            if (info == default(UserInfo))
                return;

            _bot.Logger.Info($"User {info} joined channel [{Channel.ChannelName}]");

            _statisticManager.Update(info.Id, false);
        }

        public void OnUserLeft(object sender, OnUserLeftArgs e)
        {
            var userInfos = ResolveHelper.GetUsersByName(e.Username, true);
            if (userInfos.Count == 0)
            {
                _bot.Logger.Warn($"Failed to resolve left user {e.Username} for channel {Channel.ChannelName}");
                return;
            }
            else if (userInfos.Count > 1)
            {
                _bot.Logger.Warn($"Left user {e.Username} for channel {Channel.ChannelName} resolves in more than 1 entities: " +
                    string.Join(", ", userInfos.Select(_ => _.ToString())));
                return;
            }

            var info = userInfos.FirstOrDefault(_ => !ResolveHelper.IsIgnoredUser(_.Id));
            if (info == default(UserInfo))
                return;

            _bot.Logger.Info($"User {info} left channel [{Channel.ChannelName}]");

            _statisticManager.Update(info.Id, true);
        }

        public void OnJoinedChannel(object sender)
        {
            _bot.Logger.Info($"Channel joined: {this.Channel.ChannelName}");
        }

        public void OnLeftChannel(object sender)
        {
            _bot.Logger.Info($"Channel left: {this.Channel.ChannelName}");
        }

        public void OnExistingUsersDetected(object sender, OnExistingUsersDetectedArgs e)
        {
            var message = $"Existing users: ";
            message += string.Join(", ", e.Users.Take(50));
            if (e.Users.Count > 50)
                message += $" (and {e.Users.Count - 50} more)"; 

            _bot.Logger.Info(message);
        }

        public void Stop(bool async = false)
        {
            _statisticManager.Stop(async);

            _updateThreadTokenSource.Cancel();

            if (!async)
                WaitUntilStopped();
        }

        private void WaitUntilStopped()
        {
            while (!IsStopped)
                Thread.Sleep(50);
        }
    }
}
