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
    /// Trava no vetor Retrograde via MechJeb AttitudeController.
    /// Mantém o SAS nativo desligado durante o controle ativo para evitar conflito/piscar de menus.
    /// </summary>
    public class ReentryAutopilot
    {
        public ReentryPhase CurrentPhase { get; private set; } = ReentryPhase.Idle;
        public bool IsActive => CurrentPhase != ReentryPhase.Idle && CurrentPhase != ReentryPhase.Landed;
        public string StatusMessage { get; private set; } = "Pronto para reentrada";

        private AttitudeReference _lastReference = AttitudeReference.INERTIAL;

        public void StartReentry(TelemetryReader telemetry)
        {
            CurrentPhase = ReentryPhase.HoldingRetrograde;
            StatusMessage = "Virando traseira para Retrograde (Escudo Térmico)...";
            _lastReference = AttitudeReference.INERTIAL; // Força reavaliação inicial

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

            // Mantém a orientação constante em Retrograde sem conflito com SAS nativo
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
        /// Comanda o MechJeb AttitudeController a apontar para o vetor Retrograde.
        /// Desliga o SAS nativo para que não haja disputa nem menus piscando.
        /// </summary>
        private void LockRetrograde(TelemetryReader telemetry)
        {
            if (telemetry.Vessel == null) return;

            // 1. Desliga o SAS nativo do KSP para que ele não tente sobrepor o MechJeb
            if (telemetry.Vessel.ActionGroups[KSPActionGroup.SAS])
            {
                telemetry.Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            }

            // 2. Define o referencial: superfície na atmosfera, órbita no vácuo
            AttitudeReference reference = telemetry.IsInAtmosphere 
                ? AttitudeReference.SURFACE_VELOCITY 
                : AttitudeReference.ORBIT;

            // 3. Aplica o comando de atitude para Vector3d.back (Retrograde)
            if (telemetry.MechJeb != null)
            {
                var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    // Envia comando para virar a ré da nave diretamente para a trajetória de velocidade
                    attitude.attitudeTo(Vector3d.back, reference, this, false);
                    _lastReference = reference;
                }
            }
        }

        /// <summary>
        /// Devolve o controle manual ao jogador e reativa o SAS nativo.
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
