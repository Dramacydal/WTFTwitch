using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using WTFShared;
using WTFShared.Database;
using WTFShared.Logging;
using WTFTwitch.Bot.Commands;

namespace WTFTwitch.Bot
{
    class ChatBot
    {
        public TwitchClient Client { get; private set; }

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        private readonly BotSettings _settings;

        private List<WatchedChannel> _watchedChannels = new List<WatchedChannel>();
        private readonly Dictionary<string, ChannelProcessor> _channelProcessors = new Dictionary<string, ChannelProcessor>();

        private readonly WhisperCommandHandler _whisperCommandHandler;

        public TelegramBotClient Telegram { get; private set; }

        public string BotName => Client.TwitchUsername;

        private bool _isStopping = false;

        private readonly CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private Task _updateTask;

        public ChatBot(BotSettings settings)
        {
            this._settings = settings;
            settings.Bot = this;

            var container = ApiPool.FindByBotName(_settings.Name);
            if (container == null)
                throw new Exception("Failed to get api container by bot");

            InitializeClient(container.AccessToken);

            InitializeTelegram();

            _whisperCommandHandler = new WhisperCommandHandler(_settings);
        }

        private void InitializeClient(string accessToken)
        {
            var credentials = new ConnectionCredentials(_settings.Name, accessToken);

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
                Logger.Instance.Error($"[{0}] Unchaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnError(object sender, OnErrorEventArgs e)
        {
            Logger.Instance.Error($"Twitch client error fired: {e.Exception.Info()}, {e.Exception.StackTrace}");
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
            return Client.JoinedChannels.Any(
                _ => string.Equals(_.Channel, channelName, StringComparison.CurrentCultureIgnoreCase));
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
                var query =
                    "SELECT bwc.channel_id, btn.telegram_channel_id, bwc.commands_enabled != 0, bwc.install_date FROM bot_watched_channels bwc " +
                    "LEFT JOIN bot_telegram_notify btn ON bwc.channel_id = btn.channel_id AND bwc.bot_id = btn.bot_id " +
                    "WHERE bwc.bot_id = @bot_id AND ";

                if (_settings.ExplicitChannelId != 0)
                    query += "bwc.channel_id = @channel_id";
                else
                    query += "bwc.enabled != 0";

                using (var command = new MySqlCommand(query, DbConnection.GetConnection()))
                {
                    command.Parameters.AddWithValue("@bot_id", _settings.Id);
                    if (_settings.ExplicitChannelId != 0)
                        command.Parameters.AddWithValue("@channel_id", _settings.ExplicitChannelId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var channelId = reader.GetString(0);

                            if (watchedChannels.TryGetValue(channelId, out var channel))
                            {
                                if (!reader.IsDBNull(1))
                                    channel.TelegramNotifyChannels.Add(reader.GetString(1));
                            }
                            else
                            {
                                channel = new WatchedChannel
                                {
                                    BotId = _settings.Id,
                                    Id = channelId,
                                    CommandsEnabled = reader.GetBoolean(2),
                                    InstallDate = reader.GetInt32(3),
                                };

                                if (!reader.IsDBNull(1))
                                    channel.TelegramNotifyChannels.Add(reader.GetString(1));

                                watchedChannels[channelId] = channel;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Instance.Error($"Faled to load watched channels: {e.Info()}");

                return new List<WatchedChannel>();
            }

            var channels = watchedChannels.Select(_ => _.Value).ToList();
            foreach (var channel in channels)
            {
                try
                {
                    var res1 = ApiPool.GetContainer().API.Helix.Channels.GetChannelInformationAsync(channel.Id).Result;
                    if (res1 == null || res1.Data.Length == 0)
                    {
                        Logger.Instance.Warn($"Failed to resolve channel with id '{channel.Id}");
                        continue;
                    }

                    channel.Name = res1.Data[0].BroadcasterLogin.ToLower();
                }
                catch (Exception e)
                {
                    Logger.Instance.Error($"Failed to add watched channel: {e.Info()}");
                    continue;
                }
            }

            return channels;
        }

        private void UpdateThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                //Logger.Instance.Debug("Update loop");

                //this.Client.Reconnect

                try
                {
                    ReconnectIfNeeded();
                }
                catch (Exception e)
                {
                    Logger.Instance.Error($"Chat bot update loop failed: {e.Info()}");
                }

                Thread.Sleep(3000);
            }
        }

        private void ReconnectIfNeeded()
        {
            if (_isStopping)
                return;

            if (!Client.IsInitialized)
                return;

            ApiPool.Reload();
            var container = ApiPool.FindByBotName(_settings.Name);
            if (container != null && "oauth:" + container.AccessToken != Client.ConnectionCredentials.TwitchOAuth)
            {
                Logger.Instance.Warn("Client token changed, reconnecting chat bot");
                Client.Disconnect();
                InitializeClient(container.AccessToken);
                Start();
                return;
            }

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
                            Logger.Instance.Error($"Failed to connect to channel: {ex.Info()}");
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
                Logger.Instance.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Instance.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Instance.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Instance.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                    Logger.Instance.Error($"Failed to connect to channel: {ex.Info()}");
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
                Logger.Instance.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Instance.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Instance.Error($"Failed to handle whisper command: {ex.Info()}");
            }
        }
    }
}
