using AppointmentBot.Services;

namespace AppointmentBot.Models;

public class Service
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int DurationMinutes { get; set; }
    public int Price { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<BookingService> BookingServices { get; set; } = new List<BookingService>();
}