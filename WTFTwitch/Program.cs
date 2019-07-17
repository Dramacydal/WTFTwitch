using System;
using System.Threading;
using WTFShared.Logging;
using WTFTwitch.Bot;

namespace WTFTwitch
{
    class Program
    {
        private static BotManager _manager;

        private static void Main(string[] args)
        {
            bool needStop = false;
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
            {
                e.Cancel = true;
                needStop = true;
            };

            _manager = new BotManager();
            _manager.Start();
            while(!needStop)
                Thread.Sleep(50);
            Logger.Instance.Info("Stopping...");
            _manager.Stop();
        }
    }
}
