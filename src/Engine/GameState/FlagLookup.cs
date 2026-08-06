namespace OverlayMod.Engine.GameState;

/// <summary>
/// Every intermediate value from one event-flag lookup.
///
/// The lookup walks a chain of pointers through structures whose layout was
/// reverse-engineered rather than documented. When it returns the wrong answer,
/// the only useful question is which hop broke — a null instance pointer, an
/// unresolved signature, or an area whose world block is not loaded all look
/// identical from the outside. <see cref="FailedAt"/> names the step that gave up.
/// </summary>
public sealed record FlagLookup
{
    public uint Id { get; init; }
    public bool Attached { get; init; }

    /// <summary>Null when the lookup completed; otherwise the step that gave up.</summary>
    public string? FailedAt { get; init; }

    public bool IsSet { get; init; }

    // The id decomposed: which bucket, area, block and group it belongs to.
    public int A { get; init; }
    public int Area { get; init; }
    public int B { get; init; }
    public int C { get; init; }

    // Statics resolved at attach. Zero means the AOB scan found nothing.
    public long FieldAreaStatic { get; init; }
    public long EventFlagManStatic { get; init; }

    // The FieldArea walk, which maps an area to its storage category.
    public long FieldArea { get; init; }
    public long WorldInfoOwner { get; init; }
    public int WorldBlockCount { get; init; }
    public long WorldBlockVector { get; init; }
    public int BlockCount { get; init; }
    public long BlockBase { get; init; }
    public int Category { get; init; } = -1;

    // The bit table itself.
    public long EventFlagMan { get; init; }
    public long Table { get; init; }
    public long Bucket { get; init; }
    public long ResultBase { get; init; }
    public int WordOffset { get; init; }
    public uint Word { get; init; }
    public int Bit { get; init; }
}
