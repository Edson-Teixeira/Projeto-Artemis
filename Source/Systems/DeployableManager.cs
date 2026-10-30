using System;
using System.Collections.Generic;
using UnityEngine;
using Artemis.Telemetry;

namespace Artemis.Systems
{
    /// <summary>
    /// DeployableManager: Gerencia o acionamento de partes móveis ao entrar e sair da atmosfera.
    /// Regras:
    /// - Ao passar dos 75.000m (vácuo): estende painéis solares, antenas, radiadores e ejeta as coifas.
    /// - Ao cair abaixo dos 70.000m (reentrada): recolhe painéis, antenas e radiadores para proteção térmica/aerodinâmica.
    /// </summary>
    public class DeployableManager
    {
        private const double DEPLOY_ALTITUDE = 75000.0;  // 75 km (abre instrumentos)
        private const double RETRACT_ALTITUDE = 70000.0; // 70 km (recolhe instrumentos)

        private bool _isExtendedInSpace = false;
        private bool _fairingsJettisoned = false;

        public bool IsDeployed => _isExtendedInSpace;

        public void Reset()
        {
            _isExtendedInSpace = false;
            _fairingsJettisoned = false;
        }

        /// <summary>
        /// Executado a cada frame de física pelo ArtemisCore.
        /// </summary>
        public void Update(TelemetryReader telemetry)
        {
            if (telemetry.Vessel == null) return;

            double alt = telemetry.AltitudeASL;

            // 1. Acima de 75km: Abre tudo e ejeta coifas no vácuo
            if (alt >= DEPLOY_ALTITUDE && !_isExtendedInSpace)
            {
                ExtendAllDeployables(telemetry.Vessel);
                if (!_fairingsJettisoned)
                {
                    JettisonFairings(telemetry.Vessel);
                    _fairingsJettisoned = true;
                }
                _isExtendedInSpace = true;
            }
            // 2. Abaixo de 70km (caindo na atmosfera): Recolhe tudo para não quebrar pelo calor e atrito
            else if (alt < RETRACT_ALTITUDE && _isExtendedInSpace)
            {
                RetractAllDeployables(telemetry.Vessel);
                _isExtendedInSpace = false;
            }
        }

        public void ExtendAllDeployables(Vessel vessel)
        {
            if (vessel == null) return;

            int count = 0;
            var parts = vessel.FindPartModulesImplementing<ModuleDeployablePart>();
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i].Extend();
                count++;
            }

            string msg = $"[Artemis] Altitude > 75km: {count} instrumentos estendidos no vácuo!";
            ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
            Debug.Log(msg);
        }

        public void RetractAllDeployables(Vessel vessel)
        {
            if (vessel == null) return;

            int count = 0;
            var parts = vessel.FindPartModulesImplementing<ModuleDeployablePart>();
            for (int i = 0; i < parts.Count; i++)
            {
                // Só recolhe se for do tipo retrátil
                if (parts[i].retractable)
                {
                    parts[i].Retract();
                    count++;
                }
            }

            string msg = $"[Artemis] Altitude < 70km: Recolhendo {count} peças para proteção térmica/aerodinâmica!";
            ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
            Debug.Log(msg);
        }

        private void JettisonFairings(Vessel vessel)
        {
            if (vessel == null) return;

            var fairings = vessel.FindPartModulesImplementing<ModuleProceduralFairing>();
            for (int i = 0; i < fairings.Count; i++)
            {
                fairings[i].DeployFairing();
            }

            if (fairings.Count > 0)
            {
                ScreenMessages.PostScreenMessage($"[Artemis] {fairings.Count} Coifas protetoras ejetadas!", 4f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
    }
}
