using System;
using UnityEngine;
using OraculoGuidance.Core;

namespace OraculoGuidance.Modules.Missions
{
    public enum LunarPhase
    {
        Idle,
        CalculatingTransfer,
        WaitingForBurn,
        ExecutingTransferBurn,
        CoastingToMoonSOI,
        CircularizingMoonOrbit,
        InMoonOrbit
    }

    /// <summary>
    /// Controlador autônomo de transferência e inserção orbital para Luas de Kerbin (Mun e Minmus).
    /// Executa:
    /// 1. Cálculo do ângulo de fase de injeção translunar (TLI).
    /// 2. Criação e execução nativa da queima Prograde (~860 m/s para Mun, ~920 m/s para Minmus).
    /// 3. Viagem e detecção de entrada na Esfera de Influência (SOI).
    /// 4. Queima de frenagem retrógrada no periastro para circularização em órbita baixa da Lua.
    /// </summary>
    public class LunarTransferController
    {
        public bool IsActive { get; private set; }
        public LunarPhase CurrentPhase { get; private set; } = LunarPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        private Vessel _vessel;
        private CelestialBody _targetMoon;
        private ManeuverNode _activeNode;
        private double _nodeBurnTime = 0.0;
        private double _targetOrbitAltitude = 25000.0; // 25 km em Mun

        private float _cmdPitch = 0f;
        private float _cmdYaw = 0f;
        private float _cmdThrottle = 0f;

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

        public void StartTransfer(string moonName)
        {
            if (_vessel == null)
            {
                StatusMessage = "Erro: Nave não selecionada.";
                return;
            }

            _targetMoon = FlightGlobals.GetBodyByName(moonName);
            if (_targetMoon == null)
            {
                StatusMessage = $"Erro: Corpo celeste '{moonName}' não encontrado.";
                return;
            }

            if (_vessel.mainBody != FlightGlobals.GetBodyByName("Kerbin"))
            {
                StatusMessage = "Erro: A nave precisa estar em órbita de Kerbin para iniciar.";
                return;
            }

            _targetOrbitAltitude = (moonName == "Minmus") ? 15000.0 : 25000.0;
            IsActive = true;
            CurrentPhase = LunarPhase.CalculatingTransfer;
            StatusMessage = $"Calculando janela e queima de injeção para {_targetMoon.bodyName}...";

            Debug.Log($"[Oraculo 2.0] Transferência Lunar iniciada para {_targetMoon.bodyName}!");
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = LunarPhase.Idle;
            _cmdThrottle = 0f;

            if (_activeNode != null && _vessel != null && _vessel.patchedConicSolver != null)
            {
                _vessel.patchedConicSolver.RemoveManeuverNode(_activeNode);
                _activeNode = null;
            }

            if (_vessel != null)
            {
                _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }

            StatusMessage = "Transferência lunar cancelada.";
        }

        public void Update()
        {
            if (!IsActive || _vessel == null || _targetMoon == null) return;

            switch (CurrentPhase)
            {
                case LunarPhase.CalculatingTransfer:
                    PlanTransferNode();
                    break;

                case LunarPhase.WaitingForBurn:
                    WaitForBurnWindow();
                    break;

                case LunarPhase.ExecutingTransferBurn:
                    ExecuteTransferBurn();
                    break;

                case LunarPhase.CoastingToMoonSOI:
                    MonitorCoastingToSOI();
                    break;

                case LunarPhase.CircularizingMoonOrbit:
                    ExecuteCircularizationBurn();
                    break;

                case LunarPhase.InMoonOrbit:
                    break;
            }
        }

        private void PlanTransferNode()
        {
            double r1 = _vessel.orbit.semiMajorAxis;
            double r2 = _targetMoon.orbit.semiMajorAxis;
            double muKerbin = _vessel.mainBody.gravParameter;

            // Semi-eixo maior da órbita de transferência
            double aTrans = (r1 + r2) / 2.0;
            double tFlight = Math.PI * Math.Sqrt(Math.Pow(aTrans, 3.0) / muKerbin);

            // Deslocamento angular da Lua durante a viagem
            double moonTravelAngle = (360.0 / _targetMoon.orbit.period) * tFlight;
            double leadAngle = (180.0 - moonTravelAngle) % 360.0;

            // Ângulo de fase atual entre a nave e a Lua
            Vector3d posVessel = _vessel.orbit.pos;
            Vector3d posMoon = _targetMoon.orbit.pos;
            double currentPhaseAngle = Vector3d.Angle(posVessel, posMoon);
            Vector3d cross = Vector3d.Cross(posVessel, posMoon);
            if (Vector3d.Dot(cross, _vessel.orbit.GetOrbitNormal()) < 0)
            {
                currentPhaseAngle = 360.0 - currentPhaseAngle;
            }

            double phaseRate = (360.0 / _vessel.orbit.period) - (360.0 / _targetMoon.orbit.period);
            double timeToWindow = ((currentPhaseAngle - leadAngle) % 360.0) / phaseRate;
            if (timeToWindow < 0) timeToWindow += Math.Abs(360.0 / phaseRate);

            // Delta-V da queima prograde de injeção
            double v1 = Math.Sqrt(muKerbin / r1);
            double vTrans = Math.Sqrt(muKerbin * ((2.0 / r1) - (1.0 / aTrans)));
            double dVPrograde = vTrans - v1;

            if (_vessel.patchedConicSolver != null)
            {
                _activeNode = _vessel.patchedConicSolver.AddManeuverNode(Planetarium.GetUniversalTime() + timeToWindow);
                _activeNode.DeltaV = new Vector3d(0, 0, dVPrograde);
                _vessel.patchedConicSolver.UpdateFlightPlan();

                _nodeBurnTime = NavigationMath.CalculateBurnTime(_vessel, dVPrograde);
            }

            CurrentPhase = LunarPhase.WaitingForBurn;
            StatusMessage = $"Injeção planejada para {_targetMoon.bodyName}: dV={dVPrograde:F1} m/s | Espera={timeToWindow/60:F1} min";
            Debug.Log($"[Oraculo 2.0] Nó TLI criado para {_targetMoon.bodyName}: {dVPrograde:F1} m/s.");
        }

        private void WaitForBurnWindow()
        {
            if (_activeNode == null)
            {
                CurrentPhase = LunarPhase.CalculatingTransfer;
                return;
            }

            Vector3d burnVector = _activeNode.GetBurnVector(_vessel.orbit);
            NavigationMath.CalculateSteering(_vessel, burnVector.normalized, ref _cmdPitch, ref _cmdYaw);

            double timeUntilBurn = (_activeNode.UT - Planetarium.GetUniversalTime()) - (_nodeBurnTime / 2.0);

            if (timeUntilBurn <= 0.2)
            {
                CurrentPhase = LunarPhase.ExecutingTransferBurn;
                _cmdThrottle = 1.0f;
                StatusMessage = $"Executando queima de injeção para {_targetMoon.bodyName}...";
            }
            else
            {
                _cmdThrottle = 0f;
                StatusMessage = $"Aguardando TLI: Ignição em {timeUntilBurn:F0}s (dV={burnVector.magnitude:F1} m/s)";
            }
        }

        private void ExecuteTransferBurn()
        {
            if (_activeNode == null)
            {
                CurrentPhase = LunarPhase.CoastingToMoonSOI;
                return;
            }

            Vector3d remainingVector = _activeNode.GetBurnVector(_vessel.orbit);
            double remainingDV = remainingVector.magnitude;

            NavigationMath.CalculateSteering(_vessel, remainingVector.normalized, ref _cmdPitch, ref _cmdYaw);

            if (remainingDV < 30.0)
            {
                _cmdThrottle = (float)Mathf.Clamp((float)(remainingDV / 30.0), 0.08f, 1.0f);
            }
            else
            {
                _cmdThrottle = 1.0f;
            }

            if (remainingDV <= 0.8)
            {
                _cmdThrottle = 0f;
                _vessel.patchedConicSolver.RemoveManeuverNode(_activeNode);
                _activeNode = null;
                CurrentPhase = LunarPhase.CoastingToMoonSOI;
                StatusMessage = $"Injeção concluída! Em trânsito para a Esfera de Influência de {_targetMoon.bodyName}...";
                Debug.Log($"[Oraculo 2.0] Injeção em rota para {_targetMoon.bodyName} bem-sucedida.");
            }
        }

        private void MonitorCoastingToSOI()
        {
            _cmdThrottle = 0f;

            // Se entrou na Esfera de Influência da Lua alvo
            if (_vessel.mainBody == _targetMoon)
            {
                // Verifica aproximação do Periastro na Lua
                double timeToPe = _vessel.orbit.timeToPe;

                // Alinha retrógrado para queima de captura
                Vector3d retrograde = -_vessel.obt_velocity.normalized;
                NavigationMath.CalculateSteering(_vessel, retrograde, ref _cmdPitch, ref _cmdYaw);

                if (timeToPe <= 30.0)
                {
                    CurrentPhase = LunarPhase.CircularizingMoonOrbit;
                    StatusMessage = $"Próximo ao Periastro de {_targetMoon.bodyName}! Iniciando captura orbital...";
                }
                else
                {
                    StatusMessage = $"Na SOI de {_targetMoon.bodyName}: Pe={_vessel.orbit.PeA/1000:F1}km (T-{timeToPe/60:F1} min)";
                }
            }
            else
            {
                StatusMessage = $"Em trânsito interplanetário para {_targetMoon.bodyName}...";
            }
        }

        private void ExecuteCircularizationBurn()
        {
            Vector3d retrograde = -_vessel.obt_velocity.normalized;
            NavigationMath.CalculateSteering(_vessel, retrograde, ref _cmdPitch, ref _cmdYaw);

            // Queima retrógrada para fechar a órbita e circularizar na altitude alvo
            if (_vessel.orbit.ApA > _targetOrbitAltitude * 1.5 || _vessel.orbit.eccentricity > 0.05)
            {
                _cmdThrottle = 1.0f;
                StatusMessage = $"Frenando para circularização em {_targetMoon.bodyName}: Ap={_vessel.orbit.ApA/1000:F1}km | Pe={_vessel.orbit.PeA/1000:F1}km";
            }
            else
            {
                // Órbita estável alcançada!
                _cmdThrottle = 0f;
                CurrentPhase = LunarPhase.InMoonOrbit;
                IsActive = false;

                _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                StatusMessage = $"ÓRBITA DE {_targetMoon.bodyName.ToUpper()} ESTABILIZADA COM SUCESSO!";

                ScreenMessages.PostScreenMessage(
                    $"<color=#00ff88><b>[Oráculo 2.0] ÓRBITA CIRCULAR ALCANÇADA EM {_targetMoon.bodyName.ToUpper()}!</b></color>",
                    10f,
                    ScreenMessageStyle.UPPER_CENTER
                );
                Debug.Log($"[Oraculo 2.0] Missão de captura em {_targetMoon.bodyName} concluída!");
            }
        }

        private void OnFlyByWireCallback(FlightCtrlState state)
        {
            if (!IsActive || _vessel == null) return;

            state.mainThrottle = _cmdThrottle;
            state.pitch = _cmdPitch;
            state.yaw = _cmdYaw;
        }
    }
}
