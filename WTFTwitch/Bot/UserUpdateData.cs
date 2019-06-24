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
        public DateTime Created { get; private set; }
        public DateTime LastSeen { get; private set; }

        public UserUpdateData(string userId)
        {
            this.UserId = userId;
            Reset();
        }

        public void UpdateLastSeen()
        {
            LastSeen = DateTime.UtcNow;
        }

        public void Reset()
        {
            Created = LastSeen = DateTime.UtcNow;
        }

        public TimeSpan TimeFromLastSeen()
        {
            return (DateTime.UtcNow - LastSeen);
        }
    }
}
