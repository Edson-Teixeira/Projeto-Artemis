using System;
using UnityEngine;

namespace OraculoGuidance.Core
{
    /// <summary>
    /// Gerenciador autônomo de peças retráteis (Painéis Solares, Antenas e Radiadores).
    /// Regra:
    /// - Ao sair da atmosfera (>70km em Kerbin): Estende automaticamente painéis, antenas e radiadores.
    /// - Ao entrar/reentrar na atmosfera (altitude <= atmosfera e descendo): Recolhe tudo para proteção térmica/aerodinâmica.
    /// </summary>
    public class DeployablePartsManager
    {
        public bool AutoManageEnabled { get; set; } = true;
        public string StatusSummary { get; private set; } = "Pronto";

        private Vessel _vessel;
        private bool _wasInSpace = false;
        private double _lastCheckTime = 0.0;

        public void Initialize(Vessel vessel)
        {
            _vessel = vessel;
            if (_vessel != null)
            {
                double atmDepth = _vessel.mainBody.atmosphere ? _vessel.mainBody.atmosphereDepth : 0.0;
                _wasInSpace = _vessel.altitude > atmDepth;
            }
        }

        public void Update()
        {
            if (!AutoManageEnabled || _vessel == null) return;

            double now = Planetarium.GetUniversalTime();
            if (now - _lastCheckTime < 0.5) return; // Avalia a cada 500ms para economia de CPU
            _lastCheckTime = now;

            double altitude = _vessel.altitude;
            double verticalSpeed = _vessel.verticalSpeed;
            double atmDepth = _vessel.mainBody.atmosphere ? _vessel.mainBody.atmosphereDepth : 0.0;

            bool inSpace = !_vessel.mainBody.atmosphere || altitude > atmDepth;

            // Transição 1: Saiu da atmosfera -> Estender tudo
            if (inSpace && !_wasInSpace)
            {
                _wasInSpace = true;
                ExtendAllDeployables();
                ScreenMessages.PostScreenMessage(
                    "<color=#33ccff><b>[Oráculo 2.0] No Espaço: Painéis solares, antenas e radiadores estendidos!</b></color>",
                    6f,
                    ScreenMessageStyle.UPPER_CENTER
                );
                Debug.Log("[Oraculo 2.0] Peças retráteis estendidas no vácuo.");
            }
            // Transição 2: Reentrada na atmosfera em descida -> Recolher tudo
            else if (!inSpace && _wasInSpace && verticalSpeed < 0)
            {
                _wasInSpace = false;
                RetractAllDeployables();
                ScreenMessages.PostScreenMessage(
                    "<color=#ff9900><b>[Oráculo 2.0] Reentrada: Painéis, antenas e radiadores recolhidos para proteção!</b></color>",
                    7f,
                    ScreenMessageStyle.UPPER_CENTER
                );
                Debug.Log("[Oraculo 2.0] Peças retráteis recolhidas antes da reentrada.");
            }
        }

        /// <summary>
        /// Estende todos os painéis solares, antenas e radiadores da nave.
        /// </summary>
        public void ExtendAllDeployables()
        {
            if (_vessel == null) return;

            int count = 0;
            for (int i = 0; i < _vessel.Parts.Count; i++)
            {
                var part = _vessel.Parts[i];
                for (int j = 0; j < part.Modules.Count; j++)
                {
                    if (part.Modules[j] is ModuleDeployablePart deployable)
                    {
                        if (deployable.deployState != ModuleDeployablePart.DeployState.EXTENDED &&
                            deployable.deployState != ModuleDeployablePart.DeployState.EXTENDING &&
                            deployable.deployState != ModuleDeployablePart.DeployState.BROKEN)
                        {
                            deployable.Extend();
                            count++;
                        }
                    }
                }
            }
            StatusSummary = $"{count} dispositivos estendidos no vácuo.";
        }

        /// <summary>
        /// Recolhe todas as peças retráteis para protegê-las do atrito e calor da reentrada.
        /// </summary>
        public void RetractAllDeployables()
        {
            if (_vessel == null) return;

            int count = 0;
            for (int i = 0; i < _vessel.Parts.Count; i++)
            {
                var part = _vessel.Parts[i];
                for (int j = 0; j < part.Modules.Count; j++)
                {
                    if (part.Modules[j] is ModuleDeployablePart deployable)
                    {
                        // Apenas recolhe se a peça suportar retração e não estiver quebrada
                        if (deployable.retractable &&
                            deployable.deployState != ModuleDeployablePart.DeployState.RETRACTED &&
                            deployable.deployState != ModuleDeployablePart.DeployState.RETRACTING &&
                            deployable.deployState != ModuleDeployablePart.DeployState.BROKEN)
                        {
                            deployable.Retract();
                            count++;
                        }
                    }
                }
            }
            StatusSummary = $"{count} dispositivos recolhidos para reentrada.";
        }
    }
}
