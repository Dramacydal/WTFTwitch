using System;
using System.Threading;
using MySql.Data.MySqlClient;
using WTFShared.Database;
using WTFShared.Logging;
using WTFShared.Tasks;
using WTFTwitch.Bot;

namespace WTFTwitch
{
    class Program
    {
        private static BotManager _manager;

        private static void Main(string[] args)
        {
            if (true)
            {
                var manager = new TaskManager();

                var finishedCOunt = 0;
                for (var i = 0; i < 500; ++i)
                {
                    var task = new QueryTask(new MySqlCommand("SHOW TABLES"), 10, () =>
                    {
                        Console.WriteLine($"WTFTask {++finishedCOunt} finished");
                        return TaskStatus.Finished;
                    });

                    manager.AddTask(task);
                }

                System.Threading.Tasks.Task.Run(() =>
                {
                    for (;;)
                    {
                        Thread.Sleep(50);
                        try
                        {
                            manager.Update();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                });

                for (;;)
                    Thread.Sleep(500);

                return;
            }

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
