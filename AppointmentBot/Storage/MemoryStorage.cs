#region

using System.Collections.Concurrent;
using AppointmentBot.Storage.Models;

#endregion

namespace AppointmentBot.Storage;

public class MemoryUserSessionStorage : IUserSessionStorage
{
    private readonly ConcurrentDictionary<long, UserSession> _sessions = new();

    public UserSession GetOrCreateSession(long userId)
    {
        return _sessions.GetOrAdd(userId, _ => new UserSession
        {
            UserId = userId,
            CurrentMenu = "main",
            CurrentMonth = DateTime.Today
        });
    }

    public void SaveSession(UserSession session)
    {
        _sessions[session.UserId] = session;
    }

    public void ClearSession(long userId)
    {
        _sessions.TryRemove(userId, out _);
    }
}