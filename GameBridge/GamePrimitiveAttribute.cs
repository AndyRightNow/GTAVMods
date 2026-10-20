using System;

namespace GTAVMods.Game
{
    /// <summary>
    /// A direct external runtime operation, requiring the actual host.
    /// Every use must also appear in the reviewed coverage boundary manifest.
    /// No mod decisions, retries, calculations or cleanup belong in these methods.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
    public sealed class GamePrimitiveAttribute : Attribute
    {
    }
}
