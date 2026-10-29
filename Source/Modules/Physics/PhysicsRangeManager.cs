using System;
using UnityEngine;

namespace OraculoGuidance.Modules.Physics
{
    /// <summary>
    /// Gerencia a distância da bolha de física (VesselRanges) do KSP.
    /// Configura o limite seguro de 250 km para permitir o pouso simultâneo
    /// de múltiplos boosters sem causar instabilidade física (Kraken).
    /// </summary>
    public static class PhysicsRangeManager
    {
        private static bool _isExtended = false;
        private static VesselRanges _originalRanges = null;

        // Distância expandida de física: 250 km (limite seguro máximo do Unity PhysX no KSP)
        private const float EXTENDED_LOAD = 250000f;
        private const float EXTENDED_UNLOAD = 260000f;
        private const float EXTENDED_PACK = 240000f;
        private const float EXTENDED_UNPACK = 230000f;

        public static bool IsExtended => _isExtended;

        public static void ExtendPhysicsRange(Vessel vessel)
        {
            if (vessel == null) return;

            try
            {
                if (_originalRanges == null && vessel.vesselRanges != null)
                {
                    _originalRanges = new VesselRanges(vessel.vesselRanges);
                }

                if (vessel.vesselRanges != null)
                {
                    // Expande o alcance de voo atmosférico, suborbital e orbital para 250 km
                    ApplyRange(vessel.vesselRanges.flying);
                    ApplyRange(vessel.vesselRanges.subOrbital);
                    ApplyRange(vessel.vesselRanges.orbit);

                    _isExtended = true;
                    Debug.Log("[Oraculo] Bolha de física expandida com segurança para 250 km.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Oraculo] Erro ao estender alcance de física: {ex.Message}");
            }
        }

        public static void RestorePhysicsRange(Vessel vessel)
        {
            if (vessel == null || !_isExtended || _originalRanges == null) return;

            try
            {
                vessel.vesselRanges = new VesselRanges(_originalRanges);
                _isExtended = false;
                Debug.Log("[Oraculo] Bolha de física restaurada para os padrões originais do KSP.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Oraculo] Erro ao restaurar alcance de física: {ex.Message}");
            }
        }

        private static void ApplyRange(VesselRanges.Situation situation)
        {
            if (situation == null) return;
            situation.load = EXTENDED_LOAD;
            situation.unload = EXTENDED_UNLOAD;
            situation.pack = EXTENDED_PACK;
            situation.unpack = EXTENDED_UNPACK;
        }
    }
}
