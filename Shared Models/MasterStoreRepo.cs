using System.Text.Json;
using TenantPoC.Models;

namespace TenantPoC.Data;

public class MasterStoreRepo
{
    private readonly string _filePath;
    private List<Tenant> _tenants = [];
    private readonly ReaderWriterLockSlim _lock = new();

    public MasterStoreRepo(string filePath = "tenants.json")
    {
        _filePath = filePath;
        Load();
    }

    public IReadOnlyList<Tenant> GetAll()
    {
        _lock.EnterReadLock();
        try { return _tenants.ToList().AsReadOnly(); }
        finally { _lock.ExitReadLock(); }
    }

    public Tenant? GetById(Guid id)
    {
        _lock.EnterReadLock();
        try { return _tenants.FirstOrDefault(t => t.Id == id); }
        finally { _lock.ExitReadLock(); }
    }

    public Tenant? GetBySlug(string slug)
    {
        _lock.EnterReadLock();
        try { return _tenants.FirstOrDefault(t => t.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)); }
        finally { _lock.ExitReadLock(); }
    }

    public void Add(Tenant tenant)
    {
        _lock.EnterWriteLock();
        try
        {
            _tenants.Add(tenant);
            Save();
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool Remove(Guid id)
    {
        _lock.EnterWriteLock();
        try
        {
            var removed = _tenants.RemoveAll(t => t.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
        finally { _lock.ExitWriteLock(); }
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        var json = File.ReadAllText(_filePath);
        _tenants = JsonSerializer.Deserialize<List<Tenant>>(json) ?? [];
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_tenants, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
