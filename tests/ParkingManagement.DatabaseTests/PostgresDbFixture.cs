using Microsoft.EntityFrameworkCore;
using ParkingManagement.Api.Data;
using Testcontainers.PostgreSql;

namespace ParkingManagement.DatabaseTests;

public class PostgresDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("parking_db_tests")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public ParkingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ParkingDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await ResetDatabaseAsync(seedLargeData: true);
    }

    public async Task ResetDatabaseAsync(bool seedLargeData = false)
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        if (seedLargeData)
            SeedData.Initialize(context);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
