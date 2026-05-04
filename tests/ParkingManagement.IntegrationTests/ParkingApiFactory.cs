using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParkingManagement.Api.Data;
using Testcontainers.PostgreSql;

namespace ParkingManagement.IntegrationTests;

public class ParkingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("parking_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ParkingDbContext>));

            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<ParkingDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));
        });
    }

    public ParkingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new ParkingDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Bootstrap schema and seed data once for the whole test class lifetime.
        await using var ctx = new ParkingManagement.Api.Data.ParkingDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ParkingManagement.Api.Data.ParkingDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options);

        await ctx.Database.EnsureCreatedAsync();
        ParkingManagement.Api.Data.SeedData.Initialize(ctx);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
