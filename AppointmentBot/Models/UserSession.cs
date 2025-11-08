#region

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

#endregion

namespace AppointmentBot.Models;

public class UserSession
{
    [Key] public long UserId { get; set; }

    /// <summary>
    ///     Language of the user (optional)
    /// </summary>
    public string? LanguageCode { get; set; }

    /// <summary>
    ///     The current menu the user is viewing
    /// </summary>
    [NotMapped]
    public string? CurrentMenu { get; set; }

    /// <summary>
    ///     History of visited menus (used for Back navigation)
    /// </summary>
    [NotMapped]
    public Stack<string> MenuHistory { get; } = new();

    /// <summary>
    ///     Selected lash services (user can pick multiple)
    /// </summary>
    public List<string> SelectedServices { get; set; } = new();

    /// <summary>
    ///     Optional: selected date/time for booking
    /// </summary>
    public DateTime? SelectedDateTime { get; set; }

    /// <summary>
    ///     Optional: user contact or username for confirmation
    /// </summary>
    public string? ContactInfo { get; set; }

    // Tracks which month is currently displayed in the calendar
    public DateTime CurrentMonth { get; set; } = DateTime.Today;

    // JSON-serialized data for simplicity
    public string SelectedServicesJson { get; set; } = "[]";
    public DateTime? SelectedDate { get; set; }
    public TimeSpan? SelectedTimeSlot { get; set; }
}