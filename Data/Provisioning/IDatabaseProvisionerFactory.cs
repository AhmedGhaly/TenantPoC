using TenantPoC.Models;

namespace TenantPoC.Data.Provisioning;

public interface IDatabaseProvisionerFactory
{
    IDatabaseProvisioner Resolve(DatabaseProvider provider);
}
