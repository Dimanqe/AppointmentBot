#region

using AppointmentBot.Models;

#endregion

namespace AppointmentBot.Services;

public interface IUserSessionStorage
{
    UserSession GetOrCreateSession(long userId);
    void SaveSession(UserSession session);
    void ClearSession(long userId);
}