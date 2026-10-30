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
        HoldingRetrograde,  // Travando vetor Retrograde com Roll 0° para orientar o escudo térmico
        AtmosphericBraking, // Reentrada na atmosfera (absorvendo calor)
        Parachutes,         // Acionamento escalonado (MK-12 Drogues -> Mains)
        Landed              // Pouso realizado
    }

    /// <summary>
    /// ReentryAutopilot: Gerencia a reentrada segura na atmosfera.
    /// Trava a nave no vetor Retrograde via MechJeb AttitudeController,
    /// mantendo sempre o ROLL em 0° e o SAS nativo desligado para não haver conflitos nem menus piscando.
    /// </summary>
    public class ReentryAutopilot
    {
        public ReentryPhase CurrentPhase { get; private set; } = ReentryPhase.Idle;
        public bool IsActive => CurrentPhase != ReentryPhase.Idle && CurrentPhase != ReentryPhase.Landed;
        public string StatusMessage { get; private set; } = "Pronto para reentrada";

        public void StartReentry(TelemetryReader telemetry)
        {
            CurrentPhase = ReentryPhase.HoldingRetrograde;
            StatusMessage = "Virando traseira para Retrograde (Escudo Térmico, Roll 0°)...";

            LockRetrograde(telemetry);

            ScreenMessages.PostScreenMessage("[Artemis] Reentrada iniciada: Escudo em Retrograde e Roll em 0°!", 5f, ScreenMessageStyle.UPPER_CENTER);
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

            // Mantém a orientação constante em Retrograde com Roll 0°
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

        /// <summary>
        /// Comanda o MechJeb AttitudeController a apontar para o vetor Retrograde com Roll travado em 0°.
        /// Desliga o SAS nativo do KSP para que os atuadores obedeçam sem lutar.
        /// </summary>
        public void LockRetrograde(TelemetryReader telemetry)
        {
            if (telemetry.Vessel == null) return;

            // 1. Desliga o SAS nativo do KSP para não causar pisca-pisca no navball
            if (telemetry.Vessel.ActionGroups[KSPActionGroup.SAS])
            {
                telemetry.Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            }

            // 2. Calcula o vetor retrógrado (oposto à velocidade)
            Vector3d velocity = telemetry.IsInAtmosphere 
                ? telemetry.Vessel.srf_velocity 
                : telemetry.Vessel.obt_velocity;

            Vector3d retrograde = -velocity.normalized;

            // 3. Converte em ângulos de superfície (Heading e Pitch)
            TelemetryReader.VectorToHeadingPitch(telemetry.Vessel, retrograde, out double heading, out double pitch);

            // 4. Aplica no MechJeb com Roll estritamente em 0.0°
            if (telemetry.MechJeb != null)
            {
                var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeTo(heading, pitch, 0.0, this);
                }
            }
        }

        /// <summary>
        /// Devolve o controle manual ao jogador e reativa o SAS nativo.
        /// </summary>
        public void ReleaseControl(TelemetryReader telemetry)
        {
            if (telemetry.MechJeb != null)
            {
                var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeDeactivate();
                }
            }

            if (telemetry.Vessel != null)
            {
                telemetry.Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                if (telemetry.Vessel.Autopilot != null)
                {
                    telemetry.Vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
                }
            }
        }
    }
}
