using System;
using System.Collections.Generic;
using UnityEngine;
using Artemis.Telemetry;

namespace Artemis.Systems
{
    /// <summary>
    /// ParachuteManager: Gerencia a abertura escalonada e 100% segura dos paraquedas.
    /// Regras:
    /// 1º: Se houver paraquedas Drogue (MK-12 / de arrasto), abre em alta velocidade na atmosfera (<= 25km).
    /// 2º: Abre os paraquedas Principais em baixa altitude (<= 2.500m) INDEPENDENTE de ter drogues ou não.
    /// 3º: Trava de emergência: abaixo de 1.200m / 1.000m do relevo, força a abertura de QUALQUER paraquedas guardado.
    /// </summary>
    public class ParachuteManager
    {
        private bool _droguesDeployed = false;
        private bool _mainsDeployed = false;

        public bool DroguesDeployed => _droguesDeployed;
        public bool MainsDeployed => _mainsDeployed;

        public void Reset()
        {
            _droguesDeployed = false;
            _mainsDeployed = false;
        }

        /// <summary>
        /// Atualiza o ciclo de monitoramento de paraquedas durante a descida.
        /// </summary>
        public void Update(TelemetryReader telemetry)
        {
            if (telemetry.Vessel == null) return;

            // Só atuamos se a nave estiver caindo (velocidade vertical negativa)
            if (telemetry.VerticalSpeed >= 0) return;

            // Altitude verdadeira em relação ao solo ou mar (o que for mais próximo)
            double trueAltitude = Math.Min(telemetry.Vessel.radarAltitude, telemetry.AltitudeASL);

            var chutes = telemetry.Vessel.FindPartModulesImplementing<ModuleParachute>();
            if (chutes.Count == 0) return;

            // Verifica se a nave possui paraquedas Drogue (MK-12) instalados
            bool hasDrogues = false;
            for (int i = 0; i < chutes.Count; i++)
            {
                if (IsDrogue(chutes[i]))
                {
                    hasDrogues = true;
                    break;
                }
            }

            // FASE 1: Drogue Chutes (MK-12 e equivalentes de alta velocidade)
            if (hasDrogues && !_droguesDeployed && trueAltitude <= 25000.0)
            {
                if (TryDeployDrogues(chutes))
                {
                    _droguesDeployed = true;
                    ScreenMessages.PostScreenMessage("[Artemis] Paraquedas Drogue (MK-12) armados com sucesso!", 4f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            // FASE 2: Paraquedas Principais (Main Chutes)
            // Dispara abaixo de 2.500m de altitude verdadeira, SEM depender de drogues terem existido ou aberto!
            if (!_mainsDeployed && trueAltitude <= 2500.0)
            {
                if (TryDeployMains(chutes, trueAltitude))
                {
                    _mainsDeployed = true;
                    ScreenMessages.PostScreenMessage("[Artemis] Paraquedas Principais ABERTOS para pouso!", 5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        /// <summary>
        /// Localiza paraquedas drogue e aciona se não estiver em risco imediato de destruição (diferente de UNSAFE).
        /// </summary>
        private bool TryDeployDrogues(List<ModuleParachute> chutes)
        {
            bool anyDeployed = false;
            for (int i = 0; i < chutes.Count; i++)
            {
                var chute = chutes[i];
                if (IsDrogue(chute) && chute.deploymentState == ModuleParachute.deploymentStates.STOWED)
                {
                    // Drogues podem ser acionados em SAFE ou RISKY (eles existem exatamente para frear em alta velocidade)
                    if (chute.deploymentSafeState != ModuleParachute.deploymentSafeStates.UNSAFE)
                    {
                        chute.Deploy();
                        anyDeployed = true;
                    }
                }
            }
            return anyDeployed;
        }

        /// <summary>
        /// Localiza os paraquedas principais e garante sua abertura segura.
        /// </summary>
        private bool TryDeployMains(List<ModuleParachute> chutes, double trueAltitude)
        {
            bool anyDeployed = false;
            for (int i = 0; i < chutes.Count; i++)
            {
                var chute = chutes[i];
                if (!IsDrogue(chute) && chute.deploymentState == ModuleParachute.deploymentStates.STOWED)
                {
                    // Abre se estiver em estado SAFE ou se a altitude estiver em zona crítica (< 1.500m)
                    if (chute.deploymentSafeState == ModuleParachute.deploymentSafeStates.SAFE || trueAltitude <= 1500.0)
                    {
                        chute.Deploy();
                        anyDeployed = true;
                    }
                }
            }

            // Trava de Emergência: abaixo de 1.000m do impacto, força a abertura de QUALQUER paraquedas guardado!
            if (!anyDeployed && trueAltitude <= 1000.0)
            {
                for (int i = 0; i < chutes.Count; i++)
                {
                    if (chutes[i].deploymentState == ModuleParachute.deploymentStates.STOWED)
                    {
                        chutes[i].Deploy();
                        anyDeployed = true;
                    }
                }
            }

            return anyDeployed;
        }

        /// <summary>
        /// Identifica se a peça é um paraquedas do tipo Drogue / MK-12.
        /// </summary>
        private bool IsDrogue(ModuleParachute chute)
        {
            if (chute == null || chute.part == null || chute.part.partInfo == null) return false;
            string name = chute.part.partInfo.name.ToLower();
            string title = chute.part.partInfo.title.ToLower();

            return name.Contains("drogue") || title.Contains("drogue") || name.Contains("mk12") || title.Contains("mk12");
        }
    }
}
