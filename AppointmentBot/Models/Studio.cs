using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointmentBot.Models
{
    public class Studio
    {
        public int Id { get; set; } = 1;  // Always 1 — single studio config

        public string Name { get; set; } = "Студия красоты";
        public string Address { get; set; } = "Адрес не указан";
        public string Phone { get; set; } = "Телефон не указан";
        public string Instagram { get; set; } = "";
        public string Description { get; set; } = "";
    }
}

