using System.Reflection;
using RetailCore.Domain.Common;
using RetailCore.Domain.Monetary;
using RetailCore.Domain.Tenancy;

namespace RetailCore.Architecture.Tests;

/// <summary>
/// Rules about how the domain models money, tenancy and mutability.
/// </summary>
public class ModellingSpecs
{
    private static readonly Assembly Domain = typeof(Money).Assembly;

    /// <summary>Words that mean an amount of money, whatever the property is called.</summary>
    private static readonly string[] MonetaryWords =
    [
        "Amount", "Price", "Total", "Cost", "Cash", "Float", "Change", "Tendered",
        "Balance", "Variance", "Payable", "Takings", "Discount",
    ];

    [Fact]
    public void No_domain_type_holds_money_in_a_raw_decimal()
    {
        // PLAN.md Phase 1 calls for exactly this check. A decimal named Total is the first
        // step towards adding two currencies together, or towards a fraction of a cent that
        // never reconciles against the drawer.
        //
        // Rate and Percentage are ratios rather than amounts, so they are legitimately
        // decimal and are excluded by name.
        var offenders = Domain.GetTypes()
            .Where(t => t.IsClass || (t.IsValueType && !t.IsEnum))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => new { Type = t, Property = p }))
            .Where(x => x.Property.PropertyType == typeof(decimal)
                        || x.Property.PropertyType == typeof(decimal?))
            .Where(x => MonetaryWords.Any(word =>
                x.Property.Name.Contains(word, StringComparison.Ordinal)))
            .Where(x => !IsKnownRatio(x.Type, x.Property))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        offenders.ShouldBeEmpty(
            $"money must be a Money, not a decimal. Offending members: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void No_domain_type_holds_money_in_a_double_or_a_float()
    {
        // Binary floating point cannot represent 0.10 exactly, so a hundred additions of ten
        // cents does not give ten rupees. There is no defensible use for one here.
        var offenders = Domain.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => new { Type = t, Property = p }))
            .Where(x => x.Property.PropertyType == typeof(double)
                        || x.Property.PropertyType == typeof(float))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        offenders.ShouldBeEmpty(
            $"floating point has no place in a money model. Offending members: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Every_persisted_entity_belongs_to_a_shop()
    {
        // TEN-01. A table without a shop cannot have a row level security policy, so a new
        // entity has to declare which shop it belongs to before it can be persisted.
        //
        // Shop is the tenant itself, so it is the one exception.
        var offenders = Domain.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && IsEntity(t))
            .Where(t => t != typeof(Shop))
            .Where(t => !typeof(IBelongsToShop).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        offenders.ShouldBeEmpty(
            $"every entity must implement IBelongsToShop. Offending types: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Entity_state_cannot_be_set_from_outside()
    {
        // Invariants live in methods. A public setter is a way around every one of them, and
        // SAL-10 in particular ("totals computed from lines, never assignable") is only true
        // if this holds.
        var offenders = Domain.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && IsEntity(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(IsPubliclyAssignableAfterConstruction)
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offenders.ShouldBeEmpty(
            $"entity properties must not be publicly settable. Offending members: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Domain_events_are_immutable_records()
    {
        // An event is a statement about something that already happened. A handler that could
        // edit one could change what the next handler is told.
        var offenders = Domain.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(IsPubliclyAssignableAfterConstruction)
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offenders.ShouldBeEmpty(
            $"domain events must be immutable. Offending members: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// True for a property anyone can assign once the object exists.
    /// </summary>
    /// <remarks>
    /// An <c>init</c> accessor is public in the metadata but can only run during construction,
    /// which is exactly what a record's positional properties use. Treating those as mutable
    /// would fail every event in the system for being written the recommended way.
    /// </remarks>
    private static bool IsPubliclyAssignableAfterConstruction(PropertyInfo property)
    {
        var setter = property.SetMethod;
        if (setter is null || !setter.IsPublic)
        {
            return false;
        }

        var isInitOnly = setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        return !isInitOnly;
    }

    [Fact]
    public void Identifiers_are_strongly_typed_and_generated_as_uuid_v7()
    {
        // SYN-02. Every identifier type must offer New(), and that New() must produce sortable
        // values so index inserts stay sequential rather than scattering the B-tree.
        var idTypes = Domain.GetTypes()
            .Where(t => t.IsValueType && typeof(IEntityId).IsAssignableFrom(t))
            .ToList();

        idTypes.ShouldNotBeEmpty();

        foreach (var idType in idTypes)
        {
            var factory = idType.GetMethod("New", BindingFlags.Public | BindingFlags.Static);
            factory.ShouldNotBeNull($"{idType.Name} should expose a New() factory");

            var first = ((IEntityId)factory.Invoke(null, null)!).Value;
            var second = ((IEntityId)factory.Invoke(null, null)!).Value;

            first.ShouldNotBe(second);
            first.Version.ShouldBe(7, $"{idType.Name} should mint UUIDv7 values");
        }
    }

    private static bool IsEntity(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A ratio, not an amount. <c>MaxUnauthorisedRate</c> is a proportion of a sale and
    /// <c>Percentage</c> is a proportion of a line, so neither is money.
    /// </summary>
    private static bool IsKnownRatio(Type type, PropertyInfo property) =>
        property.Name.Contains("Rate", StringComparison.Ordinal)
        || property.Name.Contains("Percentage", StringComparison.Ordinal);
}
