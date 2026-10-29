using System;
using UnityEngine;
using MuMech;
using OraculoGuidance.Modules.Physics;

namespace OraculoGuidance.Modules.Landing
{
    public enum BoosterPhase
    {
        Idle,
        Separation,
        FlipManeuver,
        BoostbackBurn,
        HighAltitudeCoast,
        EntryBurn,
        TerminalGuidanceAndLanding,
        Landed
    }

    /// <summary>
    /// Gerenciador de retorno do primeiro estágio reutilizável (RTLS estilo Falcon 9).
    /// </summary>
    public class BoosterRecovery
    {
        public bool IsActive { get; private set; }
        public BoosterPhase CurrentPhase { get; private set; } = BoosterPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        // Coordenadas da Zona de Pouso 1 (LZ-1) próxima ao KSC
        public double TargetLatitude = -0.0972;
        public double TargetLongitude = -74.5577;

        private Vessel _vessel;
        private MechJebCore _core;
        private double _burnStartTime = 0;

        public void Initialize(Vessel vessel, MechJebCore core)
        {
            _vessel = vessel;
            _core = core;
        }

        public void StartRecoverySequence()
        {
            if (_vessel == null) return;

            // 1. Estende a bolha de física do KSP para 100km imediatamente
            PhysicsRangeManager.ExtendPhysicsRange(_vessel);

            IsActive = true;
            CurrentPhase = BoosterPhase.Separation;
            StatusMessage = "Separação iniciada. Preparando manobra de giro (Flip)...";
            _burnStartTime = Planetarium.GetUniversalTime();

            Debug.Log("[Oraculo] Sequência de recuperação do 1º estágio disparada!");
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = BoosterPhase.Idle;
            StatusMessage = "Sequência abortada.";
            FlightInputHandler.state.mainThrottle = 0f;

            if (_core != null)
            {
                var landing = _core.GetComputerModule<MechJebModuleLandingAutopilot>();
                if (landing != null) landing.Deactivate();
            }

            PhysicsRangeManager.RestorePhysicsRange(_vessel);
        }

        public void Update()
        {
            if (!IsActive || _vessel == null) return;

            double time = Planetarium.GetUniversalTime();
            double altitude = _vessel.altitude;

            switch (CurrentPhase)
            {
                case BoosterPhase.Separation:
                    // Aguarda 3 segundos para limpar a distância do 2º estágio com segurança
                    if (time - _burnStartTime > 3.0)
                    {
                        CurrentPhase = BoosterPhase.FlipManeuver;
                        StatusMessage = "Girando para vetor de retorno (KSC)...";
                    }
                    break;

                case BoosterPhase.FlipManeuver:
                    // Gira em direção ao vetor retrógrado de superfície
                    OrientTowardsKSC();

                    // Após o alinhamento (espera 6 segundos para estabilização dos giroscópios/RCS)
                    if (time - _burnStartTime > 9.0)
                    {
                        CurrentPhase = BoosterPhase.BoostbackBurn;
                        FlightInputHandler.state.mainThrottle = 1.0f;
                        StatusMessage = "Boostback Burn em andamento: Revertendo trajetória para o KSC...";
                    }
                    break;

                case BoosterPhase.BoostbackBurn:
                    OrientTowardsKSC();

                    // Monitora se a trajetória já aponta de volta para o KSC
                    // Quando a velocidade horizontal de afastamento inverter, finaliza o boostback
                    if (_vessel.srfSpeed < 150 || _vessel.orbit.ApA < altitude)
                    {
                        FlightInputHandler.state.mainThrottle = 0f;
                        CurrentPhase = BoosterPhase.HighAltitudeCoast;
                        StatusMessage = "Boostback concluído! Em queda livre balística...";

                        // Abre os freios aerodinâmicos / Grid Fins (Action Group Brakes)
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                    }
                    break;

                case BoosterPhase.HighAltitudeCoast:
                    // Aponta para o retrógrado aerodinâmico para frear pelo arrasto
                    OrientRetrograde();

                    // Ao cruzar 45km de altitude em alta velocidade
                    if (altitude <= 45000 && _vessel.srfSpeed > 1000)
                    {
                        CurrentPhase = BoosterPhase.EntryBurn;
                        FlightInputHandler.state.mainThrottle = 0.8f;
                        StatusMessage = "Entry Burn: Reduzindo aquecimento de reentrada...";
                    }
                    else if (altitude <= 25000)
                    {
                        // Se não precisou de entry burn, vai direto para pouso terminal
                        EngageLandingGuidance();
                    }
                    break;

                case BoosterPhase.EntryBurn:
                    OrientRetrograde();

                    // Finaliza queima de reentrada quando velocidade cair para níveis subsônicos ou altitude < 25km
                    if (_vessel.srfSpeed <= 600 || altitude <= 25000)
                    {
                        FlightInputHandler.state.mainThrottle = 0f;
                        EngageLandingGuidance();
                    }
                    break;

                case BoosterPhase.TerminalGuidanceAndLanding:
                    if (_vessel.LandedOrSplashed)
                    {
                        CurrentPhase = BoosterPhase.Landed;
                        StatusMessage = "1º Estágio recuperado com sucesso na base!";
                        FlightInputHandler.state.mainThrottle = 0f;
                        IsActive = false;

                        // Restaura alcance de física
                        PhysicsRangeManager.RestorePhysicsRange(_vessel);
                    }
                    break;
            }
        }

        private void EngageLandingGuidance()
        {
            CurrentPhase = BoosterPhase.TerminalGuidanceAndLanding;
            StatusMessage = "Pouso terminal iniciado: Calculando queima suicida e abrindo pernas...";

            if (_core != null)
            {
                var landing = _core.GetComputerModule<MechJebModuleLandingAutopilot>();
                if (landing != null)
                {
                    landing.landingSites = new[] { new MechJebModuleLandingAutopilot.LandingSite("LZ-1 KSC", TargetLatitude, TargetLongitude) };
                    landing.targetIndex = 0;
                    landing.deployGears = true;
                    landing.deployChutes = false;
                    landing.touchdownSpeed = 1.2;
                    landing.Users.Add(this);
                    landing.LandAtPositionTarget();
                }
            }
        }

        private void OrientTowardsKSC()
        {
            if (_core != null)
            {
                var attitude = _core.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    // Aponta contra o movimento horizontal de superfície (oeste se estiver indo para leste)
                    attitude.attitudeTo(new AttitudeTarget(AttitudeReference.SURFACE_VELOCITY, -Vector3d.forward));
                }
            }
        }

        private void OrientRetrograde()
        {
            if (_core != null)
            {
                var attitude = _core.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeTo(new AttitudeTarget(AttitudeReference.SURFACE_VELOCITY, -Vector3d.forward));
                }
            }
        }
    }
}
