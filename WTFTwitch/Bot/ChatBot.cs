using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using WTFShared.Database;
using WTFShared.Logging;
using WTFTwitch.Bot.Commands;

namespace WTFTwitch.Bot
{
    class ChatBot
    {
        public TwitchAPI Api { get; private set; }
        public TwitchClient Client { get; private set; }

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        private BotSettings _settings;

        private List<WatchedChannel> _watchedChannels = new List<WatchedChannel>();
        private Dictionary<string, ChannelProcessor> _channelProcessors = new Dictionary<string, ChannelProcessor>();

        private WhisperCommandHandler _whisperCommandHandler;

        public TelegramBotClient Telegram { get; private set; }

        public string BotName => Client.TwitchUsername;

        private bool _isStopping = false;

        private CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private Task _updateTask;

        public ChatBot(BotSettings settings)
        {
            this._settings = settings;
            settings.Bot = this;

            InitializeAPI();
            InitializeClient();
            InitializeTelegram();

            _whisperCommandHandler = new WhisperCommandHandler(_settings);
        }

        private void InitializeAPI()
        {
            Api = new TwitchAPI();
            Api.Settings.ClientId = _settings.CliendId;
            Api.Settings.AccessToken = _settings.AccessToken;
        }

        private void InitializeClient()
        {
            var credentials = new ConnectionCredentials(_settings.Name, _settings.AccessToken);

            Client = new TwitchClient();
            Client.Initialize(credentials);

            Client.OnLog += Client_OnLog;
            Client.OnConnected += Client_OnConnected;
            Client.OnDisconnected += Client_OnDisconnected;
            Client.OnConnectionError += Client_OnConnectionError;
            Client.OnReconnected += Client_OnReconnected;
            Client.OnError += Client_OnError;

            Client.OnJoinedChannel += Client_OnJoinedChannel;
            Client.OnLeftChannel += Client_OnLeftChannel;

            Client.OnUserJoined += Client_OnUserJoined;
            Client.OnUserLeft += Client_OnUserLeft;
            Client.OnExistingUsersDetected += Client_OnExistingUsersDetected;

            Client.OnMessageReceived += Client_OnMessageReceived;
            Client.OnChatCommandReceived += Client_OnChatCommandReceived;
            Client.OnWhisperCommandReceived += Client_OnWhisperCommandReceived;
        }

        private void Client_OnExistingUsersDetected(object sender, OnExistingUsersDetectedArgs e)
        {
            try
            {
                GetChannelProcessor(e.Channel)?.OnExistingUsersDetected(sender, e);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"[{0}] Unchaught exception: {ex.Message}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnError(object sender, OnErrorEventArgs e)
        {
            Logger.Instance.Error($"Twitch client error fired: {e.Exception.Message}, {e.Exception.StackTrace}");
        }

        private void Client_OnReconnected(object sender, OnReconnectedEventArgs e)
        {
            Logger.Instance.Warn("Twitch client reconnected");
        }

        private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            Logger.Instance.Fatal($"Client connection error: {e.Error.Message}");
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            if (_isStopping)
                Logger.Instance.Info($"Twitch client disconnected");
            else
            {
                Logger.Instance.Error($"Twitch client disconnected, reconnecting...");
            }
        }

        public void Start()
        {
            Client.Connect();
        }

        private void InitializeTelegram()
        {
            if (!string.IsNullOrEmpty(_settings.TelegramToken))
                Telegram = new TelegramBotClient(_settings.TelegramToken);
        }

        private void ConnectToChannel(WatchedChannel channel)
        {
            if (!_channelProcessors.ContainsKey(channel.Name))
                _channelProcessors[channel.Name] = new ChannelProcessor(channel, _settings);

            if (!HasJoinedChannel(channel.Name))
                Client.JoinChannel(channel.Name);
        }

        private bool HasJoinedChannel(string channelName)
        {
            return Client.JoinedChannels.Any(_ => _.Channel.ToLower() == channelName.ToLower());
        }

        private ChannelProcessor GetChannelProcessor(string channelName)
        {
            return _channelProcessors.FirstOrDefault(_ => _.Value.Channel.Name.ToLower() == channelName.ToLower()).Value;
        }

        public void Stop(bool async = false)
        {
            _isStopping = true;

            foreach (var processor in _channelProcessors)
                processor.Value.Stop(async);

            _updateThreadTokenSource.Cancel();

            Client.Disconnect();

            if (!async)
            {
                while (!IsStopped)
                    Thread.Sleep(50);
            }

            _isStopping = false;
        }

        private List<WatchedChannel> LoadWatchedChannels()
        {
            var watchedChannels = new Dictionary<string, WatchedChannel>();

            try
            {
                using (var command = new MySqlCommand("SELECT bwc.channel_id, btn.telegram_channel_id, bwc.install_date FROM bot_watched_channels bwc " +
                    "LEFT JOIN bot_telegram_notify btn ON bwc.channel_id = btn.channel_id AND bwc.bot_id = btn.bot_id " +
                    "WHERE bwc.bot_id = @bot_id", DbConnection.GetConnection()))
                {
                    command.Parameters.AddWithValue("@bot_id", _settings.Id);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var ChannelId = reader.GetString(0);

                            WatchedChannel channel;
                            if (watchedChannels.TryGetValue(ChannelId, out channel))
                            {
                                if (!reader.IsDBNull(1))
                                    channel.TelegramNotifyChannels.Add(reader.GetString(1));
                            }
                            else
                            {
                                channel = new WatchedChannel();
                                channel.BotId = _settings.Id;
                                channel.Id = ChannelId;
                                channel.InstallDate = reader.GetInt32(2);
                                if (!reader.IsDBNull(1))
                                    channel.TelegramNotifyChannels.Add(reader.GetString(1));

                                watchedChannels[ChannelId] = channel;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Faled to load watched channels: {e.Message}");

                return new List<WatchedChannel>();
            }

            var channels = watchedChannels.Select(_ => _.Value).ToList();
            foreach (var channel in channels)
            {
                try
                {
                    var res = Api.V5.Channels.GetChannelByIDAsync(channel.Id).Result;
                    if (res == null)
                    {
                        Logger.Instance.Warn($"Failed to resolve channel with id '{channel.Id}");
                        continue;
                    }

                    channel.Name = res.Name.ToLower();
                }
                catch (Exception ex)
                {
                    Logger.Instance.Error($"Failed to add watched channel: {ex.Message}");
                    continue;
                }
            }

            return channels;
        }

        private void UpdateThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    ReconnectIfNeeded();
                }
                catch (Exception e)
                {
                    Logger.Instance.Error($"Chat bot update loop failed: {e.Message}");
                }

                Thread.Sleep(10000);
            }
        }

        private void ReconnectIfNeeded()
        {
            if (_isStopping)
                return;

            if (!Client.IsInitialized)
                return;

            if (!Client.IsConnected)
                Client.Connect();
            else
            {
                foreach (var channel in _watchedChannels)
                    if (!HasJoinedChannel(channel.Name))
                    {
                        try
                        {
                            ConnectToChannel(channel);
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.Error($"Failed to connect to channel: {ex.Message}");
                            continue;
                        }
                    }
            }
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            try
            {
                GetChannelProcessor(e.ChatMessage.Channel)?.OnMessageReceived(sender, e);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"[{0}] Unchaught exception: {ex.Message}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnChatCommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            try
            {
                GetChannelProcessor(e.Command.ChatMessage.Channel)?.OnCommand(sender, e);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"[{0}] Unchaught exception: {ex.Message}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnUserLeft(object sender, OnUserLeftArgs e)
        {
            try
            {
                GetChannelProcessor(e.Channel)?.OnUserLeft(sender, e);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"[{0}] Unchaught exception: {ex.Message}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnUserJoined(object sender, OnUserJoinedArgs e)
        {
            try
            {
                GetChannelProcessor(e.Channel)?.OnUserJoined(sender, e);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"[{0}] Unchaught exception: {ex.Message}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Logger.Instance.Info($"Bot connected: {e.BotUsername}");

            ConnectChannels();
            if (_updateTask == null)
                _updateTask = Task.Run(() => UpdateThread(_updateThreadTokenSource.Token));
        }

        private void ConnectChannels()
        {
            if (_watchedChannels.Count == 0)
                _watchedChannels = LoadWatchedChannels();

            foreach (var channel in _watchedChannels)
            {
                try
                {
                    ConnectToChannel(channel);
                }
                catch (Exception ex)
                {
                    Logger.Instance.Error($"Failed to connect to channel: {ex.Message}");
                    continue;
                }
            }
        }

        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            try
            {
                GetChannelProcessor(e.Channel)?.OnJoinedChannel(sender);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"[{0}] Unchaught exception: {ex.Message}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnLeftChannel(object sender, OnLeftChannelArgs e)
        {
            try
            {
                GetChannelProcessor(e.Channel)?.OnLeftChannel(sender);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"[{0}] Unchaught exception: {ex.Message}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnLog(object sender, OnLogArgs e)
        {
            Console.WriteLine($"Log event: {e.Data}");
        }

        private void Client_OnWhisperCommandReceived(object sender, OnWhisperCommandReceivedArgs e)
        {
            try
            {
                _whisperCommandHandler.Handle(e.Command);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Failed to handle whisper command: {ex.Message}");
            }
        }
    }
}
