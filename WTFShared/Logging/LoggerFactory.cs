using NLog;
using NLog.Conditions;
using NLog.Targets;
using System.Collections.Generic;
using System.Linq;

namespace WTFShared.Logging
{
    public static class LoggerFactory
    {
        private const string DefaultLayout = @"[${date:format=yyyy-MM-dd HH\:mm\:ss}][${level:uppercase=true}] ${message}";

        private static readonly Dictionary<LogLevel, ConsoleOutputColor> ColoringRules = new Dictionary<LogLevel, ConsoleOutputColor>()
        {
            [LogLevel.Trace] = ConsoleOutputColor.DarkGray,
            [LogLevel.Info] = ConsoleOutputColor.Green,
            [LogLevel.Debug] = ConsoleOutputColor.Gray,
            [LogLevel.Warn] = ConsoleOutputColor.DarkYellow,
            [LogLevel.Error] = ConsoleOutputColor.Red,
            [LogLevel.Fatal] = ConsoleOutputColor.Red,
        };

        public static Logger Global => LogManager.GetLogger("global");

        public static Logger GetForBot(string context, string botName, string channelName = "")
        {
            if (!string.IsNullOrEmpty(channelName))
                return LogManager.GetLogger($"{context}_{botName.ToLower()}_{channelName.ToLower()}");
            else
                return LogManager.GetLogger($"{context}_{botName.ToLower()}");
        }

        static LoggerFactory()
        {
            var config = new NLog.Config.LoggingConfiguration();

            var bot = new FileTarget("logfile") {FileName = @"WTFTwitch_${logger}.log", Layout = DefaultLayout};

            var consoleLog = new ColoredConsoleTarget("consolelog") {Layout = DefaultLayout};
            foreach (var rule in GetColorRules())
                consoleLog.RowHighlightingRules.Add(rule);
            
            config.AddRuleForAllLevels(bot);
            config.AddRuleForAllLevels(consoleLog);

            LogManager.Configuration = config;
        }
        
        private static IEnumerable<ConsoleRowHighlightingRule> GetColorRules()
        {
            return ColoringRules.Select(_ =>
            {
                var highlightRule = new ConsoleRowHighlightingRule
                {
                    Condition = ConditionParser.ParseExpression($"level == LogLevel.{_.Key.ToString()}"),
                    ForegroundColor = _.Value
                };

                return highlightRule;
            });
        }
    }
}
