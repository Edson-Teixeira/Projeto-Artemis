using System;
using UnityEngine;
using MuMech;

namespace OraculoGuidance.Modules.Rendezvous
{
    public enum DockingPhase
    {
        Idle,
        AligningPlanes,
        HohmannTransfer,
        BrakingAtTarget,
        FinalDockingApproach,
        Docked
    }

    /// <summary>
    /// Sistema de Encontro e Acoplagem em 1 Clique.
    /// Cuida da interceptação de longa distância e da aproximação final de atracação.
    /// </summary>
    public class AutoDockingController
    {
        public bool IsActive { get; private set; }
        public DockingPhase CurrentPhase { get; private set; } = DockingPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        private Vessel _vessel;
        private MechJebCore _core;

        public void Initialize(Vessel vessel, MechJebCore core)
        {
            _vessel = vessel;
            _core = core;
        }

        public void StartRendezvousAndDock()
        {
            if (FlightGlobals.fetch.VesselTarget == null)
            {
                StatusMessage = "Erro: Nenhum alvo selecionado! Selecione uma nave no mapa.";
                return;
            }

            if (_core == null)
            {
                StatusMessage = "Erro: MechJebCore não disponível.";
                return;
            }

            IsActive = true;
            CurrentPhase = DockingPhase.HohmannTransfer;
            StatusMessage = "Iniciando transferência orbital para interceptar alvo...";

            var rdv = _core.GetComputerModule<MechJebModuleRendezvousAutopilot>();
            if (rdv != null)
            {
                rdv.desiredDistance = 80.0; // Intercepta a 80 metros
                rdv.Users.Add(this);
                rdv.Activate();
                Debug.Log("[Oraculo] Interceptação orbital de alvo ativada!");
            }
        }

        public void Abort()
        {
            if (_core != null)
            {
                var rdv = _core.GetComputerModule<MechJebModuleRendezvousAutopilot>();
                if (rdv != null)
                {
                    rdv.Users.Remove(this);
                    rdv.Deactivate();
                }

                var dock = _core.GetComputerModule<MechJebModuleDockingAutopilot>();
                if (dock != null)
                {
                    dock.Users.Remove(this);
                    dock.Deactivate();
                }
            }

            IsActive = false;
            CurrentPhase = DockingPhase.Idle;
            StatusMessage = "Operação abortada.";
            FlightInputHandler.state.mainThrottle = 0f;
        }

        public void Update()
        {
            if (!IsActive || _vessel == null) return;

            var target = FlightGlobals.fetch.VesselTarget;
            if (target == null)
            {
                Abort();
                StatusMessage = "Alvo perdido. Operação cancelada.";
                return;
            }

            double distanceToTarget = Vector3d.Distance(_vessel.GetWorldPos3D(), target.GetTransform().position);

            switch (CurrentPhase)
            {
                case DockingPhase.HohmannTransfer:
                    StatusMessage = $"Aproximação em curso. Distância: {distanceToTarget / 1000:F1} km";

                    // Quando a distância cair para menos de 120 metros, passa para acoplagem final
                    if (distanceToTarget <= 120.0)
                    {
                        TransitionToDocking();
                    }
                    break;

                case DockingPhase.FinalDockingApproach:
                    StatusMessage = $"Manobra fina de RCS... Distância: {distanceToTarget:F1} m";

                    // Se a nave acoplou (ou seja, se fundiu ao alvo ou distância menor que 0.5m)
                    if (distanceToTarget <= 0.8)
                    {
                        CurrentPhase = DockingPhase.Docked;
                        StatusMessage = "Acoplagem finalizada com sucesso!";
                        IsActive = false;
                        Abort(); // Desliga pilotos
                    }
                    break;
            }
        }

        private void TransitionToDocking()
        {
            // Desliga o módulo de rendezvous de longa distância
            var rdv = _core.GetComputerModule<MechJebModuleRendezvousAutopilot>();
            if (rdv != null)
            {
                rdv.Users.Remove(this);
                rdv.Deactivate();
            }

            // Liga o piloto de acoplagem por RCS
            var dock = _core.GetComputerModule<MechJebModuleDockingAutopilot>();
            if (dock != null)
            {
                dock.overrideSafeDistance = true;
                dock.speedLimit = 0.5; // Aproximação segura a 0.5 m/s
                dock.Users.Add(this);
                dock.Activate();

                CurrentPhase = DockingPhase.FinalDockingApproach;
                StatusMessage = "Alinhando portas de acoplagem via RCS...";
                Debug.Log("[Oraculo] Acoplagem final por RCS ativada!");
            }
        }
    }
}
