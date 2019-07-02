using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WTFTwitch.Bot
{
    class UserUpdateData
    {
        public string UserId { get; }
        public DateTime LastUpdate { get; private set; }
        public DateTime LastSeen { get; private set; }

        public UserUpdateData(string userId)
        {
            this.UserId = userId;
            Reset();
        }

        public void UpdateLastSeen(DateTime date)
        {
            LastSeen = DateTime.UtcNow;
        }

        public void UpdateLastUpdate()
        {
            LastUpdate = LastSeen;
        }

        public void Reset()
        {
            LastUpdate = LastSeen = DateTime.UtcNow;
        }

        public TimeSpan LifeTime()
        {
            return LastSeen - LastUpdate;
        }
    }
}
