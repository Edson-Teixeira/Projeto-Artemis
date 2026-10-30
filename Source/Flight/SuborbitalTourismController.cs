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
        Ascent,             // Lançamento com curva gravitacional até atingir Apoastro de 75km
        CoastingToSpace,    // Motores cortados, cruza 70km (validação turística no vácuo)
        Reentry,            // Reentrada ativa no modo Lander (Retrograde Roll 0° e Paraquedas)
        Completed           // Missão concluída com pouso suave
    }

    /// <summary>
    /// SuborbitalTourismController: Gerenciador autônomo de missões de Turismo Suborbital.
    /// Unifica o lançamento em arco balístico com o retorno no modo Lander:
    /// 1. Decola e executa curva gravitacional inclinada até 45° a Leste (dando rampa aerodinâmica de frenagem).
    /// 2. Corta motores ao atingir 75km de Apoastro.
    /// 3. Passa dos 70km no espaço, cumprindo o contrato de turismo.
    /// 4. Transiciona automaticamente para Reentrada Lander (Retrograde com Roll 0° e paraquedas escalonados).
    /// </summary>
    public class SuborbitalTourismController
    {
        public SuborbitalTourismPhase CurrentPhase { get; private set; } = SuborbitalTourismPhase.Idle;
        public bool IsActive => CurrentPhase != SuborbitalTourismPhase.Idle && CurrentPhase != SuborbitalTourismPhase.Completed;
        public string StatusMessage { get; private set; } = "Pronto para iniciar Turismo Suborbital";

        private float _commandedThrottle = 0f;
        public float CommandedThrottle => _commandedThrottle;

        private const double TARGET_APOAPSIS = 75000.0; // 75 km

        public void StartMission()
        {
            CurrentPhase = SuborbitalTourismPhase.Ascent;
            _commandedThrottle = 1.0f;
            StatusMessage = "Missão iniciada: Decolando com curva balística...";
            ScreenMessages.PostScreenMessage("[Artemis] Missão Turismo Suborbital Iniciada!", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void Abort(TelemetryReader telemetry, ReentryAutopilot reentry)
        {
            CurrentPhase = SuborbitalTourismPhase.Idle;
            _commandedThrottle = 0f;
            StatusMessage = "Missão abortada pelo jogador.";
            ReleaseAttitude(telemetry);
            if (reentry != null && reentry.IsActive)
            {
                reentry.Abort(telemetry);
            }
        }

        /// <summary>
        /// Loop físico executado a cada frame de física pelo ArtemisCore.
        /// </summary>
        public void Update(TelemetryReader telemetry, AutoStaging staging, DeployableManager deployables, ParachuteManager parachutes, ReentryAutopilot reentry)
        {
            if (!IsActive || telemetry.Vessel == null) return;

            // Mantém partes retráteis gerenciadas
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
        /// Fase 1: Subida com curva gravitacional pronunciada em direção a Leste (90°).
        /// Inclina o nariz até ~45° para que a reentrada seja oblíqua, permitindo que a atmosfera
        /// freie a velocidade hipersônica suavemente antes dos paraquedas abrirem.
        /// </summary>
        private void HandleAscent(TelemetryReader telemetry, AutoStaging staging)
        {
            _commandedThrottle = 1.0f;
            staging.Update(telemetry, _commandedThrottle);

            double alt = telemetry.AltitudeASL;
            double turnStart = 500.0;
            double turnEnd = 40000.0;

            // Inicia subida reta e curva progressivamente até 45°
            double pitch = 90.0;
            if (alt >= turnStart)
            {
                double progress = Mathf.Clamp01((float)((alt - turnStart) / (turnEnd - turnStart)));
                // Inclina até 45° no pico da queima
                pitch = 90.0 - (Math.Pow(progress, 0.65) * 45.0);
            }

            SetAttitude(telemetry, 90.0, pitch, 0.0);

            StatusMessage = $"Subida em Arco: Pitch {pitch:F0}° | Ap {telemetry.Apoapsis / 1000:F1}km / 75km";

            // Ao atingir 75km de Apoastro, desliga os motores e corta imediatamente
            if (telemetry.Apoapsis >= TARGET_APOAPSIS)
            {
                _commandedThrottle = 0f;
                CurrentPhase = SuborbitalTourismPhase.CoastingToSpace;
                StatusMessage = "Apoastro de 75km alcançado! Cortando motores e planando até o vácuo...";
                ScreenMessages.PostScreenMessage("[Artemis Turismo] Motores cortados a 75km de Apoastro!", 4f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Fase 2: Planando acima de 70km (validação turística no vácuo).
        /// Já prepara a atitude em Retrograde para que a reentrada seja perfeitamente estável.
        /// </summary>
        private void HandleCoasting(TelemetryReader telemetry, ReentryAutopilot reentry)
        {
            _commandedThrottle = 0f;

            // No vácuo, já orienta o escudo para Retrograde com Roll em 0°
            reentry.LockRetrograde(telemetry);

            if (!telemetry.IsInAtmosphere)
            {
                StatusMessage = $"No Espaço (>70km)! Ap: {telemetry.Apoapsis / 1000:F1}km (Turismo cumprido)";
            }
            else
            {
                StatusMessage = $"Subindo por inércia: Alt {telemetry.AltitudeASL / 1000:F1}km...";
            }

            // Dispara a reentrada assim que passar do topo (velocidade vertical negativa)
            if (telemetry.VerticalSpeed < 0 || (telemetry.AltitudeASL < 70000.0 && telemetry.VerticalSpeed < -5.0))
            {
                CurrentPhase = SuborbitalTourismPhase.Reentry;
                reentry.StartReentry(telemetry);
                StatusMessage = "Iniciando descida: Ativando módulo de Lander (Retrograde)...";
                ScreenMessages.PostScreenMessage("[Artemis Turismo] Ativando Módulo de Reentrada Lander!", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Fase 3: Reentrada atmosférica ativa.
        /// Executa continuamente o ReentryAutopilot para travar Retrograde e abrir os paraquedas.
        /// </summary>
        private void HandleReentry(TelemetryReader telemetry, ParachuteManager parachutes, ReentryAutopilot reentry)
        {
            _commandedThrottle = 0f;

            // Executa a lógica de controle da reentrada a cada frame
            reentry.Update(telemetry, parachutes);

            if (reentry.CurrentPhase == ReentryPhase.Landed)
            {
                CurrentPhase = SuborbitalTourismPhase.Completed;
                StatusMessage = "Missão de Turismo Suborbital concluída com sucesso!";
                ScreenMessages.PostScreenMessage("[Artemis Turismo] Pouso confirmado! Missão cumprida!", 6f, ScreenMessageStyle.UPPER_CENTER);
                ReleaseAttitude(telemetry);
                return;
            }

            StatusMessage = $"Descida Lander: {reentry.StatusMessage}";
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
