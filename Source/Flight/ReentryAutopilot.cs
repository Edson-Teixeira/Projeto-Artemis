using System;
using UnityEngine;
using Artemis.Telemetry;
using Artemis.Systems;
using MuMech;

namespace Artemis.Flight
{
    public enum ReentryPhase
    {
        Idle,               // Desligado
        HoldingRetrograde,  // Travando vetor Retrograde para orientar o escudo térmico
        AtmosphericBraking, // Reentrada na atmosfera (absorvendo calor)
        Parachutes,         // Acionamento escalonado (MK-12 Drogues -> Mains)
        Landed              // Pouso realizado
    }

    /// <summary>
    /// ReentryAutopilot: Gerencia a reentrada segura na atmosfera.
    /// Trava a nave no vetor Retrograde, protege os sistemas e aciona
    /// sequencialmente os paraquedas até o toque no solo ou mar.
    /// </summary>
    public class ReentryAutopilot
    {
        public ReentryPhase CurrentPhase { get; private set; } = ReentryPhase.Idle;
        public bool IsActive => CurrentPhase != ReentryPhase.Idle && CurrentPhase != ReentryPhase.Landed;
        public string StatusMessage { get; private set; } = "Pronto para reentrada";

        public void StartReentry(TelemetryReader telemetry)
        {
            CurrentPhase = ReentryPhase.HoldingRetrograde;
            StatusMessage = "Travando no vetor Retrograde...";

            // Ativa o SAS e orienta para o vetor retrógrado (escudo térmico contra o vento)
            LockRetrograde(telemetry);

            ScreenMessages.PostScreenMessage("[Artemis] Reentrada iniciada: Orientando escudo em Retrograde!", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void Abort(TelemetryReader telemetry)
        {
            CurrentPhase = ReentryPhase.Idle;
            StatusMessage = "Reentrada abortada.";
            ReleaseControl(telemetry);
        }

        /// <summary>
        /// Atualizado a cada frame de física (FixedUpdate).
        /// </summary>
        public void Update(TelemetryReader telemetry, ParachuteManager parachutes)
        {
            if (!IsActive || telemetry.Vessel == null) return;

            // Mantém sempre o SAS travado em Retrograde durante a queda
            LockRetrograde(telemetry);

            // Verifica se tocou no solo ou água
            if (telemetry.Vessel.Landed || telemetry.Vessel.Splashed)
            {
                CurrentPhase = ReentryPhase.Landed;
                StatusMessage = "Pouso concluído com sucesso!";
                ScreenMessages.PostScreenMessage("[Artemis] Pouso confirmado!", 5f, ScreenMessageStyle.UPPER_CENTER);
                ReleaseControl(telemetry);
                return;
            }

            // Atualiza o gerenciador de paraquedas (MK-12 Drogue primeiro, depois Mains)
            parachutes.Update(telemetry);

            if (parachutes.MainsDeployed)
            {
                StatusMessage = "Descida sob paraquedas principais...";
                CurrentPhase = ReentryPhase.Parachutes;
            }
            else if (parachutes.DroguesDeployed)
            {
                StatusMessage = "Frenagem com paraquedas Drogue (MK-12)...";
                CurrentPhase = ReentryPhase.Parachutes;
            }
            else if (telemetry.IsInAtmosphere)
            {
                CurrentPhase = ReentryPhase.AtmosphericBraking;
                StatusMessage = $"Freando na atmosfera: Alt {telemetry.AltitudeASL/1000:F1}km | Vel {telemetry.SurfaceSpeed:F0}m/s";
            }
        }

        private void LockRetrograde(TelemetryReader telemetry)
        {
            if (telemetry.Vessel == null) return;

            // Ativa o SAS nativo do KSP e define para Retrograde
            telemetry.Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            if (telemetry.Vessel.Autopilot != null && telemetry.Vessel.Autopilot.CanSetMode(VesselAutopilot.AutopilotMode.Retrograde))
            {
                telemetry.Vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.Retrograde);
            }
        }

        private void ReleaseControl(TelemetryReader telemetry)
        {
            if (telemetry.Vessel != null && telemetry.Vessel.Autopilot != null)
            {
                telemetry.Vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
            }
        }
    }
}
