using System;
using System.Collections.Generic;
using UnityEngine;
using Artemis.Telemetry;

namespace Artemis.Systems
{
    /// <summary>
    /// ParachuteManager: Gerencia a abertura escalonada e segura dos paraquedas.
    /// Regra solicitada:
    /// 1º: Abre os paraquedas Drogue (frenagem rápida, como MK-12) assim que for seguro.
    /// 2º: Abre os paraquedas principais (Main Chutes) em menor altitude para pouso final.
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

            // Só atuamos se a nave estiver descendo (velocidade vertical negativa)
            if (telemetry.VerticalSpeed >= 0) return;

            // Fase 1: Drogue Chutes (MK-12 e equivalentes)
            if (!_droguesDeployed)
            {
                if (TryDeployDrogues(telemetry.Vessel))
                {
                    _droguesDeployed = true;
                    ScreenMessages.PostScreenMessage("[Artemis] Paraquedas Drogue (MK-12) armados com segurança!", 4f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            // Fase 2: Main Chutes (Abaixo de 2.000m de altitude verdadeira / segura)
            if (_droguesDeployed && !_mainsDeployed)
            {
                double currentAltitude = telemetry.AltitudeASL;
                // Se a altitude estiver abaixo de 2.500m, tenta abrir os principais
                if (currentAltitude <= 2500.0)
                {
                    if (TryDeployMains(telemetry.Vessel))
                    {
                        _mainsDeployed = true;
                        ScreenMessages.PostScreenMessage("[Artemis] Paraquedas Principais abertos para pouso!", 4f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
            }
        }

        /// <summary>
        /// Localiza paraquedas drogue (MK-12 / de arrasto) e aciona se o estado for SEGURO.
        /// </summary>
        private bool TryDeployDrogues(Vessel vessel)
        {
            var chutes = vessel.FindPartModulesImplementing<ModuleParachute>();
            bool anyDeployed = false;

            for (int i = 0; i < chutes.Count; i++)
            {
                var chute = chutes[i];
                if (IsDrogue(chute))
                {
                    // Verifica se não vai queimar pelo calor/pressão (SAFE)
                    if (chute.deploymentSafeState == ModuleParachute.deploymentSafeStates.SAFE &&
                        chute.deploymentState == ModuleParachute.deploymentStates.STOWED)
                    {
                        chute.Deploy();
                        anyDeployed = true;
                    }
                }
            }

            return anyDeployed;
        }

        /// <summary>
        /// Localiza os paraquedas principais restantes e aciona quando estiver seguro.
        /// </summary>
        private bool TryDeployMains(Vessel vessel)
        {
            var chutes = vessel.FindPartModulesImplementing<ModuleParachute>();
            bool anyDeployed = false;

            for (int i = 0; i < chutes.Count; i++)
            {
                var chute = chutes[i];
                if (!IsDrogue(chute))
                {
                    if (chute.deploymentSafeState == ModuleParachute.deploymentSafeStates.SAFE &&
                        chute.deploymentState == ModuleParachute.deploymentStates.STOWED)
                    {
                        chute.Deploy();
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
