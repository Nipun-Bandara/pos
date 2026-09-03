using System.Reflection;
using NetArchTest.Rules;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;

namespace RetailCore.Architecture.Tests;

/// <summary>
/// Rules that are cheaper to enforce than to remember.
/// </summary>
/// <remarks>
/// PLAN.md Phase 1 lists these as a deliverable and section 5 makes them a CI gate. Each one
/// encodes a decision that is easy to erode one pull request at a time, and impossible to
/// undo once the erosion has spread.
/// </remarks>
public class LayeringSpecs
{
    private static readonly Assembly Domain = typeof(Money).Assembly;
    private static readonly Assembly Application = typeof(Application.Abstractions.IUnitOfWork).Assembly;
    private static readonly Assembly Infrastructure =
        typeof(Infrastructure.Persistence.RetailCoreDbContext).Assembly;

    [Fact]
    public void The_domain_depends_on_nothing_but_the_framework()
    {
        // The domain is where the shop's rules live. It has to be readable and testable
        // without a database, a UI or a container, which is only true if it cannot reach any
        // of them.
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "RetailCore.Application",
                "RetailCore.Infrastructure",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Extensions",
                "Npgsql",
                "Avalonia")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void The_domain_project_references_no_packages_at_all()
    {
        // Stronger than the dependency rule above and the one that actually holds the line:
        // a package cannot leak into the domain if the project has none.
        var referenced = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                           && !name.Equals("netstandard", StringComparison.Ordinal))
            .ToList();

        referenced.ShouldBeEmpty(
            $"the domain should reference only the base class library, but found: {string.Join(", ", referenced)}");
    }

    [Fact]
    public void The_application_layer_does_not_depend_on_infrastructure()
    {
        // The application states what it needs as interfaces; infrastructure supplies them.
        // Reversing that would put EF Core in the way of every use case test.
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("RetailCore.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void The_shared_infrastructure_does_not_depend_on_a_specific_provider()
    {
        // One model, two providers. If Npgsql leaks into the shared layer, the till's SQLite
        // build stops matching the server's and PLAN.md's Phase 1 exit criterion is a fiction.
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny("Npgsql", "Microsoft.EntityFrameworkCore.Sqlite")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Aggregate_roots_are_sealed()
    {
        // An aggregate enforces invariants. A subclass could override its way around them.
        var unsealed = Domain.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && InheritsFromAggregateRoot(t))
            .Where(t => !t.IsSealed)
            .Select(t => t.Name)
            .ToList();

        unsealed.ShouldBeEmpty();
    }

    private static bool InheritsFromAggregateRoot(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }
}
