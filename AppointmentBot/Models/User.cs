namespace AppointmentBot.Models;

public class User
{
    public long Id { get; set; } // Telegram user ID
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}