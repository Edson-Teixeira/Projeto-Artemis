using System;
using UnityEngine;
using Artemis.Telemetry;
using Artemis.Systems;
using KSP.UI.Screens;
using MuMech;

namespace Artemis.Flight
{
    public enum SuborbitalTourismPhase
    {
        Idle,               // Desligado
        Ascent,             // Lançamento com curva balística íngreme até atingir Apoastro de 75km
        CoastingToSpace,    // Motores cortados, cruza 70km, libera o booster no vácuo e prepara reentrada
        Reentry,            // Reentrada ativa no modo Lander (Retrograde Roll 0° e Paraquedas)
        Completed           // Missão concluída com pouso suave
    }

    /// <summary>
    /// SuborbitalTourismController: Gerenciador autônomo de missões de Turismo Suborbital.
    /// Unifica o lançamento em arco balístico com o retorno no modo Lander:
    /// 1. Decola e executa curva gravitacional íngreme (inclinando suavemente até ~58° a Leste).
    /// 2. Corta motores ao atingir 75km de Apoastro.
    /// 3. Ao chegar no espaço (>= 70km), desacopla OBRIGATORIAMENTE o booster usado na subida.
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
        private bool _boosterReleased = false;

        public void StartMission()
        {
            CurrentPhase = SuborbitalTourismPhase.Ascent;
            _commandedThrottle = 1.0f;
            _boosterReleased = false;
            StatusMessage = "Missão iniciada: Decolando com subida íngreme em arco...";
            ScreenMessages.PostScreenMessage("[Artemis] Missão Turismo Suborbital Iniciada!", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void Abort(TelemetryReader telemetry, ReentryAutopilot reentry)
        {
            CurrentPhase = SuborbitalTourismPhase.Idle;
            _commandedThrottle = 0f;
            _boosterReleased = false;
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
        /// Fase 1: Subida com curva gravitacional mais íngreme (~58° de pitch máximo).
        /// Mantém boa velocidade vertical para atingir 75km rapidamente, com arco suficiente para frenagem atmosférica.
        /// </summary>
        private void HandleAscent(TelemetryReader telemetry, AutoStaging staging)
        {
            _commandedThrottle = 1.0f;
            staging.Update(telemetry, _commandedThrottle);

            double alt = telemetry.AltitudeASL;
            double turnStart = 500.0;
            double turnEnd = 40000.0;

            // Inicia a curva aos 500m, porém mais íngreme (inclina no máximo até 58°)
            double pitch = 90.0;
            if (alt >= turnStart)
            {
                double progress = Mathf.Clamp01((float)((alt - turnStart) / (turnEnd - turnStart)));
                // Inclina até 58° de pitch (90 - 32)
                pitch = 90.0 - (Math.Pow(progress, 0.65) * 32.0);
            }

            SetAttitude(telemetry, 90.0, pitch, 0.0);

            StatusMessage = $"Subida Íngreme: Pitch {pitch:F0}° | Ap {telemetry.Apoapsis / 1000:F1}km / 75km";

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
        /// Desacopla o booster de subida e prepara a atitude em Retrograde para reentrada estável.
        /// </summary>
        private void HandleCoasting(TelemetryReader telemetry, ReentryAutopilot reentry)
        {
            _commandedThrottle = 0f;

            // Desacopla o booster assim que cruzar a fronteira do espaço (>= 70km)
            if (telemetry.AltitudeASL >= 70000.0 && !_boosterReleased)
            {
                _boosterReleased = true;
                ReleaseBooster(telemetry);
            }

            // No vácuo, orienta o escudo para Retrograde com Roll em 0°
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
                // Garante que o booster foi solto antes de iniciar a queda
                if (!_boosterReleased)
                {
                    _boosterReleased = true;
                    ReleaseBooster(telemetry);
                }

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

        /// <summary>
        /// Desacopla o booster de subida utilizado para levar a cápsula ao espaço.
        /// Aciona tanto o StageManager quanto qualquer desacoplador ainda acoplado.
        /// </summary>
        private void ReleaseBooster(TelemetryReader telemetry)
        {
            if (telemetry.Vessel == null) return;

            _commandedThrottle = 0f;

            Debug.Log("[Artemis Turismo] Desacoplando booster de lançamento no espaço!");
            ScreenMessages.PostScreenMessage("[Artemis Turismo] Desacoplando Booster de Subida no Espaço!", 5f, ScreenMessageStyle.UPPER_CENTER);

            // 1. Aciona o estágio no StageManager se houver estágios anteriores (CurrentStage > 0)
            if (StageManager.CurrentStage > 0)
            {
                StageManager.ActivateNextStage();
            }

            // 2. Aciona diretamente todos os desacopladores ainda presos na nave
            var decouplers = telemetry.Vessel.FindPartModulesImplementing<ModuleDecouple>();
            for (int i = 0; i < decouplers.Count; i++)
            {
                if (!decouplers[i].isDecoupled)
                {
                    decouplers[i].Decouple();
                }
            }

            var anchoredDecouplers = telemetry.Vessel.FindPartModulesImplementing<ModuleAnchoredDecoupler>();
            for (int i = 0; i < anchoredDecouplers.Count; i++)
            {
                if (!anchoredDecouplers[i].isDecoupled)
                {
                    anchoredDecouplers[i].Decouple();
                }
            }
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
