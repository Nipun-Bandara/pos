using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RetailCore.Domain.Monetary;

namespace RetailCore.Infrastructure.Persistence;

/// <summary>
/// Maps <see cref="Money"/> to two columns: the minor units and the ISO code.
/// </summary>
/// <remarks>
/// <para>
/// Two columns rather than one opaque value, so that reporting can aggregate in SQL.
/// BO-06 through BO-09 all reduce to <c>SUM(...Minor) GROUP BY something</c>, and the
/// margin report over a busy month is not something to pull into memory.
/// </para>
/// <para>
/// The currency code is carried on every amount even though a shop trades in one currency.
/// It costs three bytes and it means a row can be read and understood on its own, including
/// by whatever ad hoc SQL an owner's accountant eventually runs.
/// </para>
/// </remarks>
internal static class MoneyMapping
{
    /// <summary>Number of characters in an ISO 4217 code.</summary>
    public const int CurrencyCodeLength = 3;

    /// <summary>Maps an amount that is always present.</summary>
    public static EntityTypeBuilder<TEntity> MapMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Money>> selector,
        string columnPrefix)
        where TEntity : class
    {
        builder.ComplexProperty(selector, money => Configure(money, columnPrefix));
        return builder;
    }

    /// <summary>
    /// Maps an amount that is absent until something happens, such as the counted cash on a
    /// shift that has not been closed yet. CSH-04.
    /// </summary>
    /// <remarks>
    /// Absent rather than zero: a drawer nobody has counted is a different thing from a
    /// drawer counted at nil, and a Z report has to be able to tell them apart.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> MapOptionalMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Money?>> selector,
        string columnPrefix)
        where TEntity : class
    {
        builder.ComplexProperty(selector, money =>
        {
            money.IsRequired(false);
            Configure(money, columnPrefix);
        });

        return builder;
    }

    /// <summary>
    /// Maps a <see cref="Quantity"/> to a single integer column of thousandths. SAL-05.
    /// </summary>
    /// <remarks>
    /// An integer rather than a decimal so that the stock ledger sum behind INV-02 runs in
    /// SQL on both providers. SQLite has no decimal type and cannot aggregate one.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> MapQuantity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Quantity>> selector,
        string columnName)
        where TEntity : class
    {
        builder.ComplexProperty(selector, quantity =>
            quantity.Property(q => q.Thousandths).HasColumnName(columnName + "Thousandths"));

        return builder;
    }

    private static void Configure(ComplexPropertyBuilder<Money> money, string columnPrefix)
    {
        money.Property(m => m.MinorUnits).HasColumnName(columnPrefix + "Minor");
        money.Property(m => m.CurrencyCode)
            .HasColumnName(columnPrefix + "Currency")
            .HasMaxLength(CurrencyCodeLength)
            .IsFixedLength();
    }
}
