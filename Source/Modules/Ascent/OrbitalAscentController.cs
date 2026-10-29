using System;
using UnityEngine;
using OraculoGuidance.Core;

namespace OraculoGuidance.Modules.Ascent
{
    public enum AscentPhase
    {
        Idle,
        PreLaunch,
        GravityTurn,
        AtmosphereExitCoast,
        PlanningCircularization,
        WaitingForBurn,
        ExecutingCircularization,
        InOrbit
    }

    public enum TurnProfile
    {
        Normal,
        Rasa,
        Aguda
    }

    /// <summary>
    /// Controlador autônomo de lançamento orbital nativo do KSP.
    /// Migrado e aprimorado de DecolagemOrbital.py do MechPeste.
    /// Inclui:
    /// - Auto-Staging com proteção contra Flameout (desacoplamento automático de estágios vazios).
    /// - Curva de gravidade contínua matemática (Normal, Rasa, Aguda).
    /// - Limitador de pressão dinâmica (Max-Q) via PID.
    /// - Ejeção automática de coifas (>50km).
    /// - Circularização nativa no Apoastro via Equação Vis-Viva e Tsiolkovsky.
    /// </summary>
    public class OrbitalAscentController
    {
        public bool IsActive { get; private set; }
        public AscentPhase CurrentPhase { get; private set; } = AscentPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        // Parâmetros de lançamento
        public double TargetAltitude = 85000.0;     // 85 km padrão
        public double TargetHeading = 90.0;         // 90° = Leste equatorial
        public TurnProfile Profile = TurnProfile.Normal;
        public bool EnableAutoStage = true;         // Auto-desacoplamento quando o combustível acaba

        public double TurnStartAltitude = 150.0;    // Início da curva de gravidade
        public double TurnEndAltitude = 60000.0;    // Fim da curva de gravidade

        private Vessel _vessel;
        private PIDController _maxQPid;
        private double _commandedThrottle = 0.0;
        private float _commandedPitch = 0.0f;
        private float _commandedYaw = 0.0f;
        private bool _fairingsJettisoned = false;
        private double _lastStageTime = 0.0;

        // Dados de queima de circularização
        private ManeuverNode _circularizationNode;
        private double _nodeBurnTime = 0.0;
        private double _plannedDeltaV = 0.0;

        public OrbitalAscentController()
        {
            // PID de limitação de pressão dinâmica (Max-Q)
            _maxQPid = new PIDController(kp: 0.001, ki: 0.001, kd: 0.0001, min: 0.2, max: 1.0, sampleTime: 0.02);
            _maxQPid.Setpoint = 25.0; // 25 kPa limite de pressão dinâmica
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

        public void StartAscent(double targetAltitude = 85000.0, TurnProfile profile = TurnProfile.Normal)
        {
            if (_vessel == null)
            {
                StatusMessage = "Erro: Nave não selecionada.";
                return;
            }

            TargetAltitude = Math.Max(72000.0, targetAltitude);
            Profile = profile;
            _fairingsJettisoned = false;
            _lastStageTime = Planetarium.GetUniversalTime();
            _maxQPid.Reset();

            IsActive = true;
            CurrentPhase = AscentPhase.PreLaunch;
            StatusMessage = "Iniciando contagem de lançamento...";

            Debug.Log($"[Oraculo 2.0] Decolagem iniciada! Alvo: {TargetAltitude/1000:F0}km | Perfil: {Profile} | AutoStage: {EnableAutoStage}");
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = AscentPhase.Idle;
            _commandedThrottle = 0.0;
            _commandedPitch = 0.0f;
            _commandedYaw = 0.0f;

            if (_circularizationNode != null && _vessel != null && _vessel.patchedConicSolver != null)
            {
                _vessel.patchedConicSolver.RemoveManeuverNode(_circularizationNode);
                _circularizationNode = null;
            }

            if (_vessel != null)
            {
                _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }

            StatusMessage = "Lançamento abortado.";
        }

        public void Update()
        {
            if (!IsActive || _vessel == null) return;

            double altitude = _vessel.altitude;
            double apoapsis = _vessel.orbit.ApA;

            switch (CurrentPhase)
            {
                case AscentPhase.PreLaunch:
                    ExecutePreLaunch();
                    break;

                case AscentPhase.GravityTurn:
                    ExecuteGravityTurn(altitude, apoapsis);
                    break;

                case AscentPhase.AtmosphereExitCoast:
                    ExecuteAtmosphereExitCoast(altitude);
                    break;

                case AscentPhase.PlanningCircularization:
                    PlanCircularizationNode();
                    break;

                case AscentPhase.WaitingForBurn:
                    WaitForBurnWindow();
                    break;

                case AscentPhase.ExecutingCircularization:
                    ExecuteCircularizationBurn();
                    break;

                case AscentPhase.InOrbit:
                    break;
            }
        }

        private void ExecutePreLaunch()
        {
            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            _vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false);

            // Ajusta o acelerador inicial para TWR = 1.5
            double maxTWR = NavigationMath.GetMaxTWR(_vessel);
            _commandedThrottle = maxTWR > 0 ? Math.Min(1.0, 1.5 / maxTWR) : 1.0;

            // Se a nave estiver na plataforma de lançamento, ativa o primeiro estágio
            if (_vessel.situation == Vessel.Situations.PRELAUNCH)
            {
                KSP.UI.Screens.StageManager.ActivateNextStage();
                _lastStageTime = Planetarium.GetUniversalTime();
            }

            // Aponta vertical (Pitch 90°)
            Vector3d targetDir = NavigationMath.GetHeadingAndPitchDirection(_vessel, 90.0, TargetHeading);
            NavigationMath.CalculateSteering(_vessel, targetDir, ref _commandedPitch, ref _commandedYaw);

            CurrentPhase = AscentPhase.GravityTurn;
            StatusMessage = "Subindo e iniciando Curva de Gravidade...";
        }

        private void ExecuteGravityTurn(double altitude, double apoapsis)
        {
            // Auto-Staging contínuo durante a subida com acelerador ativo
            if (EnableAutoStage && _commandedThrottle > 0.05)
            {
                CheckAutoStage();
            }

            // 1. Cálculo da curvatura de gravidade matemática
            if (altitude > TurnStartAltitude && altitude < TurnEndAltitude)
            {
                double progress = Math.Sqrt((altitude - TurnStartAltitude) / (TurnEndAltitude - TurnStartAltitude));
                double turnAngle = 0.0;

                switch (Profile)
                {
                    case TurnProfile.Rasa:
                        turnAngle = Math.Sqrt(progress * 0.8) * 90.0;
                        break;
                    case TurnProfile.Normal:
                        turnAngle = progress * 85.0;
                        break;
                    case TurnProfile.Aguda:
                        turnAngle = Math.Pow(progress, 1.2) * 80.0;
                        break;
                }

                double targetPitch = Math.Max(0.0, 90.0 - turnAngle);
                Vector3d targetDir = NavigationMath.GetHeadingAndPitchDirection(_vessel, targetPitch, TargetHeading);
                NavigationMath.CalculateSteering(_vessel, targetDir, ref _commandedPitch, ref _commandedYaw);
            }

            // 2. Controle de Aceleração contra Max-Q (Pressão Dinâmica)
            double qKPa = _vessel.dynamicPressurekPa;
            if (altitude > 20000.0)
            {
                _maxQPid.Setpoint = 20.0;
            }
            double pidThrottle = _maxQPid.Compute(qKPa);
            _commandedThrottle = Mathf.Clamp((float)pidThrottle, 0.2f, 1.0f);

            // 3. Ejeção de coifas acima de 50 km
            CheckFairingJettison(altitude);

            // 4. Desacelera ao se aproximar do Apoastro Alvo
            if (apoapsis >= TargetAltitude * 0.95)
            {
                _commandedThrottle = 0.25;
            }

            // 5. Corte do motor (MECO) ao atingir o Apoastro Alvo
            if (apoapsis >= TargetAltitude)
            {
                _commandedThrottle = 0.0;
                CurrentPhase = AscentPhase.AtmosphereExitCoast;
                StatusMessage = "MECO: Apoastro atingido! Aguardando saída da atmosfera...";
                Debug.Log($"[Oraculo 2.0] MECO atingido em {apoapsis/1000:F1}km.");
            }
            else
            {
                StatusMessage = $"Subindo: Alt={altitude/1000:F1}km | Ap={apoapsis/1000:F1}km / {TargetAltitude/1000:F0}km";
            }
        }

        private void ExecuteAtmosphereExitCoast(double altitude)
        {
            _commandedThrottle = 0.0;
            CheckFairingJettison(altitude);

            // Mantém prograde na saída da atmosfera
            Vector3d prograde = _vessel.obt_velocity.normalized;
            NavigationMath.CalculateSteering(_vessel, prograde, ref _commandedPitch, ref _commandedYaw);

            double atmLimit = _vessel.mainBody.atmosphereDepth > 0 ? _vessel.mainBody.atmosphereDepth : 70000.0;
            if (altitude >= atmLimit)
            {
                CurrentPhase = AscentPhase.PlanningCircularization;
                StatusMessage = "Fora da atmosfera. Calculando circularização...";
            }
            else
            {
                StatusMessage = $"Saindo da atmosfera: {altitude/1000:F1}km / {atmLimit/1000:F0}km";
            }
        }

        private void PlanCircularizationNode()
        {
            _plannedDeltaV = NavigationMath.CalculateCircularizationDeltaV(_vessel);
            _nodeBurnTime = NavigationMath.CalculateBurnTime(_vessel, _plannedDeltaV);

            double timeToAp = _vessel.orbit.timeToAp;
            double universalBurnTime = Planetarium.GetUniversalTime() + timeToAp;

            if (_vessel.patchedConicSolver != null)
            {
                _circularizationNode = _vessel.patchedConicSolver.AddManeuverNode(universalBurnTime);
                _circularizationNode.DeltaV = new Vector3d(0, 0, _plannedDeltaV);
                _vessel.patchedConicSolver.UpdateFlightPlan();
            }

            _vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
            CurrentPhase = AscentPhase.WaitingForBurn;
            StatusMessage = $"Circularização planejada: dV={_plannedDeltaV:F1} m/s | Queima={_nodeBurnTime:F1}s";
            Debug.Log($"[Oraculo 2.0] Nó de circularização criado: {_plannedDeltaV:F1} m/s.");
        }

        private void WaitForBurnWindow()
        {
            if (_circularizationNode == null)
            {
                CurrentPhase = AscentPhase.PlanningCircularization;
                return;
            }

            Vector3d burnDir = _circularizationNode.GetBurnVector(_vessel.orbit).normalized;
            NavigationMath.CalculateSteering(_vessel, burnDir, ref _commandedPitch, ref _commandedYaw);

            double timeToAp = _vessel.orbit.timeToAp;
            double timeUntilBurn = timeToAp - (_nodeBurnTime / 2.0);

            if (timeUntilBurn <= 0.2)
            {
                CurrentPhase = AscentPhase.ExecutingCircularization;
                _commandedThrottle = 1.0;
                StatusMessage = "Executando queima de circularização...";
                Debug.Log("[Oraculo 2.0] Ignição da circularização iniciada!");
            }
            else
            {
                StatusMessage = $"Aguardando nó: Ignição em {timeUntilBurn:F1}s | Queima total={_nodeBurnTime:F1}s";
            }
        }

        private void ExecuteCircularizationBurn()
        {
            if (_circularizationNode == null)
            {
                FinishAscent();
                return;
            }

            // Auto-Staging também durante a queima de circularização caso o estágio esgote
            if (EnableAutoStage && _commandedThrottle > 0.05)
            {
                CheckAutoStage();
            }

            Vector3d remainingBurnVector = _circularizationNode.GetBurnVector(_vessel.orbit);
            double remainingDeltaV = remainingBurnVector.magnitude;

            NavigationMath.CalculateSteering(_vessel, remainingBurnVector.normalized, ref _commandedPitch, ref _commandedYaw);

            if (remainingDeltaV < 40.0)
            {
                _commandedThrottle = Math.Max(0.08, remainingDeltaV / 40.0);
            }
            else
            {
                _commandedThrottle = 1.0;
            }

            if (remainingDeltaV <= 0.8 || _vessel.orbit.PeA >= TargetAltitude * 0.98)
            {
                FinishAscent();
            }
            else
            {
                StatusMessage = $"Circularizando: dV Restante={remainingDeltaV:F1} m/s | Pe={_vessel.orbit.PeA/1000:F1}km";
            }
        }

        private void FinishAscent()
        {
            _commandedThrottle = 0.0;
            _commandedPitch = 0.0f;
            _commandedYaw = 0.0f;

            if (_circularizationNode != null && _vessel.patchedConicSolver != null)
            {
                _vessel.patchedConicSolver.RemoveManeuverNode(_circularizationNode);
                _circularizationNode = null;
            }

            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            CurrentPhase = AscentPhase.InOrbit;
            IsActive = false;

            StatusMessage = $"Órbita Estabilizada! Ap={_vessel.orbit.ApA/1000:F1}km | Pe={_vessel.orbit.PeA/1000:F1}km";
            ScreenMessages.PostScreenMessage($"<color=#00ff88>[Oráculo 2.0] Órbita alcançada com sucesso!</color>", 8f, ScreenMessageStyle.UPPER_CENTER);
            Debug.Log("[Oraculo 2.0] Lançamento e circularização concluídos com sucesso.");
        }

        /// <summary>
        /// Detecta esgotamento de combustível (flameout) em motores operacionais e desacopla automaticamente.
        /// </summary>
        private void CheckAutoStage()
        {
            if (!EnableAutoStage || _vessel == null) return;
            if (KSP.UI.Screens.StageManager.CurrentStage <= 0) return;

            double now = Planetarium.GetUniversalTime();
            if (now - _lastStageTime < 1.2) return; // Cooldown de 1.2s entre estágios para segurança

            bool anyOperational = false;
            bool flameoutDetected = false;

            for (int i = 0; i < _vessel.Parts.Count; i++)
            {
                var part = _vessel.Parts[i];
                for (int j = 0; j < part.Modules.Count; j++)
                {
                    if (part.Modules[j] is ModuleEngines engine)
                    {
                        if (engine.isOperational)
                        {
                            anyOperational = true;
                            // Flameout ou empuxo zero com acelerador alto indica tanque vazio
                            if (engine.flameout || (engine.currentThrottle > 0.05f && engine.finalThrust <= 0.001f))
                            {
                                flameoutDetected = true;
                                break;
                            }
                        }
                    }
                }
                if (flameoutDetected) break;
            }

            if (flameoutDetected || !anyOperational)
            {
                _lastStageTime = now;
                KSP.UI.Screens.StageManager.ActivateNextStage();
                ScreenMessages.PostScreenMessage(
                    "<color=#00ff88><b>[Oráculo 2.0] Estágio esgotado! Auto-Staging executado.</b></color>",
                    4f,
                    ScreenMessageStyle.UPPER_CENTER
                );
                Debug.Log("[Oraculo 2.0] Auto-staging disparado por detecção de flameout.");
            }
        }

        private void CheckFairingJettison(double altitude)
        {
            if (_fairingsJettisoned || altitude < 50000.0) return;

            for (int i = 0; i < _vessel.Parts.Count; i++)
            {
                var part = _vessel.Parts[i];
                for (int j = 0; j < part.Modules.Count; j++)
                {
                    if (part.Modules[j] is ModuleProceduralFairing fairing)
                    {
                        fairing.DeployFairing();
                        _fairingsJettisoned = true;
                    }
                }
            }

            if (_fairingsJettisoned)
            {
                Debug.Log("[Oraculo 2.0] Coifas de carga útil ejetadas acima de 50km.");
            }
        }

        private void OnFlyByWireCallback(FlightCtrlState state)
        {
            if (!IsActive || _vessel == null) return;

            state.mainThrottle = (float)_commandedThrottle;
            state.pitch = _commandedPitch;
            state.yaw = _commandedYaw;
        }
    }
}
