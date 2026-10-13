// =============================================================================
// UtcDateTimeConverterTests.cs
//
// Summary: Unit tests for UtcDateTimeConverter and its model-wide registration.
//
// Covers the three DateTimeKind write paths (Utc, Local, Unspecified), the read
// path (always tagged Utc), null handling for DateTime? properties, and a
// round trip through the InMemory provider to prove the convention is applied
// to entity properties.
// =============================================================================

using Ando.Server.Data.Conventions;
using Ando.Server.Models;
using Ando.Server.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Tests.Unit.Persistence;

public class UtcDateTimeConverterTests
{
    private readonly UtcDateTimeConverter _converter = new();

    [Fact]
    public void ToProvider_UtcValue_PassesThrough()
    {
        var value = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

        var result = (DateTime)_converter.ConvertToProvider(value)!;

        result.ShouldBe(value);
        result.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void ToProvider_LocalValue_ConvertsToSameInstantInUtc()
    {
        var value = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Local);

        var result = (DateTime)_converter.ConvertToProvider(value)!;

        result.Kind.ShouldBe(DateTimeKind.Utc);
        result.ShouldBe(value.ToUniversalTime());
    }

    [Fact]
    public void ToProvider_UnspecifiedValue_IsTaggedUtcWithoutShifting()
    {
        var value = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified);

        var result = (DateTime)_converter.ConvertToProvider(value)!;

        result.Kind.ShouldBe(DateTimeKind.Utc);
        result.Ticks.ShouldBe(value.Ticks);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void FromProvider_AnyKind_IsTaggedUtcWithoutShifting(DateTimeKind kind)
    {
        var value = new DateTime(2026, 10, 13, 12, 0, 0, kind);

        var result = (DateTime)_converter.ConvertFromProvider(value)!;

        result.Kind.ShouldBe(DateTimeKind.Utc);
        result.Ticks.ShouldBe(value.Ticks);
    }

    [Fact]
    public void Null_PassesThroughBothDirections()
    {
        _converter.ConvertToProvider(null).ShouldBeNull();
        _converter.ConvertFromProvider(null).ShouldBeNull();
    }

    [Fact]
    public void Model_AppliesConverterToDateTimeAndNullableDateTimeProperties()
    {
        using var db = TestDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(ProjectSecret))!;

        entity.FindProperty(nameof(ProjectSecret.CreatedAt))!.GetValueConverter()
            .ShouldBeOfType<UtcDateTimeConverter>();
        entity.FindProperty(nameof(ProjectSecret.UpdatedAt))!.GetValueConverter()
            .ShouldBeOfType<UtcDateTimeConverter>();
    }

    [Fact]
    public async Task RoundTrip_ReadsUtcKindAndKeepsNulls()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var db = TestDbContextFactory.Create(databaseName);
        var user = new ApplicationUser { UserName = "u", Email = "u@example.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var project = new Project
        {
            OwnerId = user.Id,
            GitHubRepoId = 1,
            RepoFullName = "o/r",
            RepoUrl = "https://example.com/o/r",
            CreatedAt = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified)
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        using var readDb = TestDbContextFactory.Create(databaseName);
        var loaded = await readDb.Projects.SingleAsync(p => p.Id == project.Id);

        loaded.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        loaded.CreatedAt.Ticks.ShouldBe(project.CreatedAt.Ticks);
        loaded.LastBuildAt.ShouldBeNull();
    }
}
