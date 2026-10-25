namespace HuntModelSwapCN.Core;

// Map only the current owned visual. Never dereference a candidate supplied by
// the targeting hook, and never redirect a recycled local-actor address.
public sealed class LinkedDummyTarget(LinkedDummyPreview link, LocalPreview preview, IActorAccess actors)
{
    public nint Redirect(nint candidate)
    {
        if (candidate == 0 || link.Visual is not { } visual || visual.Address != candidate
            || link.Source is not { } source || !preview.OwnsLive(visual)) return candidate;
        return actors.Exists(source) ? source.Address : 0;
    }
}
