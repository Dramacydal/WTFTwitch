using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using WTFTwitch.Bot.Commands;
using WTFTwitch.Database;

namespace WTFTwitch.Bot
{
    class ChatBot
    {
        public TwitchAPI Api { get; private set; }
        public TwitchClient Client { get; private set; }

        private BotSettings _settings;

        private Dictionary<string, ChannelProcessor> _channelProcessors = new Dictionary<string, ChannelProcessor>();

        private WhisperCommandHandler _whisperCommandHandler;

        public TelegramBotClient Telegram { get; private set; }

        public string BotName => Client.TwitchUsername;

        private bool _isStopping = false;

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

            Client.OnMessageReceived += Client_OnMessageReceived;
            Client.OnChatCommandReceived += Client_OnChatCommandReceived;
            Client.OnWhisperCommandReceived += Client_OnWhisperCommandReceived;
        }

        private void Client_OnError(object sender, OnErrorEventArgs e)
        {
            Console.WriteLine($"Twitch client error fired: {e.Exception.Message}, {e.Exception.StackTrace}");
        }

        private void Client_OnReconnected(object sender, OnReconnectedEventArgs e)
        {
            Console.WriteLine("Twitch client reconnected");
        }

        private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            Console.WriteLine($"Client connection error: {e.Error.Message}");
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            Console.WriteLine($"Twitch client disconnected");
            if (!_isStopping)
                Client.Connect();
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

        private void AddWatchedChannel(WatchedChannel channel)
        {
            _channelProcessors[channel.Name] = new ChannelProcessor(channel, _settings);
            Client.JoinChannel(channel.Name);
        }

        private ChannelProcessor GetChannelProcessor(string channelName)
        {
            return _channelProcessors.FirstOrDefault(_ => _.Value.Channel.Name.ToLower() == channelName.ToLower()).Value;
        }

        public void Stop()
        {
            _isStopping = true;
            foreach (var processor in _channelProcessors)
                processor.Value.Stop();

            Client.Disconnect();
            _isStopping = false;
        }

        private List<WatchedChannel> LoadWatchedChannels()
        {
            var WatchedChannels = new Dictionary<string, WatchedChannel>();

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
                            if (WatchedChannels.TryGetValue(ChannelId, out channel))
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

                                WatchedChannels[ChannelId] = channel;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");

                return new List<WatchedChannel>();
            }

            return WatchedChannels.Select(_ => _.Value).ToList();
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            try
            {
                GetChannelProcessor(e.ChatMessage.Channel)?.OnMessageReceived(sender, e);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unchaught exception: {ex.Message}");
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
                Console.WriteLine($"Unchaught exception: {ex.Message}");
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
                Console.WriteLine($"Unchaught exception: {ex.Message}");
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
                Console.WriteLine($"Unchaught exception: {ex.Message}");
            }
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Console.WriteLine($"Bot connected: {e.BotUsername}");

            var channels = LoadWatchedChannels();
            if (channels.Count == 0)
                return;

            foreach (var channel in channels)
            {
                try
                {
                    var res = Api.V5.Channels.GetChannelByIDAsync(channel.Id).Result;
                    if (res == null)
                    {
                        Console.WriteLine($"Failed to resolve channel with id '{channel.Id}");
                        continue;
                    }

                    channel.Name = res.Name.ToLower();
                    AddWatchedChannel(channel);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
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
                Console.WriteLine($"Unchaught exception: {ex.Message}");
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
                Console.WriteLine($"Unchaught exception: {ex.Message}");
            }
        }

        private void Client_OnLog(object sender, OnLogArgs e)
        {
            Console.WriteLine($"Log: {e.Data}");
        }

        private void Client_OnWhisperCommandReceived(object sender, OnWhisperCommandReceivedArgs e)
        {
            try
            {
                _whisperCommandHandler.Handle(e.Command);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to handle whisper command: {ex.Message}");
            }
        }
    }
}
