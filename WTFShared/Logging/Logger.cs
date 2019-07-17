using NLog;
using NLog.Conditions;
using NLog.Targets;
using System.Collections.Generic;
using System.Linq;

namespace WTFShared.Logging
{
    public static class Logger
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

        public static NLog.Logger Instance { get; } = null;

        static Logger()
        {
            var config = new NLog.Config.LoggingConfiguration();

            var fileLog = new FileTarget("logfile") {FileName = "WTFTwitch.log", Layout = DefaultLayout};

            var consoleLog = new ColoredConsoleTarget("consolelog") {Layout = DefaultLayout};
            foreach (var rule in GetColorRules())
                consoleLog.RowHighlightingRules.Add(rule);

            config.AddRuleForAllLevels(fileLog);
            config.AddRuleForAllLevels(consoleLog);

            NLog.LogManager.Configuration = config;

            Instance = NLog.LogManager.GetCurrentClassLogger();
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
