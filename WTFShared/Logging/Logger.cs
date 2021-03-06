﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NLog.Conditions;
using NLog.Targets;

namespace WTFShared.Logging
{
    public static class Logger
    {
        private static NLog.Logger _logInstance = null;

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

        public static NLog.Logger Instance => _logInstance;

        static Logger()
        {
            var config = new NLog.Config.LoggingConfiguration();

            var fileLog = new FileTarget("logfile") { FileName = "WTFTwitch.log" };
            fileLog.Layout = DefaultLayout;

            var consoleLog = new ColoredConsoleTarget("consolelog");
            consoleLog.Layout = DefaultLayout;
            foreach (var rule in GetColorRules())
                consoleLog.RowHighlightingRules.Add(rule);

            config.AddRuleForAllLevels(fileLog);
            config.AddRuleForAllLevels(consoleLog);

            NLog.LogManager.Configuration = config;

            _logInstance = NLog.LogManager.GetCurrentClassLogger();
        }

        private static IEnumerable<ConsoleRowHighlightingRule> GetColorRules()
        {
            return ColoringRules.Select(_ =>
            {
                var highlightRule = new ConsoleRowHighlightingRule();

                highlightRule.Condition = ConditionParser.ParseExpression(string.Format("level == LogLevel.{0}", _.Key.ToString()));
                highlightRule.ForegroundColor = _.Value;

                return highlightRule;
            });
        }
    }
}
