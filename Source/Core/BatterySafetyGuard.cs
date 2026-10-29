using System;
using UnityEngine;

namespace OraculoGuidance.Core
{
    /// <summary>
    /// Sistema de salvaguarda de energia e modo de hibernação para sondas e naves.
    /// Monitora a carga elétrica (ElectricCharge):
    /// - Abaixo de 15%: Ativa hibernação (desliga SAS, corta antenas de alto consumo, orienta para o Sol).
    /// - Acima de 80%: Restaura os sistemas normais de bordo.
    /// </summary>
    public class BatterySafetyGuard
    {
        public bool GuardEnabled { get; set; } = true;
        public bool IsInHibernation { get; private set; } = false;
        public string StatusSummary { get; private set; } = "Normal";

        private Vessel _vessel;
        private double _lastCheckTime = 0.0;
        private PartResourceDefinition _ecDefinition;

        public void Initialize(Vessel vessel)
        {
            _vessel = vessel;
            _ecDefinition = PartResourceLibrary.Instance?.GetDefinition("ElectricCharge");
            IsInHibernation = false;
        }

        public void Update()
        {
            if (!GuardEnabled || _vessel == null || _ecDefinition == null) return;

            double now = Planetarium.GetUniversalTime();
            if (now - _lastCheckTime < 1.0) return; // Avalia a cada 1 segundo
            _lastCheckTime = now;

            double currentEC = 0.0;
            double maxEC = 0.0;
            _vessel.GetConnectedResourceTotals(_ecDefinition.id, out currentEC, out maxEC);

            if (maxEC <= 0.0) return;

            double percent = currentEC / maxEC;

            // Condição 1: Bateria Crítica (<15%) -> Entra em Hibernação
            if (!IsInHibernation && percent <= 0.15)
            {
                EnterHibernation(percent);
            }
            // Condição 2: Bateria Recuperada (>80%) -> Sai da Hibernação
            else if (IsInHibernation && percent >= 0.80)
            {
                ExitHibernation(percent);
            }

            if (IsInHibernation)
            {
                StatusSummary = $"HIBERNAÇÃO: Carga em {(percent * 100.0):F0}% | Recarregando no Sol...";
                OrientTowardsSun();
            }
            else
            {
                StatusSummary = $"Bateria em {(percent * 100.0):F0}% ({currentEC:F0}/{maxEC:F0} EC)";
            }
        }

        private void EnterHibernation(double percent)
        {
            IsInHibernation = true;

            // Desativa SAS e RCS para cortar dreno elétrico de giroscópios
            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            _vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false);

            ScreenMessages.PostScreenMessage(
                $"<color=#ff3333><b>[Oráculo 2.0] BATERIA CRÍTICA ({(percent * 100.0):F0}%)! Modo Hibernação Ativado.</b></color>",
                8f,
                ScreenMessageStyle.UPPER_CENTER
            );
            Debug.Log($"[Oraculo 2.0] Hibernação de emergência disparada para {_vessel.vesselName}.");
        }

        private void ExitHibernation(double percent)
        {
            IsInHibernation = false;
            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

            ScreenMessages.PostScreenMessage(
                $"<color=#00ff88><b>[Oráculo 2.0] BATERIA RECUPERADA ({(percent * 100.0):F0}%)! Sistemas Restaurados.</b></color>",
                8f,
                ScreenMessageStyle.UPPER_CENTER
            );
            Debug.Log($"[Oraculo 2.0] Hibernação encerrada para {_vessel.vesselName}.");
        }

        private void OrientTowardsSun()
        {
            if (_vessel == null) return;

            CelestialBody sun = Planetarium.fetch?.Sun;
            if (sun != null)
            {
                Vector3d sunDir = (sun.transform.position - _vessel.GetWorldPos3D()).normalized;
                // Aponta painéis solares para o Sol caso haja pouca energia restante
                _vessel.Autopilot.SAS.SetTargetOrientation(sunDir, false);
            }
        }
    }
}
