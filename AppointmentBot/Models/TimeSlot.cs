namespace AppointmentBot.Models;

public class TimeSlot
{
    public int Id { get; set; }

    public DateTime Date { get; set; }

    public TimeSpan StartTime { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsOccupied { get; set; } // new
}