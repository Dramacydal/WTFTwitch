using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {
                string assemblyInfo = resolveArgs.Name;// e.g "Lib1, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
                var parts = assemblyInfo.Split(',');
                string name = parts[0];
                var version = Version.Parse(parts[1].Split('=')[1]);
                string fullName;
                if (name == "System.Net.Http" && version.Major == 4 && version.Minor == 2)
                {
                    fullName = new FileInfo(@"4.8\System.Net.Http.dll").FullName;
                }
                else if (name == "System.Net.Http" && version.Major == 4 && version.Minor == 0)
                {
                    fullName = new FileInfo(@"4.7\System.Net.Http.dll").FullName;
                }
                else
                {
                    return null;
                }

                try
                {
                    return Assembly.LoadFile(fullName);
                }catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                return null;
            };


            TaskManager.Start();


            Console.WriteLine(1);
            var api = ApiPool.GetContainer();
            Console.WriteLine(2);
            api = ApiPool.GetContainer();
            Console.WriteLine(3);
            api = ApiPool.GetContainer();
            Console.WriteLine(4);
            api = ApiPool.GetContainer();
            Console.WriteLine(5);

            if (false)
            {
                var finishedCount = 0;
                for (var i = 0; i < 500; ++i)
                {
                    var task = new QueryTask(new MySqlCommand("SHOW TABLES"), 10, (_) =>
                    {
                        Console.WriteLine($"WTFTask {++finishedCount} finished");
                        return WTFTaskStatus.Finished;
                    });

                    TaskManager.AddTask(task);
                }

                for (;;)
                    Thread.Sleep(500);

                return;
            }

            bool needStop = false;
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                needStop = true;
            };

            int botId = 0, channelId = 0;

            foreach (var arg in args)
            {
                var m = Regex.Match(arg, @"--bot_id=(\d+)");
                if (m.Success)
                {
                    botId = int.Parse(m.Groups[1].Value);
                    Console.WriteLine($"Explicit bot id: {botId}");
                }

                m = Regex.Match(arg, @"--channel_id=(-?\d+)");
                if (m.Success)
                {
                    channelId = int.Parse(m.Groups[1].Value);
                    Console.WriteLine($"Explicit channel id: {channelId}");
                }
            }

            _manager = new BotManager();
            _manager.LoadSettings(botId, channelId);
            _manager.Start();
            while (!needStop)
                Thread.Sleep(50);
            Logger.Instance.Info("Stopping...");
            _manager.Stop();
        }
    }
}
