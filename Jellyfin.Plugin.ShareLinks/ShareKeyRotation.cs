namespace Jellyfin.Plugin.ShareLinks;

/// <summary>
/// What a key rotation did, in the terms an operator has to be told it in (#28).
/// </summary>
/// <remarks>
/// A rotation is not a maintenance step with no consequence. Every hash in the
/// store was computed under the key that has just been replaced, so every link
/// that was handed out has stopped working, and the number of them is the fact an
/// operator needs and will not have if the call returns nothing.
/// </remarks>
/// <param name="SharesInvalidated">How many live shares stopped resolving because of this rotation.</param>
public sealed record ShareKeyRotation(int SharesInvalidated);
