using System;
using UnityEngine;
using OraculoGuidance.Core;
using OraculoGuidance.Modules.Ascent;
using OraculoGuidance.Modules.Landing;

namespace OraculoGuidance.Modules.Tourism
{
    public enum TourismMode
    {
        SuborbitalHop,
        OrbitalCruise
    }

    public enum TourismPhase
    {
        Idle,
        LaunchingSuborbital,
        CoastingInSpace,
        AtmosphericReentry,
        DeployingChutesOrLanding,
        LaunchingOrbital,
        InOrbitExperience,
        DeorbitBurn,
        OrbitalReentry,
        LandedSafe
    }

    public enum LandingRecoveryType
    {
        Parachutes,
        SuicideBurnHoverslam
    }

    /// <summary>
    /// Controlador autônomo de missões turísticas de ponta a ponta (Oráculo 2.0).
    /// Executa:
    /// - Salto Suborbital: Sobe até o espaço (>70km), orienta para reentrada e pousa com paraquedas ou Hoverslam.
    /// - Voo Orbital: Lança em órbita de 85km, aguarda a experiência turística, executa queima de desorbita,
    ///   reentrada protegida e aterrissagem suave.
    /// </summary>
    public class TourismFlightController
    {
        public bool IsActive { get; private set; }
        public TourismPhase CurrentPhase { get; private set; } = TourismPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        public TourismMode Mode { get; set; } = TourismMode.SuborbitalHop;
        public LandingRecoveryType RecoveryMethod { get; set; } = LandingRecoveryType.Parachutes;

        private Vessel _vessel;
        private readonly OrbitalAscentController _ascentController;
        private readonly SuicideBurnController _suicideBurnController;

        private float _cmdPitch = 0f;
        private float _cmdYaw = 0f;
        private float _cmdThrottle = 0f;

        private double _spaceReachedTime = 0.0;
        private double _orbitHoldStartTime = 0.0;
        private bool _chutesDeployed = false;
        private ManeuverNode _deorbitNode;

        public TourismFlightController(OrbitalAscentController ascent, SuicideBurnController suicideBurn)
        {
            _ascentController = ascent;
            _suicideBurnController = suicideBurn;
        }

        public void Initialize(Vessel vessel)
        {
            if (_vessel != null)
            {
                _vessel.OnFlyByWire -= OnFlyByWireCallback;
            }

            _vessel = vessel;
            if (_vessel != null)
            {
                _vessel.OnFlyByWire += OnFlyByWireCallback;
            }
        }

        public void StartMission(TourismMode mode, LandingRecoveryType recovery)
        {
            if (_vessel == null)
            {
                StatusMessage = "Erro: Nave não selecionada.";
                return;
            }

            Mode = mode;
            RecoveryMethod = recovery;
            IsActive = true;
            _chutesDeployed = false;

            if (Mode == TourismMode.SuborbitalHop)
            {
                CurrentPhase = TourismPhase.LaunchingSuborbital;
                StatusMessage = "Iniciando decolagem suborbital vertical (Meta: 75km)...";
                Debug.Log("[Oraculo 2.0] Voo Turístico Suborbital iniciado!");
            }
            else
            {
                CurrentPhase = TourismPhase.LaunchingOrbital;
                StatusMessage = "Iniciando decolagem para órbita turística (85km)...";
                _ascentController.StartAscent(85000.0, TurnProfile.Normal);
                Debug.Log("[Oraculo 2.0] Voo Turístico Orbital iniciado!");
            }
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = TourismPhase.Idle;
            _cmdThrottle = 0f;

            if (_ascentController.IsActive) _ascentController.Abort();
            if (_suicideBurnController.IsActive) _suicideBurnController.Abort();

            if (_deorbitNode != null && _vessel != null && _vessel.patchedConicSolver != null)
            {
                _vessel.patchedConicSolver.RemoveManeuverNode(_deorbitNode);
                _deorbitNode = null;
            }

            if (_vessel != null)
            {
                _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }

            StatusMessage = "Missão turística abortada.";
        }

        public void Update()
        {
            if (!IsActive || _vessel == null) return;

            // Verificação de Pouso/Amolissagem Segura
            if (_vessel.LandedOrSplashed && CurrentPhase != TourismPhase.LaunchingSuborbital && CurrentPhase != TourismPhase.LaunchingOrbital)
            {
                FinishMission();
                return;
            }

            double altitude = _vessel.altitude;
            double radarAlt = _vessel.radarAltitude;
            double srfSpeed = _vessel.srfSpeed;

            switch (CurrentPhase)
            {
                #region Modalidade Suborbital
                case TourismPhase.LaunchingSuborbital:
                    ExecuteSuborbitalAscent(altitude);
                    break;

                case TourismPhase.CoastingInSpace:
                    ExecuteCoastingInSpace(altitude);
                    break;
                #endregion

                #region Modalidade Orbital
                case TourismPhase.LaunchingOrbital:
                    if (_ascentController.CurrentPhase == AscentPhase.InOrbit)
                    {
                        CurrentPhase = TourismPhase.InOrbitExperience;
                        _orbitHoldStartTime = Planetarium.GetUniversalTime();
                        StatusMessage = "Órbita alcançada! Turistas desfrutando da vista espacial...";
                        Debug.Log("[Oraculo 2.0] Turistas em órbita estável.");
                    }
                    else
                    {
                        StatusMessage = $"Subindo: {_ascentController.StatusMessage}";
                    }
                    break;

                case TourismPhase.InOrbitExperience:
                    ExecuteOrbitExperience();
                    break;

                case TourismPhase.DeorbitBurn:
                    ExecuteDeorbitBurn();
                    break;

                case TourismPhase.OrbitalReentry:
                    ExecuteReentryAttitude(altitude, srfSpeed);
                    break;
                #endregion

                #region Fases Comuns de Reentrada e Pouso
                case TourismPhase.AtmosphericReentry:
                    ExecuteReentryAttitude(altitude, srfSpeed);
                    break;

                case TourismPhase.DeployingChutesOrLanding:
                    ExecuteLandingApproach(radarAlt, srfSpeed);
                    break;

                case TourismPhase.LandedSafe:
                    break;
                #endregion
            }
        }

        #region Métodos de Voo Suborbital

        private void ExecuteSuborbitalAscent(double altitude)
        {
            // Decolagem quase vertical (Pitch 85°) para minimizar dispersão territorial
            Vector3d ascentDir = NavigationMath.GetHeadingAndPitchDirection(_vessel, 85.0, 90.0);
            NavigationMath.CalculateSteering(_vessel, ascentDir, ref _cmdPitch, ref _cmdYaw);

            double twr = NavigationMath.GetMaxTWR(_vessel);
            _cmdThrottle = (float)Mathf.Clamp((float)(1.6 / (twr > 0 ? twr : 1.0)), 0.3f, 1.0f);

            // Corte MECO ao atingir Apoastro de 75 km
            if (_vessel.orbit.ApA >= 75000.0)
            {
                _cmdThrottle = 0f;
                CurrentPhase = TourismPhase.CoastingInSpace;
                StatusMessage = "MECO: Apoastro em 75km! Cruzando a linha do espaço (70km)...";
                Debug.Log("[Oraculo 2.0] MECO Suborbital atingido.");
            }
            else
            {
                StatusMessage = $"Subida Suborbital: Alt={altitude/1000:F1}km | Ap={_vessel.orbit.ApA/1000:F1}km / 75km";
            }
        }

        private void ExecuteCoastingInSpace(double altitude)
        {
            _cmdThrottle = 0f;

            // Aponta para o Retrógrado para preparar o escudo térmico
            Vector3d retrograde = NavigationMath.GetSurfaceRetrograde(_vessel);
            NavigationMath.CalculateSteering(_vessel, retrograde, ref _cmdPitch, ref _cmdYaw);

            if (altitude >= 70000.0 && _spaceReachedTime == 0.0)
            {
                _spaceReachedTime = Planetarium.GetUniversalTime();
                ScreenMessages.PostScreenMessage("<color=#00ff88>[Oráculo 2.0] No Espaço! Contrato suborbital registrado.</color>", 6f, ScreenMessageStyle.UPPER_CENTER);
            }

            // Ao retornar para a atmosfera (< 70km em descida)
            if (altitude <= 69000.0 && _vessel.verticalSpeed < 0)
            {
                CurrentPhase = TourismPhase.AtmosphericReentry;
                StatusMessage = "Reentrada atmosférica: Mantendo escudo térmico no vetor retrógrado...";
            }
            else
            {
                StatusMessage = $"Voo no vácuo: Alt={altitude/1000:F1}km | Vel={_vessel.srfSpeed:F0} m/s";
            }
        }

        #endregion

        #region Métodos de Voo Orbital

        private void ExecuteOrbitExperience()
        {
            _cmdThrottle = 0f;
            Vector3d prograde = _vessel.obt_velocity.normalized;
            NavigationMath.CalculateSteering(_vessel, prograde, ref _cmdPitch, ref _cmdYaw);

            double elapsed = Planetarium.GetUniversalTime() - _orbitHoldStartTime;
            // Espera 30 segundos de experiência estável para registro no contrato
            if (elapsed >= 30.0)
            {
                CurrentPhase = TourismPhase.DeorbitBurn;
                StatusMessage = "Planejando queima retrógrada de desorbita (Periastro alvo: 30km)...";
                PlanDeorbitBurn();
            }
            else
            {
                StatusMessage = $"Em órbita: Turistas registrando a experiência ({elapsed:F0}s / 30s)...";
            }
        }

        private void PlanDeorbitBurn()
        {
            if (_deorbitNode == null && _vessel.patchedConicSolver != null)
            {
                double burnUT = Planetarium.GetUniversalTime() + 60.0;
                _deorbitNode = _vessel.patchedConicSolver.AddManeuverNode(burnUT);

                // Queima retrógrada para derrubar o periastro para 30km
                double currentSMA = _vessel.orbit.semiMajorAxis;
                double mu = _vessel.mainBody.gravParameter;
                double v1 = Math.Sqrt(mu / currentSMA);
                double targetSMA = (currentSMA + (_vessel.mainBody.Radius + 30000.0)) / 2.0;
                double v2 = Math.Sqrt(mu * ((2.0 / currentSMA) - (1.0 / targetSMA)));
                double dV = -(v1 - v2);

                _deorbitNode.DeltaV = new Vector3d(0, 0, dV);
                _vessel.patchedConicSolver.UpdateFlightPlan();
            }
        }

        private void ExecuteDeorbitBurn()
        {
            if (_deorbitNode == null)
            {
                CurrentPhase = TourismPhase.OrbitalReentry;
                return;
            }

            Vector3d burnVector = _deorbitNode.GetBurnVector(_vessel.orbit);
            NavigationMath.CalculateSteering(_vessel, burnVector.normalized, ref _cmdPitch, ref _cmdYaw);

            double timeUntilBurn = _deorbitNode.UT - Planetarium.GetUniversalTime();
            if (timeUntilBurn <= 0.2)
            {
                _cmdThrottle = 1.0f;
                if (burnVector.magnitude <= 1.0 || _vessel.orbit.PeA <= 32000.0)
                {
                    _cmdThrottle = 0f;
                    _vessel.patchedConicSolver.RemoveManeuverNode(_deorbitNode);
                    _deorbitNode = null;
                    CurrentPhase = TourismPhase.OrbitalReentry;
                    StatusMessage = "Desorbita concluída! Separando estágios e preparando reentrada...";
                }
            }
            else
            {
                _cmdThrottle = 0f;
                StatusMessage = $"Aguardando queima de desorbita em {timeUntilBurn:F0}s (Pe alvo: 30km)";
            }
        }

        #endregion

        #region Reentrada e Pouso

        private void ExecuteReentryAttitude(double altitude, double srfSpeed)
        {
            _cmdThrottle = 0f;

            // Mantém a nave rigorosamente no vetor retrógrado aerodinâmico
            Vector3d retrograde = NavigationMath.GetSurfaceRetrograde(_vessel);
            NavigationMath.CalculateSteering(_vessel, retrograde, ref _cmdPitch, ref _cmdYaw, sensitivity: 3.5);

            // Aciona freios de ar se disponíveis
            if (altitude > 1000.0 && altitude < 25000.0)
            {
                _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
            }

            // Ao descer para menos de 5.000m de altitude radar
            if (_vessel.radarAltitude <= 5000.0)
            {
                CurrentPhase = TourismPhase.DeployingChutesOrLanding;
                if (RecoveryMethod == LandingRecoveryType.SuicideBurnHoverslam)
                {
                    _suicideBurnController.StartSuicideBurn();
                    StatusMessage = "Transição para Hoverslam propulsivo nos metros finais...";
                }
                else
                {
                    StatusMessage = "Aproximação terminal: Preparando abertura de paraquedas...";
                }
            }
            else
            {
                StatusMessage = $"Reentrada térmica: Alt={altitude/1000:F1}km | Vel={srfSpeed:F0} m/s";
            }
        }

        private void ExecuteLandingApproach(double radarAlt, double srfSpeed)
        {
            if (RecoveryMethod == LandingRecoveryType.SuicideBurnHoverslam)
            {
                StatusMessage = $"Pouso Hoverslam: {_suicideBurnController.StatusMessage}";
                return;
            }

            // Método com Paraquedas
            Vector3d retrograde = NavigationMath.GetSurfaceRetrograde(_vessel);
            NavigationMath.CalculateSteering(_vessel, retrograde, ref _cmdPitch, ref _cmdYaw);

            // Aciona paraquedas automaticamente em velocidade segura (< 300 m/s) e altitude < 2000m
            if (!_chutesDeployed && radarAlt <= 2000.0 && srfSpeed < 320.0)
            {
                DeployAllParachutes();
                _chutesDeployed = true;
                _vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true); // Baixa trem de pouso se houver
                StatusMessage = "Paraquedas acionados com sucesso! Descida terminal suave...";
                Debug.Log("[Oraculo 2.0] Paraquedas abertos para pouso turístico.");
            }
            else if (_chutesDeployed)
            {
                StatusMessage = $"Descida de paraquedas: Alt={radarAlt:F0}m | Vel={_vessel.verticalSpeed:F1} m/s";
            }
        }

        private void DeployAllParachutes()
        {
            for (int i = 0; i < _vessel.Parts.Count; i++)
            {
                var part = _vessel.Parts[i];
                for (int j = 0; j < part.Modules.Count; j++)
                {
                    if (part.Modules[j] is ModuleParachute chute)
                    {
                        chute.Deploy();
                    }
                }
            }
        }

        private void FinishMission()
        {
            _cmdThrottle = 0f;
            CurrentPhase = TourismPhase.LandedSafe;
            IsActive = false;

            _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

            string modeName = (Mode == TourismMode.SuborbitalHop) ? "Suborbital" : "Orbital";
            StatusMessage = $"VOO TURÍSTICO {modeName.ToUpper()} FINALIZADO COM SUCESSO!";

            ScreenMessages.PostScreenMessage(
                $"<color=#00ff88><b>[Oráculo 2.0] MISSÃO TURÍSTICA CONCLUÍDA! Turistas sãos e salvos.</b></color>",
                10f,
                ScreenMessageStyle.UPPER_CENTER
            );
            Debug.Log("[Oraculo 2.0] Voo turístico concluído.");
        }

        #endregion

        private void OnFlyByWireCallback(FlightCtrlState state)
        {
            if (!IsActive || _vessel == null) return;

            // Se o sub-módulo de subida orbital ou suicide burn estiver ativo, eles assumem os controles
            if (CurrentPhase == TourismPhase.LaunchingOrbital) return;
            if (CurrentPhase == TourismPhase.DeployingChutesOrLanding && RecoveryMethod == LandingRecoveryType.SuicideBurnHoverslam) return;

            state.mainThrottle = _cmdThrottle;
            state.pitch = _cmdPitch;
            state.yaw = _cmdYaw;
        }
    }
}
