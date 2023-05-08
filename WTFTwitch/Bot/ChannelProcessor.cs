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
using Telegram.Bot.Types.InputFiles;
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
        public WatchedChannel Channel { get; }

        public BotSettings Settings { get; }
        private TwitchClient Client => Settings.Client;

        private readonly StatisticManager _statisticManager;
        private readonly ChatCommandHandler _chatCommandHandler;

        private readonly CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private readonly Task _updateTask;
        private bool _needNotify = false;

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        public bool IsBroadcasting { get; private set; } = false;

        public ChannelProcessor(WatchedChannel channel, BotSettings settings)
        {
            this.Channel = channel;
            this.Settings = settings;

            this._statisticManager = new StatisticManager(channel);

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
            var res = ApiPool.GetContainer().Api.Helix.Streams.GetStreamsAsync(userIds: new List<string> { Channel.Id }).Result;
            return res.Streams.Length > 0;
        }

        private void UpdateChannel()
        {
            var oldOnline = IsBroadcasting;
            IsBroadcasting = CheckIsBroadcasting();

            if (_needNotify || IsBroadcasting && oldOnline != IsBroadcasting)
            {
                var streamResult = ApiPool.GetContainer().Api.Helix.Streams.GetStreamsAsync(userIds:new List<string> { Channel.Id }).Result;
                if (streamResult.Streams.Length > 0)
                    NotifyOnline(streamResult.Streams[0]);
            }
            else
                _needNotify = false;
        }

        private void NotifyOnline(TwitchStream stream)
        {
            _needNotify = false;
            if (Channel.TelegramNotifyChannels.Count == 0)
                return;

            var message = $"{stream.Title}\r\n{stream.GameName}\r\nhttps://www.twitch.tv/{Channel.Name}";

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

                    var request = WebRequest.CreateHttp(checkUrl);

                    request.AllowAutoRedirect = false;
                    var response = request.GetResponse() as HttpWebResponse;
                    if (response?.StatusCode == HttpStatusCode.Redirect)
                    {
                        response.Close();
                        continue;
                    }

                    response.GetResponseStream().CopyTo(memoryStream);

                    response?.Close();

                    Logger.Instance.Debug($"Found {width}x{height} preview for channel {Channel.Name}");

                    foreach (var notifyChannel in Channel.TelegramNotifyChannels)
                    {
                        using (var memoryStream2 = new MemoryStream())
                        {
                            var outImage = ResizeImage(memoryStream, width, height);
                            outImage.Save(memoryStream2, ImageFormat.Jpeg);
                            memoryStream2.Position = 0;

                            var res = Settings.Telegram?.SendPhotoAsync(new ChatId(notifyChannel),
                                new InputOnlineFile(memoryStream2), message).Result;
                        }
                    }
                    found = true;

                    break;
                }
            }

            if (!found)
            {
                _needNotify = true;
                Logger.Instance.Warn("Channel previews load failed, postponing online notification");
            }
        }

        private Image ResizeImage(MemoryStream stream, int width, int height)
        {
            var image = Image.FromStream(stream);
            if (width <= 1600)
                return image;

            //return new Bitmap(image, new Size(1600, 900));
            return image.GetThumbnailImage(1600, 900, null, IntPtr.Zero);
        }

        private void UpdateChannelUserStats()
        {
            if (!IsBroadcasting)
            {
                _statisticManager.SyncChatters(new List<string>());
                return;
            }

            var chatters = ApiPool.GetContainer().Api.Undocumented.GetChattersAsync(Channel.Name).Result;
            if (chatters.Count == 0)
                return;

            var c = ResolveHelper.GetUsersByNames(chatters.Select(_ => _.Username).ToList(), true);
            var tmp = new List<string>();
            foreach (var e in c)
                tmp.AddRange(e.Value.Select(_ => _.Id));

            _statisticManager.SyncChatters(tmp);
        }

        public void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            _statisticManager.Update(e.ChatMessage.UserId, false);
            Logger.Instance.Info($"Received message: {e.ChatMessage.Message}, author: {ResolveHelper.GetInfo(e.ChatMessage.UserId)}, channel: {e.ChatMessage.Channel}");
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
                Logger.Instance.Warn($"Failed to resolve joined user {e.Username} for channel {Channel.Name}");
                return;
            }
            else if (userInfos.Count > 1)
            {
                Logger.Instance.Warn($"Joined user {e.Username} for channel {Channel.Name} resolves in more than 1 entities " +
                    string.Join(", ", userInfos.Select(_ => _.ToString())));
                return;
            }

            var info = userInfos.FirstOrDefault(_ => !ResolveHelper.IsIgnoredUser(_.Id));
            if (info == default(UserInfo))
                return;

            Logger.Instance.Info($"User {info} joined channel [{Channel.Name}]");

            _statisticManager.Update(info.Id, false);
        }

        public void OnUserLeft(object sender, OnUserLeftArgs e)
        {
            var userInfos = ResolveHelper.GetUsersByName(e.Username, true);
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

            var info = userInfos.FirstOrDefault(_ => !ResolveHelper.IsIgnoredUser(_.Id));
            if (info == default(UserInfo))
                return;

            Logger.Instance.Info($"User {info} left channel [{Channel.Name}]");

            _statisticManager.Update(info.Id, true);
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
                WaitUntilStopped();
        }

        private void WaitUntilStopped()
        {
            while (!IsStopped)
                Thread.Sleep(50);
        }
    }
}
