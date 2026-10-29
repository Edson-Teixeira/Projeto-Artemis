using System;
using UnityEngine;
using MuMech;

namespace OraculoGuidance.Modules.Ascent
{
    public class AscentController
    {
        public bool IsActive { get; private set; }
        public string StatusMessage { get; private set; } = "Pronto";

        // Parâmetros de lançamento
        public double TargetAltitude = 80000; // 80 km padrão
        public double TargetInclination = 0.0;
        public bool EnableBoosterReserve = false;
        public double ReservedDeltaV = 1200.0; // m/s necessários para RTLS Falcon 9

        private Vessel _vessel;
        private MechJebCore _core;

        public void Initialize(Vessel vessel, MechJebCore core)
        {
            _vessel = vessel;
            _core = core;
        }

        public void StartAscent()
        {
            if (_vessel == null || _core == null)
            {
                StatusMessage = "Erro: MechJebCore não encontrado.";
                return;
            }

            var ascent = _core.GetComputerModule<MechJebModuleAscentAutopilot>();
            if (ascent != null)
            {
                ascent.desiredAltitude = TargetAltitude;
                ascent.desiredInclination = TargetInclination;
                ascent.autoStage = true;
                ascent.Users.Add(this);
                ascent.Activate();

                IsActive = true;
                StatusMessage = "Subindo para órbita alvo...";
                Debug.Log($"[Oraculo] Lançamento vertical iniciado: {TargetAltitude/1000}km a {TargetInclination}°");
            }
            else
            {
                StatusMessage = "Módulo de subida não disponível.";
            }
        }

        public void StopAscent()
        {
            if (_core != null)
            {
                var ascent = _core.GetComputerModule<MechJebModuleAscentAutopilot>();
                if (ascent != null)
                {
                    ascent.Users.Remove(this);
                    ascent.Deactivate();
                }
            }

            IsActive = false;
            StatusMessage = "Interrompido";
        }

        public void Update()
        {
            if (!IsActive || _vessel == null) return;

            // Verificação de reserva de combustível para recuperação do primeiro estágio
            if (EnableBoosterReserve)
            {
                CheckBoosterMECO();
            }

            // Checagem se atingiu o apoastro desejado
            if (_vessel.orbit.ApA >= TargetAltitude * 0.98)
            {
                StatusMessage = "Apoastro atingido. Aguardando circularização.";
            }
        }

        private void CheckBoosterMECO()
        {
            // Pega o deltaV restante no estágio atual
            double currentStageDeltaV = GetCurrentStageDeltaV();

            if (currentStageDeltaV <= ReservedDeltaV && currentStageDeltaV > 0)
            {
                Debug.Log($"[Oraculo] MECO disparado! Combustível restante ({currentStageDeltaV:F1} m/s) atingiu a reserva ({ReservedDeltaV:F1} m/s).");
                StatusMessage = "MECO: Reserva atingida. Desacoplando estágio...";

                // Corta aceleração
                FlightInputHandler.state.mainThrottle = 0f;

                // Desativa a subida temporariamente
                StopAscent();

                // Dispara próximo estágio (separação)
                StageManager.ActivateNextStage();
            }
        }

        private double GetCurrentStageDeltaV()
        {
            if (_vessel == null || _vessel.VesselDeltaV == null) return 0;
            var stageInfo = _vessel.VesselDeltaV.GetStage(_vessel.currentStage);
            return stageInfo != null ? stageInfo.deltavActual : 0;
        }
    }
}
