
using UnityEngine;

namespace Modern8bitTanks
{
    public sealed class SpriteStackLayer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer slice;
        [SerializeField] private SpriteRenderer fill;
        [SerializeField] private SpriteRenderer outline;

        public SpriteRenderer Slice => slice;
        public SpriteRenderer Fill => fill;
        public SpriteRenderer Outline => outline;
    }
}
