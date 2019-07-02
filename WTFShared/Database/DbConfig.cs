using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WTFShared.Configuration;

namespace WTFShared.Database
{
    [SourceFile("database")]
    class DbConfig
    {
        [IniEntry("host", "127.0.0.1")]
        public string Host;
        [IniEntry("port", 3306)]
        public int Port;
        [IniEntry("user", "twitch")]
        public string User;
        [IniEntry("password", "")]
        public string Password;
        [IniEntry("db", "twitch")]
        public string Database;
    }
}
