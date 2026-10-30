using System;
using UnityEngine;
using Artemis.Telemetry;
using Artemis.Systems;
using MuMech;

namespace Artemis.Flight
{
    public enum SuborbitalTourismPhase
    {
        Idle,               // Desligado
        Ascent,             // Lançamento com motores a 100% até atingir Apoastro no espaço (>75km)
        CoastingToSpace,    // Motores cortados, aguarda cruzar 70km para validar turismo suborbital
        Reentry,            // Aciona o módulo Lander (Retrograde com Roll 0° e Paraquedas)
        Completed           // Missão de turismo concluída com sucesso
    }

    /// <summary>
    /// SuborbitalTourismController: Gerenciador de missões de Turismo Suborbital.
    /// Unifica o módulo de Lançamento e o módulo de Lander:
    /// 1. Decola com aceleração total mantendo Roll em 0°.
    /// 2. Queima até o Apoastro atingir 75km (vácuo de Kerbin) e corta os motores.
    /// 3. Passa dos 70km cumprindo o requisito de turismo suborbital no espaço.
    /// 4. Inicia automaticamente a reentrada no modo Lander (Retrograde + Paraquedas sequenciais).
    /// </summary>
    public class SuborbitalTourismController
    {
        public SuborbitalTourismPhase CurrentPhase { get; private set; } = SuborbitalTourismPhase.Idle;
        public bool IsActive => CurrentPhase != SuborbitalTourismPhase.Idle && CurrentPhase != SuborbitalTourismPhase.Completed;
        public string StatusMessage { get; private set; } = "Pronto para iniciar Turismo Suborbital";

        private float _commandedThrottle = 0f;
        public float CommandedThrottle => _commandedThrottle;

        private const double TARGET_APOAPSIS = 75000.0; // 75 km (garante que passa da atmosfera de 70km)

        public void StartMission()
        {
            CurrentPhase = SuborbitalTourismPhase.Ascent;
            _commandedThrottle = 1.0f;
            StatusMessage = "Missão iniciada: Decolando em direção ao espaço...";
            ScreenMessages.PostScreenMessage("[Artemis] Missão Turismo Suborbital Iniciada!", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void Abort(TelemetryReader telemetry, ReentryAutopilot reentry)
        {
            CurrentPhase = SuborbitalTourismPhase.Idle;
            _commandedThrottle = 0f;
            StatusMessage = "Missão abortada pelo jogador.";
            ReleaseAttitude(telemetry);
            if (reentry.IsActive)
            {
                reentry.Abort(telemetry);
            }
        }

        /// <summary>
        /// Loop físico executado a cada FixedUpdate pelo ArtemisCore.
        /// </summary>
        public void Update(TelemetryReader telemetry, AutoStaging staging, DeployableManager deployables, ParachuteManager parachutes, ReentryAutopilot reentry)
        {
            if (!IsActive || telemetry.Vessel == null) return;

            // 1. Atualiza partes retráteis continuamente
            deployables.Update(telemetry);

            switch (CurrentPhase)
            {
                case SuborbitalTourismPhase.Ascent:
                    HandleAscent(telemetry, staging);
                    break;

                case SuborbitalTourismPhase.CoastingToSpace:
                    HandleCoasting(telemetry, reentry);
                    break;

                case SuborbitalTourismPhase.Reentry:
                    HandleReentry(telemetry, parachutes, reentry);
                    break;
            }
        }

        /// <summary>
        /// Fase 1: Subida inicial até o Apoastro atingir 75km.
        /// Mantém Roll em 0° e leve inclinação a Leste (pitch ~80°) para amortecer a descida no mar.
        /// </summary>
        private void HandleAscent(TelemetryReader telemetry, AutoStaging staging)
        {
            _commandedThrottle = 1.0f;
            staging.Update(telemetry, _commandedThrottle);

            // Subida vertical até 500m, depois leve inclinação para 80° a Leste
            double pitch = telemetry.AltitudeASL < 500.0 ? 90.0 : 80.0;
            SetAttitude(telemetry, 90.0, pitch, 0.0);

            StatusMessage = $"Subindo: Ap {telemetry.Apoapsis/1000:F1}km / Alvo {TARGET_APOAPSIS/1000:F0}km";

            // Ao atingir o apoastro alvo, corta os motores imediatamente
            if (telemetry.Apoapsis >= TARGET_APOAPSIS)
            {
                _commandedThrottle = 0f;
                CurrentPhase = SuborbitalTourismPhase.CoastingToSpace;
                StatusMessage = "Apoastro de 75km alcançado! Cortando motores e planando até o vácuo...";
                ScreenMessages.PostScreenMessage("[Artemis Turismo] Motores cortados! Subindo até o vácuo.", 4f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Fase 2: Planando acima de 70km (validação do turismo suborbital).
        /// </summary>
        private void HandleCoasting(TelemetryReader telemetry, ReentryAutopilot reentry)
        {
            _commandedThrottle = 0f;

            // Mantém apontado para cima com Roll 0° enquanto sobe
            SetAttitude(telemetry, 90.0, 85.0, 0.0);

            if (!telemetry.IsInAtmosphere)
            {
                StatusMessage = $"No espaço (>70km)! Ap: {telemetry.Apoapsis/1000:F1}km (Turismo cumprido)";
            }
            else
            {
                StatusMessage = $"Planando até o espaço: Alt {telemetry.AltitudeASL/1000:F1}km...";
            }

            // Quando passar do apoastro e começar a descer, ativa automaticamente o módulo de Lander
            if (telemetry.VerticalSpeed < 0 && telemetry.AltitudeASL >= 65000.0)
            {
                CurrentPhase = SuborbitalTourismPhase.Reentry;
                reentry.StartReentry(telemetry);
                StatusMessage = "Iniciando descida: Ativando módulo de Lander (Retrograde)...";
                ScreenMessages.PostScreenMessage("[Artemis Turismo] Ativando Módulo de Reentrada Lander!", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Fase 3: Reentrada atmosférica gerenciada pelo ReentryAutopilot.
        /// </summary>
        private void HandleReentry(TelemetryReader telemetry, ParachuteManager parachutes, ReentryAutopilot reentry)
        {
            _commandedThrottle = 0f;

            // Delega para o piloto de reentrada
            if (reentry.CurrentPhase == ReentryPhase.Landed)
            {
                CurrentPhase = SuborbitalTourismPhase.Completed;
                StatusMessage = "Missão de Turismo Suborbital concluída com sucesso!";
                ScreenMessages.PostScreenMessage("[Artemis Turismo] Pouso confirmado! Missão cumprida!", 6f, ScreenMessageStyle.UPPER_CENTER);
                ReleaseAttitude(telemetry);
                return;
            }

            StatusMessage = $"Reentrada Lander: {reentry.StatusMessage}";
        }

        private void SetAttitude(TelemetryReader telemetry, double heading, double pitch, double roll)
        {
            if (telemetry.Vessel == null) return;

            if (telemetry.Vessel.ActionGroups[KSPActionGroup.SAS])
            {
                telemetry.Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            }

            if (telemetry.MechJeb != null)
            {
                var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeTo(heading, pitch, roll, this);
                }
            }
        }

        private void ReleaseAttitude(TelemetryReader telemetry)
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
            }
        }
    }
}
