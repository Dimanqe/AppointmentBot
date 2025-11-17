using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointmentBot.Models
{
    public class BotSettings
    {
        public long AdminId { get; set; }          // telegram admin id
        public int? LastChannelMessageId { get; set; }
    }

}
