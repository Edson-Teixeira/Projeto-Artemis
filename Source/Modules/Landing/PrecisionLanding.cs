using System;
using UnityEngine;
using MuMech;

namespace OraculoGuidance.Modules.Landing
{
    /// <summary>
    /// Piloto automático para pousos verticais em qualquer corpo celeste (Mun, Minmus, Duna, etc.)
    /// ou em terreno aberto. Realiza queima de frenagem e suicide burn suave a 1.2 m/s.
    /// </summary>
    public class PrecisionLanding
    {
        public bool IsActive { get; private set; }
        public string StatusMessage { get; private set; } = "Pronto";

        public bool DeployGears { get; set; } = true;
        public bool DeployChutes { get; set; } = false;

        private Vessel _vessel;
        private MechJebCore _core;

        public void Initialize(Vessel vessel, MechJebCore core)
        {
            _vessel = vessel;
            _core = core;
        }

        public void StartLandingHere()
        {
            if (_vessel == null || _core == null)
            {
                StatusMessage = "Erro: MechJebCore não disponível.";
                return;
            }

            var landing = _core.GetComputerModule<MechJebModuleLandingAutopilot>();
            if (landing != null)
            {
                landing.deployGears = DeployGears;
                landing.deployChutes = DeployChutes;
                landing.touchdownSpeed = 1.2;
                landing.autowarp = true;
                landing.Users.Add(this);
                landing.LandUntargeted();

                IsActive = true;
                StatusMessage = $"Iniciando descida vertical em {_vessel.mainBody.bodyName}...";
                ScreenMessages.PostScreenMessage(
                    $"<color=#33ccff>[Oráculo] Pouso vertical iniciado em {_vessel.mainBody.bodyName}!</color>",
                    6f,
                    ScreenMessageStyle.UPPER_CENTER
                );
            }
        }

        public void StartLandingAtTarget(double lat, double lon, string siteName)
        {
            if (_vessel == null || _core == null)
            {
                StatusMessage = "Erro: MechJebCore não disponível.";
                return;
            }

            var landing = _core.GetComputerModule<MechJebModuleLandingAutopilot>();
            if (landing != null)
            {
                landing.landingSites = new[] { new MechJebModuleLandingAutopilot.LandingSite(siteName, lat, lon) };
                landing.targetIndex = 0;
                landing.deployGears = DeployGears;
                landing.deployChutes = DeployChutes;
                landing.touchdownSpeed = 1.2;
                landing.autowarp = true;
                landing.Users.Add(this);
                landing.LandAtPositionTarget();

                IsActive = true;
                StatusMessage = $"Pousando em {siteName} ({_vessel.mainBody.bodyName})...";
            }
        }

        public void AbortLanding()
        {
            if (_core != null)
            {
                var landing = _core.GetComputerModule<MechJebModuleLandingAutopilot>();
                if (landing != null)
                {
                    landing.Users.Remove(this);
                    landing.Deactivate();
                }
            }

            IsActive = false;
            StatusMessage = "Pouso vertical cancelado.";
            FlightInputHandler.state.mainThrottle = 0f;
        }

        public void Update()
        {
            if (!IsActive || _vessel == null) return;

            if (_vessel.LandedOrSplashed)
            {
                IsActive = false;
                StatusMessage = $"Pouso concluído com sucesso em {_vessel.mainBody.bodyName}!";
                FlightInputHandler.state.mainThrottle = 0f;
                ScreenMessages.PostScreenMessage(
                    $"<color=#00ff88>[Oráculo] Nave pousada com sucesso em {_vessel.mainBody.bodyName}!</color>",
                    8f,
                    ScreenMessageStyle.UPPER_CENTER
                );
            }
            else
            {
                StatusMessage = $"Descendo em {_vessel.mainBody.bodyName}: Alt={_vessel.radarAltitude:F0}m | Vel={_vessel.verticalSpeed:F1} m/s";
            }
        }
    }
}
