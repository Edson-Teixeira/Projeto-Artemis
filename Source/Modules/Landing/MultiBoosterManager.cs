using System;
using System.Collections.Generic;
using UnityEngine;
using OraculoGuidance.Modules.Physics;

namespace OraculoGuidance.Modules.Landing
{
    public struct LandingPad
    {
        public string Name;
        public double Latitude;
        public double Longitude;

        public LandingPad(string name, double lat, double lon)
        {
            Name = name;
            Latitude = lat;
            Longitude = lon;
        }
    }

    /// <summary>
    /// Gerenciador de múltiplos boosters (Falcon Heavy / 2 ou 4 boosters).
    /// Coordena a distribuição de plataformas e o voo paralelo de cada estágio.
    /// </summary>
    public class MultiBoosterManager
    {
        public static MultiBoosterManager Instance { get; private set; }

        public bool AutoCaptureSeparatedBoosters = true;
        public List<BoosterFlightTracker> ActiveBoosters = new List<BoosterFlightTracker>();

        // Zonas de pouso dedicadas para evitar colisão entre múltiplos foguetes
        private readonly LandingPad[] _availablePads = new[]
        {
            new LandingPad("KSC LZ-1", -0.0972, -74.5577),
            new LandingPad("KSC LZ-2", -0.0972, -74.5200),
            new LandingPad("KSC Pista 09", -0.0486, -74.7245),
            new LandingPad("KSC Pista 27", -0.0486, -74.6000)
        };

        public MultiBoosterManager()
        {
            Instance = this;
            GameEvents.onVesselCreate.Add(OnVesselCreated);
        }

        public void Dispose()
        {
            GameEvents.onVesselCreate.Remove(OnVesselCreated);
            ClearAll();
        }

        public void ClearAll()
        {
            foreach (var tracker in ActiveBoosters)
            {
                tracker.Unhook();
            }
            ActiveBoosters.Clear();
        }

        /// <summary>
        /// Detecta automaticamente quando um ou múltiplos boosters são desacoplados
        /// </summary>
        private void OnVesselCreated(Vessel newVessel)
        {
            if (!AutoCaptureSeparatedBoosters || newVessel == null) return;

            // Se o novo vaso for um fragmento descartado (Debris/Probe) durante voo atmosférico/suborbital
            if (newVessel.situation == Vessel.Situations.FLYING || newVessel.situation == Vessel.Situations.SUB_ORBITAL)
            {
                // Verifica se tem motores e combustível (ou seja, é um booster e não apenas uma coifa descartada)
                if (HasEnginesAndFuel(newVessel))
                {
                    RegisterBooster(newVessel);
                }
            }
        }

        public void RegisterBooster(Vessel vessel)
        {
            if (vessel == null) return;

            // Estende a bolha de física imediatamente para 250 km
            PhysicsRangeManager.ExtendPhysicsRange(vessel);
            if (FlightGlobals.ActiveVessel != null)
            {
                PhysicsRangeManager.ExtendPhysicsRange(FlightGlobals.ActiveVessel);
            }

            int index = ActiveBoosters.Count % _availablePads.Length;
            LandingPad pad = _availablePads[index];

            var tracker = new BoosterFlightTracker(vessel, pad.Name, pad.Latitude, pad.Longitude);
            ActiveBoosters.Add(tracker);

            ScreenMessages.PostScreenMessage(
                $"<color=#33ccff>[Oráculo] Booster #{ActiveBoosters.Count} ({vessel.vesselName}) assumido! Alvo: {pad.Name}</color>",
                6f,
                ScreenMessageStyle.UPPER_CENTER
            );
        }

        public void Update()
        {
            // Atualiza o ciclo de vida de cada booster ativo
            for (int i = ActiveBoosters.Count - 1; i >= 0; i--)
            {
                var tracker = ActiveBoosters[i];
                tracker.Update();

                // Se o booster terminou seu pouso ou foi recuperado, mantemos no log ou removemos se inativo
                if (tracker.IsFinished)
                {
                    // Mantém registro para status na UI
                }
            }
        }

        private bool HasEnginesAndFuel(Vessel v)
        {
            var engines = v.FindPartModulesImplementing<ModuleEngines>();
            var enginesFX = v.FindPartModulesImplementing<ModuleEnginesFX>();
            return (engines.Count > 0 || enginesFX.Count > 0);
        }

        public string GetSummary()
        {
            if (ActiveBoosters.Count == 0) return "Nenhum booster em recuperação no momento.";

            int flying = 0;
            int landed = 0;
            foreach (var b in ActiveBoosters)
            {
                if (b.IsFinished) landed++;
                else flying++;
            }

            return $"Boosters: {ActiveBoosters.Count} total ({flying} voando, {landed} pousados)";
        }
    }
}
