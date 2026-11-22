using FlyerMonkey.Shared.Model;
using Microsoft.Data.SqlClient;

namespace SQLServerConnection.Data;

public interface ICustomerRepository
{
    Task<CustomerAccount?> GetAsync(Guid tenant, Guid user, CancellationToken token);
    Task<CustomerAccount> ProvisionAsync(Guid tenant, Guid user, CancellationToken token);
    Task<CustomerGenerationAccess?> GetGenerationAccessAsync(Guid tenant, Guid user, CancellationToken token) =>
        Task.FromResult<CustomerGenerationAccess?>(null);
}

public sealed class CustomerRepository(string connectionString) : ICustomerRepository
{
    public async Task<CustomerGenerationAccess?> GetGenerationAccessAsync(Guid tenant, Guid user, CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = """
            SELECT c.CustomerId,u.ExternalTenantId,u.ExternalUserId,u.Status,r.Name
            FROM dbo.Customers c
            JOIN dbo.Users u ON u.UserId=c.UserId
                AND u.ExternalTenantId=c.ExternalTenantId AND u.ExternalUserId=c.ExternalUserId
            LEFT JOIN dbo.UserRoles ur ON ur.UserId=u.UserId
            LEFT JOIN dbo.Roles r ON r.RoleId=ur.RoleId
            WHERE c.ExternalTenantId=@tenant AND c.ExternalUserId=@user;
            """;
        command.Parameters.AddWithValue("@tenant", tenant);
        command.Parameters.AddWithValue("@user", user);
        await using var reader = await command.ExecuteReaderAsync(token);
        CustomerGenerationAccess? access = null;
        var roles = new List<string>();
        while (await reader.ReadAsync(token))
        {
            access ??= new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), roles);
            if (!reader.IsDBNull(4)) roles.Add(reader.GetString(4));
        }
        return access;
    }

    public Task<CustomerAccount?> GetAsync(Guid tenant, Guid user, CancellationToken token) => Query(tenant, user, false, token);
    public async Task<CustomerAccount> ProvisionAsync(Guid tenant, Guid user, CancellationToken token) =>
        await Query(tenant, user, true, token) ?? throw new InvalidOperationException("Customer provisioning returned no account.");

    private async Task<CustomerAccount?> Query(Guid tenant, Guid user, bool provision, CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        // Serializable range locking plus a unique index makes concurrent first logins one account.
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandTimeout = 10;
        command.CommandText = (provision ? """
            IF NOT EXISTS (SELECT 1 FROM dbo.Customers WITH (UPDLOCK,HOLDLOCK)
                WHERE ExternalTenantId=@tenant AND ExternalUserId=@user)
                INSERT dbo.Customers (CustomerId,ExternalTenantId,ExternalUserId,Status,CraterEnabled,CreatedUtc,UpdatedUtc)
                VALUES (NEWID(),@tenant,@user,N'Active',0,SYSUTCDATETIME(),SYSUTCDATETIME());
            """ : "") + """
            SELECT CustomerId,Status,CraterEnabled FROM dbo.Customers
            WHERE ExternalTenantId=@tenant AND ExternalUserId=@user;
            """;
        command.Parameters.AddWithValue("@tenant", tenant);
        command.Parameters.AddWithValue("@user", user);
        CustomerAccount? account = null;
        await using (var reader = await command.ExecuteReaderAsync(token))
            if (await reader.ReadAsync(token)) account = new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2));
        await transaction.CommitAsync(token);
        return account;
    }
}
