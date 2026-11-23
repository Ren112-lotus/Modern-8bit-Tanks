using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>
    /// Cloud shadows and ambient motes for the current map theme.
    /// </summary>
    public class MapAtmosphere : MonoBehaviour
    {
        private float _worldW;
        private float _worldH;

        private const float CloudTileSize = 44f;
        private const int CloudColumns = 4;
        private Vector2 _cloudOffset;
        [SerializeField] private Transform[] _cloudTileTransforms;
        [SerializeField] private SpriteRenderer[] _moteRenderers;

        private readonly List<DustMote> _motes = new();

        private class DustMote
        {
            public Transform tf;
            public SpriteRenderer sr;
            public Vector2 pos;
            public Vector2 vel;
            public float phase;
            public float baseAlpha;
        }

        public void Initialize(float worldW, float worldH, Sprite whitePixel, Material emissiveMaterial)
        {
            _worldW = worldW;
            _worldH = worldH;
            _cloudOffset = Vector2.zero;
            PositionCloudTiles();

            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_EmissionIntensity", 1.35f);
            _motes.Clear();
            foreach (var renderer in _moteRenderers)
            {
                renderer.sprite = whitePixel;
                if (emissiveMaterial != null)
                {
                    renderer.sharedMaterial = emissiveMaterial;
                    renderer.SetPropertyBlock(properties);
                }

                float alpha = Random.Range(0.20f, 0.45f);
                var position = new Vector2(Random.Range(1f, worldW - 1f), Random.Range(1f, worldH - 1f));
                renderer.color = new Color(1f, 0.93f, 0.72f, alpha);
                renderer.transform.position = new Vector3(position.x, position.y, -1.5f);
                _motes.Add(new DustMote
                {
                    tf = renderer.transform,
                    sr = renderer,
                    pos = position,
                    vel = new Vector2(Random.Range(0.45f, 1.15f), Random.Range(-0.38f, -0.12f)),
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    baseAlpha = alpha
                });
            }
        }

        public void ApplyMapTheme(BattleMap map)
        {
            Color moteCol = map switch
            {
                BattleMap.City => new Color(0.86f, 0.85f, 0.82f, 1f),    // Urban dust & ash
                BattleMap.Forest => new Color(0.92f, 0.98f, 0.68f, 1f),  // Sunlit woodland pollen
                _ => new Color(1f, 0.93f, 0.72f, 1f)                     // Golden desert dust
            };
            for (int i = 0; i < _motes.Count; i++)
            {
                var m = _motes[i];
                if (m.sr != null)
                    m.sr.color = new Color(moteCol.r, moteCol.g, moteCol.b, m.baseAlpha);
            }
        }

        internal void SetVisible(bool visible)
        {
            enabled = visible;
            foreach (var tile in _cloudTileTransforms) tile.gameObject.SetActive(visible);
            foreach (var mote in _moteRenderers) mote.gameObject.SetActive(visible);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            _cloudOffset.x = (_cloudOffset.x + 1.05f * dt) % CloudTileSize;
            _cloudOffset.y = (_cloudOffset.y - 0.50f * dt + CloudTileSize) % CloudTileSize;
            PositionCloudTiles();

            for (int i = 0; i < _motes.Count; i++)
            {
                var m = _motes[i];
                m.phase += dt * 2.0f;
                m.pos += new Vector2(m.vel.x, m.vel.y + Mathf.Sin(m.phase) * 0.16f) * dt;
                if (m.pos.x > _worldW) m.pos.x -= _worldW;
                if (m.pos.y < 0f) m.pos.y += _worldH;

                m.tf.position = new Vector3(m.pos.x, m.pos.y, -1.5f);
                Color c = m.sr.color;
                c.a = m.baseAlpha * (0.65f + 0.35f * Mathf.Sin(m.phase * 1.3f));
                m.sr.color = c;
            }
        }

        private void PositionCloudTiles()
        {
            for (int i = 0; i < _cloudTileTransforms.Length; i++)
            {
                float x = (i % CloudColumns - 0.5f) * CloudTileSize + _cloudOffset.x;
                float y = (i / CloudColumns - 0.3f) * CloudTileSize + _cloudOffset.y;
                _cloudTileTransforms[i].position = new Vector3(x, y, -1.6f);
            }
        }
    }
}
