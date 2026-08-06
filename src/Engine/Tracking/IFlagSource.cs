namespace OverlayMod.Engine.Tracking;

/// <summary>
/// Supplies event-flag state to the run tracker (boss-defeat detection). The
/// live implementation is the DS3 reader; tests provide a fake. Kept as an
/// abstraction so the tracker's logic is fully testable with no game attached.
/// </summary>
public interface IFlagSource
{
    bool IsEventFlagSet(uint flagId);
}
