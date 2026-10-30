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
    /// Trava a nave no vetor Retrograde utilizando o piloto de atitude do MechJeb (que não depende de nível de piloto),
    /// protege os sistemas e aciona sequencialmente os paraquedas até o toque no solo ou mar.
    /// </summary>
    public class ReentryAutopilot
    {
        public ReentryPhase CurrentPhase { get; private set; } = ReentryPhase.Idle;
        public bool IsActive => CurrentPhase != ReentryPhase.Idle && CurrentPhase != ReentryPhase.Landed;
        public string StatusMessage { get; private set; } = "Pronto para reentrada";

        public void StartReentry(TelemetryReader telemetry)
        {
            CurrentPhase = ReentryPhase.HoldingRetrograde;
            StatusMessage = "Virando traseira para Retrograde (Escudo Térmico)...";

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

            // Mantém ativamente o comando de atitude em Retrograde
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
        /// Comanda o piloto do MechJeb a virar a nave para o vetor Retrógrado (traseira contra o vento).
        /// </summary>
        private void LockRetrograde(TelemetryReader telemetry)
        {
            if (telemetry.Vessel == null) return;

            // 1. Comando direto via MechJeb AttitudeController (independente de nível de piloto do KSP)
            if (telemetry.MechJeb != null)
            {
                var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    // Na atmosfera usa o vetor de superfície (vento aerodinâmico real)
                    // No vácuo usa o vetor orbital
                    AttitudeReference reference = telemetry.IsInAtmosphere 
                        ? AttitudeReference.SURFACE_VELOCITY 
                        : AttitudeReference.ORBIT;

                    attitude.attitudeTo(Vector3d.back, reference, this, false);
                }

                // Configura também o SmartASS do MechJeb para garantir orientação contínua
                var smartAss = telemetry.MechJeb.GetComputerModule<MechJebModuleSmartASS>();
                if (smartAss != null)
                {
                    smartAss.Engage(true);
                    if (telemetry.IsInAtmosphere)
                    {
                        smartAss.mode = MechJebModuleSmartASS.Mode.SURFACE;
                        smartAss.target = MechJebModuleSmartASS.Target.SURFACE_RETROGRADE;
                    }
                    else
                    {
                        smartAss.mode = MechJebModuleSmartASS.Mode.ORBITAL;
                        smartAss.target = MechJebModuleSmartASS.Target.RETROGRADE;
                    }
                }
            }

            // 2. Redundância: Ativa também o SAS nativo do KSP se suportado pela cápsula
            telemetry.Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            if (telemetry.Vessel.Autopilot != null && telemetry.Vessel.Autopilot.CanSetMode(VesselAutopilot.AutopilotMode.Retrograde))
            {
                telemetry.Vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.Retrograde);
            }
        }

        /// <summary>
        /// Desativa o controle autônomo e devolve os comandos manuais ao jogador.
        /// </summary>
        private void ReleaseControl(TelemetryReader telemetry)
        {
            if (telemetry.MechJeb != null)
            {
                var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeDeactivate();
                }

                var smartAss = telemetry.MechJeb.GetComputerModule<MechJebModuleSmartASS>();
                if (smartAss != null)
                {
                    smartAss.Engage(false);
                    smartAss.target = MechJebModuleSmartASS.Target.OFF;
                }
            }

            if (telemetry.Vessel != null && telemetry.Vessel.Autopilot != null)
            {
                telemetry.Vessel.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
            }
        }
    }
}
