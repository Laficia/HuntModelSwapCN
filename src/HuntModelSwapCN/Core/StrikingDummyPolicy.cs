namespace HuntModelSwapCN.Core;

public static class StrikingDummyPolicy
{
    // BNpcName rows verified against the installed CN client. Use the original
    // identity, since GameObject.Name is already changed by an active swap.
    public static bool IsDummy(uint nameId) => nameId is 541 or 13078;

    public static bool NeedsRedraw(uint nameId, int modelId, int secondaryModelId,
        bool transformed, VisualState applied)
        => IsDummy(nameId) && !transformed
            && (modelId != applied.ModelId || secondaryModelId != applied.SecondaryModelId);
}
