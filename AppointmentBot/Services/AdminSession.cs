namespace AppointmentBot.Services;

public class AdminSession
{
    public long AdminId { get; set; }
    public int? SelectedServiceId { get; set; }
    public DateTime LastAccessed { get; set; }

    // Temporary fields for multi-step admin actions
    public string? TempServiceName { get; set; } // for adding new service
    public int? TempServicePrice { get; set; } // ✅ add this
    public int? TempServiceId { get; set; } // for editing existing service
    public string? ActionType { get; set; } // "price" or "duration"
    public int? TempTimeSlotId { get; set; }
    public DateTime? TempSlotDate { get; set; }
    public TimeSpan? TempSlotTime { get; set; }
    public string TempSlotAction { get; set; } // "add" or "edit"

    // --- NEW: For calendar navigation ---
    public DateTime CurrentMonth { get; set; } = DateTime.Today;

    public List<TimeSpan> SelectedTimes { get; set; } = new();
    public int? TempTimePickerMessageId { get; set; }
}
