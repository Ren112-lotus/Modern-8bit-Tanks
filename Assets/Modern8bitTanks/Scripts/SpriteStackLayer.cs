
using UnityEngine;

namespace Modern8bitTanks
{
    public sealed class SpriteStackLayer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer slice;
        [SerializeField] private SpriteRenderer fill;

        public SpriteRenderer Slice => slice;
        public SpriteRenderer Fill => fill;
    }
}
