using System;
using UnityEngine;
using MuMech;

namespace OraculoGuidance.Modules.Landing
{
    public enum KSCLandingSite
    {
        Launchpad,
        LandingZone1,
        RunwayCenter
    }

    /// <summary>
    /// Piloto automático de Desorbita e Pouso de Precisão no KSC para Foguetes e Sondas.
    /// Funciona a partir de qualquer órbita ao redor de Kerbin: calcula a janela de queima
    /// retrógrada levando em conta a rotação do planeta e o arrasto aerodinâmico,
    /// acelera o tempo (timewarp), executa a queima de desorbita, estabiliza a reentrada
    /// e realiza o toque suave propulsivo exatamente na plataforma do KSC.
    /// </summary>
    public class KSCOrbitLandingController
    {
        public bool IsActive { get; private set; }
        public string StatusMessage { get; private set; } = "Pronto";

        public KSCLandingSite TargetSite { get; set; } = KSCLandingSite.Launchpad;
        public bool DeployChutesIfAvailable { get; set; } = true;
        public bool DeployGearAutomatically { get; set; } = true;

        // Coordenadas das instalações do KSC
        public static readonly double LaunchpadLat = -0.0972;
        public static readonly double LaunchpadLon = -74.5577;

        public static readonly double LZ1Lat = -0.0972;
        public static readonly double LZ1Lon = -74.5200;

        public static readonly double RunwayLat = -0.0486;
        public static readonly double RunwayLon = -74.7245;

        private Vessel _vessel;
        private MechJebCore _core;

        public void Initialize(Vessel vessel, MechJebCore core)
        {
            _vessel = vessel;
            _core = core;
        }

        public void StartDeorbitAndLanding()
        {
            if (_vessel == null || _core == null)
            {
                StatusMessage = "Erro: MechJebCore ou nave não disponível.";
                return;
            }

            if (_vessel.mainBody != FlightGlobals.GetBodyByName("Kerbin"))
            {
                StatusMessage = "Erro: Esta função é exclusiva para naves em órbita de Kerbin.";
                return;
            }

            var landing = _core.GetComputerModule<MechJebModuleLandingAutopilot>();
            if (landing == null)
            {
                StatusMessage = "Módulo de pouso do MechJeb não disponível.";
                return;
            }

            double targetLat = LaunchpadLat;
            double targetLon = LaunchpadLon;
            string siteName = "KSC Launchpad";

            if (TargetSite == KSCLandingSite.LandingZone1)
            {
                targetLat = LZ1Lat;
                targetLon = LZ1Lon;
                siteName = "KSC LZ-1";
            }
            else if (TargetSite == KSCLandingSite.RunwayCenter)
            {
                targetLat = RunwayLat;
                targetLon = RunwayLon;
                siteName = "KSC Pista";
            }

            // Configura o alvo de pouso com simulação aerodinâmica
            landing.landingSites = new[] { new MechJebModuleLandingAutopilot.LandingSite(siteName, targetLat, targetLon) };
            landing.targetIndex = 0;
            landing.deployGears = DeployGearAutomatically;
            landing.deployChutes = DeployChutesIfAvailable;
            landing.touchdownSpeed = 1.2; // toque suave a 1.2 m/s
            landing.autowarp = true;      // acelera o tempo automaticamente até o ponto de desorbita

            landing.Users.Add(this);
            landing.LandAtPositionTarget();

            IsActive = true;
            StatusMessage = $"Calculando desorbita e ponto de reentrada para {siteName}...";
            ScreenMessages.PostScreenMessage(
                $"<color=#33ccff>[Oráculo] Desorbita e Pouso no {siteName} ativados!</color>",
                6f,
                ScreenMessageStyle.UPPER_CENTER
            );

            Debug.Log($"[Oraculo] Desorbita e pouso de foguete/sonda iniciado para {siteName}.");
        }

        public void Abort()
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
            StatusMessage = "Pouso no KSC cancelado pelo usuário.";
            FlightInputHandler.state.mainThrottle = 0f;
        }

        public void Update()
        {
            if (!IsActive || _vessel == null) return;

            if (_vessel.LandedOrSplashed)
            {
                IsActive = false;
                StatusMessage = "Nave pousada com sucesso no KSC!";
                FlightInputHandler.state.mainThrottle = 0f;
                ScreenMessages.PostScreenMessage(
                    "<color=#00ff88>[Oráculo] Pouso no KSC concluído com sucesso!</color>",
                    8f,
                    ScreenMessageStyle.UPPER_CENTER
                );
                return;
            }

            double alt = _vessel.altitude;
            double srfSpeed = _vessel.srfSpeed;

            if (alt > 70000)
            {
                StatusMessage = "Em órbita: Aguardando/executando queima de desorbita...";
            }
            else if (alt > 25000)
            {
                StatusMessage = $"Reentrada atmosférica: Alt={alt/1000:F0}km | Vel={srfSpeed:F0} m/s";
            }
            else
            {
                StatusMessage = $"Aproximação final no KSC: Alt={_vessel.radarAltitude:F0}m | Vel={_vessel.verticalSpeed:F1} m/s";
            }
        }
    }
}
