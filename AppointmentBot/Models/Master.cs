namespace AppointmentBot.Models;

public class Master
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Bio { get; set; }
    public string? TelegramLink { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}