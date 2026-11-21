using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    /// <summary>
    /// Subtle natural desert daylight atmosphere (cloud shadows + delicate drifting dust motes):
    /// Keeps the battlefield crisp & clean so URP Post-Processing (Bloom, Vignette, Tilt-Shift DoF)
    /// carries the optical HD-2D look without artificial light overlays.
    /// </summary>
    public class DesertAtmosphere : MonoBehaviour
    {
        private Camera _cam;
        private float _worldW;
        private float _worldH;

        private Transform _cloudRoot;
        private readonly List<Transform> _cloudTiles = new();
        private Texture2D _cloudTex;
        private float _tileW;
        private float _tileH;
        private Vector2 _cloudOffset;

        private readonly List<DustMote> _motes = new();
        private Sprite _moteSprite;

        private class DustMote
        {
            public Transform tf;
            public SpriteRenderer sr;
            public Vector2 pos;
            public Vector2 vel;
            public float phase;
            public float baseAlpha;
        }

        public void Initialize(Camera gameCam, float worldW, float worldH, Sprite whitePxSprite, Material emissiveMat)
        {
            _cam = gameCam;
            _worldW = worldW;
            _worldH = worldH;
            _moteSprite = whitePxSprite;

            BuildCloudShadows();
            BuildSunlitDustMotes(emissiveMat);
        }

        private void BuildCloudShadows()
        {
            const int size = 256;
            _cloudTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size * Mathf.PI * 2f;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size * Mathf.PI * 2f;
                    float n =
                        0.50f * Mathf.Sin(u + v * 0.5f) +
                        0.32f * Mathf.Cos(u * 2f - v + 0.8f) +
                        0.22f * Mathf.Sin(v * 2f + 1.6f) +
                        0.15f * Mathf.Cos(u * 3f + v * 2f);

                    float shade = Mathf.SmoothStep(0.16f, 0.86f, n);
                    byte a = (byte)Mathf.RoundToInt(shade * 22f);
                    px[y * size + x] = new Color32(24, 34, 60, a);
                }
            }
            _cloudTex.SetPixels32(px);
            _cloudTex.Apply(false, false);

            _tileW = 44f;
            _tileH = 44f;
            Sprite cloudSpr = Sprite.Create(_cloudTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / _tileW);

            _cloudRoot = new GameObject("DesertCloudShadows").transform;
            _cloudRoot.SetParent(transform, false);

            // 4x3 tiling grid covers up to 176x132 world units (seamlessly covers 2048x1360 = 128x85 arenas!)
            for (int ty = 0; ty < 3; ty++)
            {
                for (int tx = 0; tx < 4; tx++)
                {
                    var go = new GameObject($"CloudTile_{tx}_{ty}");
                    go.transform.SetParent(_cloudRoot, false);
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = cloudSpr;
                    sr.sortingOrder = 14200;
                    _cloudTiles.Add(go.transform);
                }
            }
            PositionCloudTiles();
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

        private void BuildSunlitDustMotes(Material emissiveMat)
        {
            var root = new GameObject("SunlitDustMotes").transform;
            root.SetParent(transform, false);

            var mpb = new MaterialPropertyBlock();
            mpb.SetFloat("_EmissionIntensity", 1.35f);

            for (int i = 0; i < 36; i++)
            {
                var go = new GameObject($"Mote_{i:00}");
                go.transform.SetParent(root, false);
                go.transform.localScale = new Vector3(0.06f, 0.06f, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _moteSprite;
                sr.sortingOrder = 14900;
                if (emissiveMat != null)
                {
                    sr.sharedMaterial = emissiveMat;
                    sr.SetPropertyBlock(mpb);
                }

                float alpha = Random.Range(0.20f, 0.45f);
                sr.color = new Color(1f, 0.93f, 0.72f, alpha);
                Vector2 p = new Vector2(Random.Range(1f, _worldW - 1f), Random.Range(1f, _worldH - 1f));
                go.transform.position = new Vector3(p.x, p.y, -1.5f);

                _motes.Add(new DustMote
                {
                    tf = go.transform,
                    sr = sr,
                    pos = p,
                    vel = new Vector2(Random.Range(0.45f, 1.15f), Random.Range(-0.38f, -0.12f)),
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    baseAlpha = alpha
                });
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            _cloudOffset.x = (_cloudOffset.x + 1.05f * dt) % _tileW;
            _cloudOffset.y = (_cloudOffset.y - 0.50f * dt + _tileH) % _tileH;
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
            int idx = 0;
            for (int ty = 0; ty < 3; ty++)
            {
                for (int tx = 0; tx < 4; tx++)
                {
                    if (idx >= _cloudTiles.Count) return;
                    float x = (tx - 0.5f) * _tileW + _cloudOffset.x;
                    float y = (ty - 0.3f) * _tileH + _cloudOffset.y;
                    _cloudTiles[idx].position = new Vector3(x, y, -1.6f);
                    idx++;
                }
            }
        }

        private void OnDestroy()
        {
            if (_cloudTex != null) Destroy(_cloudTex);
        }
    }
}
