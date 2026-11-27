using System;
using System.Collections.Generic;

namespace Modern8bitTanks
{
    internal sealed class VehicleCatalogIndex
    {
        internal static readonly VehicleCatalogIndex Empty = new(Array.Empty<VehicleDef>());

        private readonly Dictionary<string, VehicleDef> _byId = new(StringComparer.Ordinal);

        internal VehicleDef[] Vehicles { get; }

        private VehicleCatalogIndex(VehicleDef[] vehicles)
        {
            Vehicles = vehicles;
            foreach (var vehicle in vehicles)
                if (vehicle != null && !string.IsNullOrEmpty(vehicle.id))
                    _byId[vehicle.id] = vehicle;
        }

        internal static VehicleCatalogIndex Load() => new(MetaLoader.LoadVehicleCatalog().vehicles);

        internal VehicleDef Find(string id) =>
            !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var vehicle) ? vehicle : null;

        internal VehicleDef FindOrFirst(string id) => Find(id) ?? (Vehicles.Length > 0 ? Vehicles[0] : null);
    }
}
