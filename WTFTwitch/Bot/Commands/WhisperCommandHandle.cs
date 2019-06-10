using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwitchLib.Client.Models;

namespace WTFTwitch.Bot.Commands
{
    class WhisperCommandHandler : AbstractCommandHandler
    {
        public WhisperCommandHandler(BotSettings settings) : base(settings)
        {
        }

        private void SendWhisper(string receiver, string message, params object[] args)
        {
            message = string.Format(message, args);

            _client.SendWhisper(receiver, message);
        }

        public void Handle(WhisperCommand command)
        {
            switch (command.CommandText.ToLower())
            {
                case "install":
                    HandleInstallCommand(command);
                    break;
                case "users":
                    HandleUsersCommand(command);
                    break;
                default:
                    break;
            }
        }

        private void HandleUsersCommand(WhisperCommand command)
        {
            SendWhisper(command.WhisperMessage.Username, "users command");
        }

        private void HandleInstallCommand(WhisperCommand command)
        {
            SendWhisper(command.WhisperMessage.Username, "install command");
        }
    }
}
