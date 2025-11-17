namespace AppointmentBot.Models;

public class Booking
{
    public int Id { get; set; }

    public long UserId { get; set; }
    public User User { get; set; } = null!;

    public int MasterId { get; set; }
    public Master Master { get; set; } = null!;

    public DateTime Date { get; set; } // Date of booking
    public TimeSpan TimeSlot { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool ReminderSent { get; set; }  // <-- add this

    public ICollection<BookingService> BookingServices { get; set; } = new List<BookingService>();
}