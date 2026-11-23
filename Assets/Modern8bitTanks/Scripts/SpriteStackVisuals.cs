
using UnityEngine;

namespace Modern8bitTanks
{
    public sealed class SpriteStackVisuals : MonoBehaviour
    {
        [SerializeField] private SpriteStackLayer layer;
        [SerializeField] private SpriteRenderer contactShadow;
        [SerializeField] private SpriteRenderer castShadow;

        public SpriteStackLayer Layer => layer;
        public SpriteRenderer ContactShadow => contactShadow;
        public SpriteRenderer CastShadow => castShadow;
    }
}
