using System;
using System.Collections.Generic;
using UnityEngine;
using Artemis.Telemetry;

namespace Artemis.Systems
{
    /// <summary>
    /// DeployableManager: Gerencia o acionamento de partes móveis ao sair da atmosfera.
    /// Regra solicitada: Ao passar de 75.000m (vácuo de Kerbin), estende painéis solares,
    /// antenas, radiadores e ejeta as coifas aerodinâmicas (fairings).
    /// </summary>
    public class DeployableManager
    {
        private const double DEPLOY_ALTITUDE = 75000.0; // 75 km
        private bool _alreadyDeployed = false;

        public bool IsDeployed => _alreadyDeployed;

        /// <summary>
        /// Reinicia o gatilho (útil para novas decolagens).
        /// </summary>
        public void Reset()
        {
            _alreadyDeployed = false;
        }

        /// <summary>
        /// Executado a cada frame de física pelo ArtemisCore.
        /// </summary>
        public void Update(TelemetryReader telemetry)
        {
            if (_alreadyDeployed || telemetry.Vessel == null) return;

            // Dispara apenas quando ultrapassa a marca dos 75km
            if (telemetry.AltitudeASL >= DEPLOY_ALTITUDE)
            {
                DeployAll(telemetry.Vessel);
                _alreadyDeployed = true;
            }
        }

        /// <summary>
        /// Percorre a nave estendendo todos os equipamentos e soltando coifas.
        /// </summary>
        public void DeployAll(Vessel vessel)
        {
            if (vessel == null) return;

            int panelsCount = 0;
            int antennasCount = 0;
            int radiatorsCount = 0;
            int fairingsCount = 0;

            // 1. Painéis Solares
            var panels = vessel.FindPartModulesImplementing<ModuleDeployableSolarPanel>();
            for (int i = 0; i < panels.Count; i++)
            {
                panels[i].Extend();
                panelsCount++;
            }

            // 2. Antenas de Comunicação
            var antennas = vessel.FindPartModulesImplementing<ModuleDeployableAntenna>();
            for (int i = 0; i < antennas.Count; i++)
            {
                antennas[i].Extend();
                antennasCount++;
            }

            // 3. Radiadores Térmicos
            var radiators = vessel.FindPartModulesImplementing<ModuleDeployableRadiator>();
            for (int i = 0; i < radiators.Count; i++)
            {
                radiators[i].Extend();
                radiatorsCount++;
            }

            // 4. Coifas Protetoras (Procedural Fairings)
            var fairings = vessel.FindPartModulesImplementing<ModuleProceduralFairing>();
            for (int i = 0; i < fairings.Count; i++)
            {
                fairings[i].DeployFairing();
                fairingsCount++;
            }

            // Notificação visual na tela do KSP
            string msg = $"[Artemis] Altitude > 75km: Liberados {panelsCount} painéis, {antennasCount} antenas, {radiatorsCount} radiadores e {fairingsCount} coifas.";
            ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
            Debug.Log(msg);
        }
    }
}
