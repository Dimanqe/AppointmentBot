#region

using System.Collections.Concurrent;

#endregion

namespace AppointmentBot.Services;

public class AdminSessionStorage : IAdminSessionStorage
{
    private readonly ConcurrentDictionary<long, AdminSession> _sessions = new();

    // --- TEMP DATA ---
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<string, string>> _tempData = new();

    public AdminSession GetOrCreateSession(long adminId)
    {
        return _sessions.GetOrAdd(adminId, id => new AdminSession { AdminId = id });
    }

    public void SaveSession(AdminSession session)
    {
        _sessions[session.AdminId] = session;
    }

    public void SetSelectedService(long adminId, int serviceId)
    {
        var session = GetOrCreateSession(adminId);
        session.SelectedServiceId = serviceId;
        SaveSession(session);
    }

    public int? GetSelectedService(long adminId)
    {
        var session = GetOrCreateSession(adminId);
        return session.SelectedServiceId;
    }

    public void ClearSelectedService(long adminId)
    {
        var session = GetOrCreateSession(adminId);
        session.SelectedServiceId = null;
        SaveSession(session);
    }

    public void SetTempData(long adminId, string key, string value)
    {
        var adminTemp = _tempData.GetOrAdd(adminId, new ConcurrentDictionary<string, string>());
        adminTemp[key] = value;
    }

    public bool TryGetTempData(long adminId, string key, out string? value)
    {
        value = null;
        if (_tempData.TryGetValue(adminId, out var adminTemp)) return adminTemp.TryGetValue(key, out value);
        return false;
    }

    public void ClearTempData(long adminId, string key)
    {
        if (_tempData.TryGetValue(adminId, out var adminTemp)) adminTemp.TryRemove(key, out _);
    }
}