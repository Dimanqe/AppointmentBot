namespace AppointmentBot.Services;

public interface IAdminSessionStorage
{
    AdminSession GetOrCreateSession(long adminId);
    void SaveSession(AdminSession session);

    void SetSelectedService(long adminId, int serviceId);
    int? GetSelectedService(long adminId);
    void ClearSelectedService(long adminId);

    // Temporary key-value storage
    void SetTempData(long adminId, string key, string value);
    bool TryGetTempData(long adminId, string key, out string? value);
    void ClearTempData(long adminId, string key);
}