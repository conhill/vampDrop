using UnityEngine;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Optional per-piece override for <see cref="PuzzlePropDresser"/>.
    ///
    /// The dresser normally picks a prop bucket from the piece's silhouette (a long thin
    /// cube becomes a beam, a wide slab becomes a shelf). Drop this on a single
    /// 'smallwall' cube when you want that one piece dressed as something specific —
    /// "this one's the lamp post" — without touching how any other piece is chosen.
    ///
    /// Purely additive: pieces without this component fall back to the automatic rule,
    /// so an undressed puzzle prefab keeps working exactly as before.
    /// </summary>
    [DisallowMultipleComponent]
    public class PuzzlePropStyle : MonoBehaviour
    {
        [Tooltip("Skip this piece entirely — it keeps its original renderer and is left bare.")]
        public bool LeaveBare = false;

        [Tooltip("Force this piece into a specific bucket instead of inferring from its shape.")]
        public bool OverrideBucket = false;

        [Tooltip("Bucket to draw from when OverrideBucket is on.")]
        public PuzzlePropDresser.PropBucket Bucket = PuzzlePropDresser.PropBucket.LongThin;

        [Tooltip("Pick this exact index within the bucket instead of rolling. -1 = roll normally.")]
        public int ForcePropIndex = -1;
    }
}
