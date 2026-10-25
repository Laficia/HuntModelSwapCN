namespace HuntModelSwapCN.Core;

public sealed class RefreshGate(int intervalMs)
{
    private long next;
    private long revision = long.MinValue;
    public bool Due(long now, long currentRevision)
    {
        if (now < next && revision == currentRevision) return false;
        revision = currentRevision;
        next = now + intervalMs;
        return true;
    }
    public void Reset() { next = 0; revision = long.MinValue; }
}
