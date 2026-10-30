using System;
using UnityEngine;
using KSP.UI.Screens;
using Artemis.Telemetry;

namespace Artemis.Flight
{
    /// <summary>
    /// AutoStaging: Responsável por soltar os estágios vazios e ligar o próximo.
    /// Monitora o esgotamento dos motores ativos (flameout) e aciona a separação
    /// com uma pequena pausa de segurança (debounce) para não disparar vários estágios juntos.
    /// </summary>
    public class AutoStaging
    {
        public bool Enabled { get; set; } = true;

        // Tempo mínimo entre ativações de estágio (para estabilização física)
        private const double STAGE_COOLDOWN = 1.0; 
        private double _lastStageTime = 0.0;

        public void Reset()
        {
            _lastStageTime = Planetarium.GetUniversalTime();
        }

        /// <summary>
        /// Verifica a cada frame de física se é necessário desacoplar e avançar o estágio.
        /// </summary>
        public void Update(TelemetryReader telemetry, float currentThrottle)
        {
            if (!Enabled || telemetry.Vessel == null) return;

            // Não aciona se já chegamos no último estágio
            if (StageManager.CurrentStage <= 0) return;

            double now = Planetarium.GetUniversalTime();
            if (now - _lastStageTime < STAGE_COOLDOWN) return;

            // Condição 1: Algum motor ativo apagou por falta de combustível (flameout)
            bool flameoutDetected = telemetry.HasFlameout;

            // Condição 2: Estamos tentando acelerar (throttle > 0.1), mas o empuxo zerou
            bool thrustLost = (currentThrottle > 0.1f && telemetry.CurrentThrust <= 0.01f);

            if (flameoutDetected || thrustLost)
            {
                TriggerStage();
                _lastStageTime = now;
            }
        }

        private void TriggerStage()
        {
            Debug.Log($"[Artemis] AutoStaging: Ejetando estágio vazio. Novo estágio: {StageManager.CurrentStage - 1}");
            ScreenMessages.PostScreenMessage("[Artemis] Separação de Estágio!", 2.5f, ScreenMessageStyle.UPPER_CENTER);
            StageManager.ActivateNextStage();
        }
    }
}
