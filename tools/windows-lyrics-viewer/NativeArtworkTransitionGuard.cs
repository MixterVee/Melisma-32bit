namespace MelismaLyricsViewer;

/// <summary>
/// The native cover may be published before async Foobar song metadata.
/// The second metadata event must NOT reset an already accepted sample or
/// replay its artwork transition. Equality is scoped to the track generation.
/// </summary>
internal static class NativeArtworkTransitionGuard
{
    internal static bool NeedsReset(ulong acceptedGeneration,
        ulong metadataGeneration) => acceptedGeneration != metadataGeneration;

    // A native callback or catalog can repeat identical artwork bytes with
    // a different sequence. Ignore it or it would replace current->current
    // and make the actual old-to-new fade look instantaneous.
    internal static ulong Fingerprint(ReadOnlySpan<byte> bytes)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }
}
