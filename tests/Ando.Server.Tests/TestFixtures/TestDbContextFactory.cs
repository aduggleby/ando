// =============================================================================
// TestDbContextFactory.cs
//
// Summary: Factory for creating in-memory database contexts for testing.
//
// Provides isolated database contexts for each test to prevent test pollution.
// Uses Entity Framework Core's InMemory provider.
// =============================================================================

using Ando.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// Factory for creating test database contexts.
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// Creates a new in-memory database context with a unique database name.
    /// </summary>
    public static AndoDbContext Create(string? databaseName = null)
    {
        databaseName ??= Guid.NewGuid().ToString();

        var options = new DbContextOptionsBuilder<AndoDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var context = new AndoDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }

    /// <summary>
    /// Creates a new context that shares the same database as an existing context.
    /// </summary>
    public static AndoDbContext CreateShared(AndoDbContext existingContext)
    {
        var options = new DbContextOptionsBuilder<AndoDbContext>()
            .UseInMemoryDatabase(existingContext.Database.GetDbConnection().Database)
            .Options;

        return new AndoDbContext(options);
    }
}
