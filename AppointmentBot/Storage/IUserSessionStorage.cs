

#region

using AppointmentBot.Storage.Models;

#endregion

namespace AppointmentBot.Storage;

public interface IUserSessionStorage
{
    UserSession GetOrCreateSession(long userId);
    void SaveSession(UserSession session);
    void ClearSession(long userId);
}