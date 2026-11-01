using FlyerMonkey.Shared.Model;
using Microsoft.Data.SqlClient;

namespace SQLServerConnection.Data;

public interface ICustomerRepository
{
    Task<CustomerAccount?> GetAsync(Guid tenant, Guid user, CancellationToken token);
    Task<CustomerAccount> ProvisionAsync(Guid tenant, Guid user, CancellationToken token);
}

public sealed class CustomerRepository(string connectionString) : ICustomerRepository
{
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
