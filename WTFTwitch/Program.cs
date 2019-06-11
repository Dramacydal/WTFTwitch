using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Extensions;
using TwitchLib.Client.Models;
using WTFShared.Logging;
using WTFTwitch.Bot;

namespace WTFTwitch
{
    class Program
    {
        private static BotManager manager;

        static void Main(string[] args)
        {
            bool needStop = false;
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
            {
                e.Cancel = true;
                needStop = true;
            };

            manager = new BotManager();
            manager.Start();
            while(!needStop)
                Thread.Sleep(50);
            Logger.Instance.Info("Stopping...");
            manager.Stop();
        }
    }
}
