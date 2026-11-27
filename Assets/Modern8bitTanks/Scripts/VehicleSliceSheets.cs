using System.Collections.Generic;
using UnityEngine;

namespace Modern8bitTanks
{
    internal sealed class VehicleSliceSheets
    {
        private readonly Dictionary<string, (Texture2D Hull, Texture2D Turret)> _byId = new();

        internal (Texture2D Hull, Texture2D Turret) Get(string id)
        {
            if (!_byId.TryGetValue(id, out var sheets))
            {
                sheets = Load(id);
                _byId[id] = sheets;
            }
            return sheets;
        }

        internal static (Texture2D Hull, Texture2D Turret) Load(string id) =>
            (Resources.Load<Texture2D>($"Vehicles/{id}_hull_slices"),
             Resources.Load<Texture2D>($"Vehicles/{id}_turret_slices"));
    }
}
