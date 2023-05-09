using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using WTFShared;
using WTFShared.Logging;
using WTFTwitch.Bot.Commands;

namespace WTFTwitch.Bot
{
    class ChatBot
    {
        public TwitchClient Client { get; private set; }

        public bool IsStopped => _updateTask.Status == TaskStatus.RanToCompletion;

        public BotSettings Settings { get; private set; }

        private ChannelProcessor _channelProcessor;

        private readonly WhisperCommandHandler _whisperCommandHandler;

        public string BotName => Client.TwitchUsername;

        private bool _isStopping = false;

        private readonly CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();

        private Task _updateTask;

        public ChatBot(BotSettings settings)
        {
            this.Settings = settings;

            var api = ApiPool.FindByBotName(Settings.BotName);
            if (api == null)
                throw new Exception("Failed to get api container by bot");

            InitializeClient(api.AccessToken);

            _whisperCommandHandler = new WhisperCommandHandler(this);
        }

        public Logger Logger => LoggerFactory.GetForBot("channel", Settings.BotName, Settings.Channel.ChannelName);

        private Logger RawLogger => LoggerFactory.GetForBot("raw", Settings.BotName, Settings.Channel.ChannelName);

        private void InitializeClient(string accessToken)
        {
            var credentials = new ConnectionCredentials(Settings.BotName, accessToken);

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
                Logger.Error($"[{0}] Unchaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnError(object sender, OnErrorEventArgs e)
        {
            Logger.Error($"Twitch client error fired: {e.Exception.Info()}, {e.Exception.StackTrace}");
        }

        private void Client_OnReconnected(object sender, OnReconnectedEventArgs e)
        {
            Logger.Warn("Twitch client reconnected");
        }

        private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            Logger.Fatal($"Client connection error: {e.Error.Message}");
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            if (_isStopping)
                Logger.Info($"Twitch client disconnected");
            else
            {
                Logger.Error($"Twitch client disconnected, reconnecting...");
            }
        }

        public void Start()
        {
            Client.Connect();
        }

        private void ConnectToChannel()
        {
            if (_channelProcessor == null)
                _channelProcessor = new ChannelProcessor(this);

            if (!HasJoinedChannel())
                Client.JoinChannel(Settings.Channel.ChannelName);
        }

        private bool HasJoinedChannel()
        {
            return Client.JoinedChannels.Any(
                _ => string.Equals(_.Channel, Settings.Channel.ChannelName, StringComparison.CurrentCultureIgnoreCase));
        }

        private ChannelProcessor GetChannelProcessor(string channelName)
        {
            return _channelProcessor;
        }

        public void Stop(bool async = false)
        {
            _isStopping = true;

            _channelProcessor.Stop(async);

            _updateThreadTokenSource.Cancel();

            Client.Disconnect();

            if (!async)
            {
                while (!IsStopped)
                    Thread.Sleep(50);
            }

            _isStopping = false;
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
                    Logger.Error($"Chat bot update loop failed: {e.Info()}");
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
            var container = ApiPool.FindByBotName(Settings.BotName);
            if (container != null && "oauth:" + container.AccessToken != Client.ConnectionCredentials.TwitchOAuth)
            {
                Logger.Warn("Client token changed, reconnecting chat bot");
                Client.Disconnect();
                InitializeClient(container.AccessToken);
                Start();
                return;
            }

            if (!Client.IsConnected)
                Client.Connect();
            else
            {
                if (!HasJoinedChannel())
                {
                    try
                    {
                        ConnectToChannel();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to connect to channel: {ex.Info()}");
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
                Logger.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Logger.Info($"Bot connected: {e.BotUsername}");

            ConnectChannels();
            if (_updateTask == null)
                _updateTask = Task.Run(() => UpdateThread(_updateThreadTokenSource.Token));
        }

        private void ConnectChannels()
        {
            try
            {
                ConnectToChannel();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to connect to channel: {ex.Info()}");
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
                Logger.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
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
                Logger.Error($"[{0}] Uncaught exception: {ex.Info()}", MethodBase.GetCurrentMethod().Name);
            }
        }

        private void Client_OnLog(object sender, OnLogArgs e)
        {
            RawLogger.Trace($"Log event: {e.Data}");
        }

        private void Client_OnWhisperCommandReceived(object sender, OnWhisperCommandReceivedArgs e)
        {
            try
            {
                _whisperCommandHandler?.Handle(e.Command);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to handle whisper command: {ex.Info()}");
            }
        }
    }
}
