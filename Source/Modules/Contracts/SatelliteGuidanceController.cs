using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Contracts;
using OraculoGuidance.Core;

namespace OraculoGuidance.Modules.Contracts
{
    public class SpecificOrbit
    {
        public string Title;
        public CelestialBody Body;
        public double ApoapsisKm;
        public double PeriapsisKm;
        public double InclinationDeg;
        public double LongitudeOfAscendingNode;
        public double ArgumentOfPeriapsis;

        public SpecificOrbit(string title, CelestialBody body, double apKm, double peKm, double incDeg, double lan = 0, double argPe = 0)
        {
            Title = title;
            Body = body;
            ApoapsisKm = apKm;
            PeriapsisKm = peKm;
            InclinationDeg = incDeg;
            LongitudeOfAscendingNode = lan;
            ArgumentOfPeriapsis = argPe;
        }
    }

    public enum SatellitePhase
    {
        Idle,
        CalculatingPlaneChange,
        ExecutingPlaneChange,
        CalculatingApoapsisChange,
        ExecutingApoapsisChange,
        CalculatingPeriapsisChange,
        ExecutingPeriapsisChange,
        HoldingStabilityForContract,
        Finished
    }

    /// <summary>
    /// Controlador autônomo nativo de inserção de satélites em órbitas específicas de contratos (Oráculo 2.0).
    /// Detecta missões ativas na carreira via ContractSystem ou recebe parâmetros manuais.
    /// Respeita o Argumento do Periastro (ω) para garantir que o Apoastro e o Periastro
    /// fiquem exatamente no lado correto do planeta (linha visual da missão).
    /// </summary>
    public class SatelliteGuidanceController
    {
        public bool IsActive { get; private set; }
        public SatellitePhase CurrentPhase { get; private set; } = SatellitePhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        public List<SpecificOrbit> DetectedContracts { get; private set; } = new List<SpecificOrbit>();
        public SpecificOrbit SelectedTarget { get; set; }

        private Vessel _vessel;
        private ManeuverNode _activeNode;
        private double _nodeBurnTime = 0.0;
        private double _stabilityStartTime = 0.0;

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

            RefreshActiveContracts();
        }

        public void RefreshActiveContracts()
        {
            DetectedContracts.Clear();

            if (ContractSystem.Instance == null || ContractSystem.Instance.Contracts == null) return;

            foreach (var contract in ContractSystem.Instance.Contracts)
            {
                if (contract.ContractState != Contract.State.Active) continue;

                foreach (var param in contract.AllParameters)
                {
                    var paramType = param.GetType();

                    PropertyInfo bodyProp = paramType.GetProperty("TargetBody") ?? paramType.GetProperty("targetBody");
                    PropertyInfo smaProp = paramType.GetProperty("semiMajorAxis") ?? paramType.GetProperty("SemiMajorAxis");
                    PropertyInfo eccProp = paramType.GetProperty("eccentricity") ?? paramType.GetProperty("Eccentricity");
                    PropertyInfo incProp = paramType.GetProperty("inclination") ?? paramType.GetProperty("Inclination");
                    PropertyInfo lanProp = paramType.GetProperty("longitudeOfAscendingNode") ?? paramType.GetProperty("LAN");
                    PropertyInfo argPeProp = paramType.GetProperty("argumentOfPeriapsis") ?? paramType.GetProperty("ArgumentOfPeriapsis");

                    if (incProp != null && (smaProp != null || eccProp != null))
                    {
                        try
                        {
                            CelestialBody body = (bodyProp != null) ? (CelestialBody)bodyProp.GetValue(param, null) : FlightGlobals.currentMainBody;
                            double sma = smaProp != null ? Convert.ToDouble(smaProp.GetValue(param, null)) : 700000;
                            double ecc = eccProp != null ? Convert.ToDouble(eccProp.GetValue(param, null)) : 0;
                            double inc = Convert.ToDouble(incProp.GetValue(param, null));
                            double lan = (lanProp != null) ? Convert.ToDouble(lanProp.GetValue(param, null)) : 0;
                            double argPe = (argPeProp != null) ? Convert.ToDouble(argPeProp.GetValue(param, null)) : 0;

                            double apMeters = (sma * (1.0 + ecc)) - (body != null ? body.Radius : 600000);
                            double peMeters = (sma * (1.0 - ecc)) - (body != null ? body.Radius : 600000);

                            string title = $"{contract.Title} ({body?.bodyName ?? "Kerbin"}: {apMeters/1000:F0}x{peMeters/1000:F0}km, {inc:F1}°, ω={argPe:F0}°)";
                            DetectedContracts.Add(new SpecificOrbit(title, body, apMeters / 1000.0, peMeters / 1000.0, inc, lan, argPe));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Oraculo 2.0] Erro ao extrair dados de contrato: {ex.Message}");
                        }
                        break;
                    }
                }
            }

            if (DetectedContracts.Count > 0 && SelectedTarget == null)
            {
                SelectedTarget = DetectedContracts[0];
            }
        }

        public void StartAlignment(SpecificOrbit target)
        {
            if (target == null || _vessel == null)
            {
                StatusMessage = "Erro: Alvo ou nave inválida.";
                return;
            }

            SelectedTarget = target;
            IsActive = true;
            CurrentPhase = SatellitePhase.CalculatingPlaneChange;
            StatusMessage = "Iniciando Etapa 1: Correção de Plano Orbital (Inclinação e LAN)...";

            Debug.Log($"[Oraculo 2.0] Alinhamento com contrato: {target.Title} (ω={target.ArgumentOfPeriapsis:F1}°)");
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = SatellitePhase.Idle;
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

            StatusMessage = "Alinhamento de satélite abortado.";
        }

        public void Update()
        {
            if (!IsActive || _vessel == null || SelectedTarget == null) return;

            switch (CurrentPhase)
            {
                case SatellitePhase.CalculatingPlaneChange:
                    PlanPlaneChangeNode();
                    break;

                case SatellitePhase.ExecutingPlaneChange:
                    ExecuteNodeBurn(nextPhase: SatellitePhase.CalculatingApoapsisChange, "Plano alinhado! Calculando ajuste de Apoastro casado com o Argumento do Periastro (ω)...");
                    break;

                case SatellitePhase.CalculatingApoapsisChange:
                    PlanApoapsisChangeNode();
                    break;

                case SatellitePhase.ExecutingApoapsisChange:
                    ExecuteNodeBurn(nextPhase: SatellitePhase.CalculatingPeriapsisChange, "Apoastro no lado correto! Calculando ajuste fino de Periastro...");
                    break;

                case SatellitePhase.CalculatingPeriapsisChange:
                    PlanPeriapsisChangeNode();
                    break;

                case SatellitePhase.ExecutingPeriapsisChange:
                    ExecuteNodeBurn(nextPhase: SatellitePhase.HoldingStabilityForContract, "Órbita geométrica casada! Estabilizando para conclusão do contrato...");
                    break;

                case SatellitePhase.HoldingStabilityForContract:
                    ExecuteStabilityHold();
                    break;

                case SatellitePhase.Finished:
                    break;
            }
        }

        #region Etapa 1: Correção de Plano (Inclinação e LAN)

        private void PlanPlaneChangeNode()
        {
            double deltaInc = SelectedTarget.InclinationDeg - _vessel.orbit.inclination;

            if (Math.Abs(deltaInc) < 0.2)
            {
                CurrentPhase = SatellitePhase.CalculatingApoapsisChange;
                return;
            }

            double timeToNode = _vessel.orbit.period * 0.25;
            double v = Math.Sqrt(_vessel.mainBody.gravParameter / _vessel.orbit.semiMajorAxis);
            double deltaVNormal = 2.0 * v * Math.Sin((Math.Abs(deltaInc) * Mathf.Deg2Rad) / 2.0);
            if (deltaInc < 0) deltaVNormal = -deltaVNormal;

            CreateManeuverNode(Planetarium.GetUniversalTime() + timeToNode, new Vector3d(0, deltaVNormal, 0));
            CurrentPhase = SatellitePhase.ExecutingPlaneChange;
            StatusMessage = $"Etapa 1/3: Corrigindo Plano (dV={Math.Abs(deltaVNormal):F1} m/s | Inc alvo: {SelectedTarget.InclinationDeg:F1}°)";
        }

        #endregion

        #region Etapa 2: Ajuste de Apoastro Alinhado ao Argumento do Periastro (ω)

        private void PlanApoapsisChangeNode()
        {
            double currentApMeters = _vessel.orbit.ApA;
            double targetApMeters = SelectedTarget.ApoapsisKm * 1000.0;
            double error = targetApMeters - currentApMeters;

            if (Math.Abs(error) < 1500.0)
            {
                CurrentPhase = SatellitePhase.CalculatingPeriapsisChange;
                return;
            }

            // Para garantir que o Periastro fique exatamente no ponto ω especificado no contrato:
            // Queimamos Prograde no Ponto ω (Argumento de Latitude da posição do Periastro alvo).
            // Ao queimar Prograde nesse ponto exato, o ponto atual vira o Periastro da nova órbita
            // e o ponto diametralmente oposto (ω + 180°) vira o Apoastro!
            double currentArgOfLat = (_vessel.orbit.trueAnomaly + _vessel.orbit.argumentOfPeriapsis) % 360.0;
            double targetArgOfLat = SelectedTarget.ArgumentOfPeriapsis % 360.0;

            double deltaAngle = (targetArgOfLat - currentArgOfLat) % 360.0;
            if (deltaAngle < 0) deltaAngle += 360.0;

            double timeToBurnPoint = (deltaAngle / 360.0) * _vessel.orbit.period;
            if (timeToBurnPoint < 60.0) timeToBurnPoint += _vessel.orbit.period; // Garante tempo para girar a nave

            double rBurn = _vessel.orbit.radius;
            double mu = _vessel.mainBody.gravParameter;
            double currentSMA = _vessel.orbit.semiMajorAxis;
            double targetSMA = (rBurn + (targetApMeters + _vessel.mainBody.Radius)) / 2.0;

            double v1 = Math.Sqrt(mu * ((2.0 / rBurn) - (1.0 / currentSMA)));
            double v2 = Math.Sqrt(mu * ((2.0 / rBurn) - (1.0 / targetSMA)));
            double dVPrograde = v2 - v1;

            CreateManeuverNode(Planetarium.GetUniversalTime() + timeToBurnPoint, new Vector3d(0, 0, dVPrograde));
            CurrentPhase = SatellitePhase.ExecutingApoapsisChange;
            StatusMessage = $"Etapa 2/3: Queimando em ω={SelectedTarget.ArgumentOfPeriapsis:F0}° para orientar a elipse no lado correto!";
        }

        #endregion

        #region Etapa 3: Ajuste de Periastro (Queima no Apoastro)

        private void PlanPeriapsisChangeNode()
        {
            double currentPeMeters = _vessel.orbit.PeA;
            double targetPeMeters = SelectedTarget.PeriapsisKm * 1000.0;
            double error = targetPeMeters - currentPeMeters;

            if (Math.Abs(error) < 1500.0)
            {
                CurrentPhase = SatellitePhase.HoldingStabilityForContract;
                _stabilityStartTime = Planetarium.GetUniversalTime();
                return;
            }

            // Queima no novo Apoastro para ajustar o Periastro exatamente no lado oposto
            double timeToAp = _vessel.orbit.timeToAp;
            double mu = _vessel.mainBody.gravParameter;
            double rAp = _vessel.orbit.ApR;
            double currentSMA = _vessel.orbit.semiMajorAxis;
            double targetSMA = (rAp + (targetPeMeters + _vessel.mainBody.Radius)) / 2.0;

            double v1 = Math.Sqrt(mu * ((2.0 / rAp) - (1.0 / currentSMA)));
            double v2 = Math.Sqrt(mu * ((2.0 / rAp) - (1.0 / targetSMA)));
            double dVPrograde = v2 - v1;

            CreateManeuverNode(Planetarium.GetUniversalTime() + timeToAp, new Vector3d(0, 0, dVPrograde));
            CurrentPhase = SatellitePhase.ExecutingPeriapsisChange;
            StatusMessage = $"Etapa 3/3: Queima no Apoastro ajustando Periastro para {SelectedTarget.PeriapsisKm:F0}km (dV={dVPrograde:F1} m/s)";
        }

        #endregion

        #region Execução Nativa de Nós e Estabilização

        private void CreateManeuverNode(double universalTime, Vector3d deltaV)
        {
            if (_vessel.patchedConicSolver == null) return;

            _activeNode = _vessel.patchedConicSolver.AddManeuverNode(universalTime);
            _activeNode.DeltaV = deltaV;
            _vessel.patchedConicSolver.UpdateFlightPlan();

            _nodeBurnTime = NavigationMath.CalculateBurnTime(_vessel, deltaV.magnitude);
        }

        private void ExecuteNodeBurn(SatellitePhase nextPhase, string completionMsg)
        {
            if (_activeNode == null)
            {
                CurrentPhase = nextPhase;
                return;
            }

            Vector3d burnVector = _activeNode.GetBurnVector(_vessel.orbit);
            double remainingDV = burnVector.magnitude;

            NavigationMath.CalculateSteering(_vessel, burnVector.normalized, ref _cmdPitch, ref _cmdYaw);

            double timeUntilBurn = (_activeNode.UT - Planetarium.GetUniversalTime()) - (_nodeBurnTime / 2.0);

            if (timeUntilBurn <= 0.2)
            {
                if (remainingDV < 25.0)
                {
                    _cmdThrottle = (float)Mathf.Clamp((float)(remainingDV / 25.0), 0.05f, 0.6f);
                }
                else
                {
                    _cmdThrottle = 1.0f;
                }

                if (remainingDV <= 0.4)
                {
                    _cmdThrottle = 0f;
                    _vessel.patchedConicSolver.RemoveManeuverNode(_activeNode);
                    _activeNode = null;
                    CurrentPhase = nextPhase;
                    StatusMessage = completionMsg;

                    if (nextPhase == SatellitePhase.HoldingStabilityForContract)
                    {
                        _stabilityStartTime = Planetarium.GetUniversalTime();
                    }
                }
            }
            else
            {
                _cmdThrottle = 0f;
                StatusMessage = $"Alinhando com nó: Ignição em {timeUntilBurn:F0}s (dV={remainingDV:F1} m/s)";
            }
        }

        private void ExecuteStabilityHold()
        {
            _cmdThrottle = 0f;
            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

            double elapsed = Planetarium.GetUniversalTime() - _stabilityStartTime;
            if (elapsed >= 10.0)
            {
                CurrentPhase = SatellitePhase.Finished;
                IsActive = false;
                StatusMessage = "ÓRBITA DO CONTRATO CONCLUÍDA COM SUCESSO!";

                ScreenMessages.PostScreenMessage(
                    $"<color=#00ff88><b>[Oráculo 2.0] ÓRBITA DE SATÉLITE ATINGIDA! Contrato Concluído.</b></color>",
                    10f,
                    ScreenMessageStyle.UPPER_CENTER
                );
                Debug.Log("[Oraculo 2.0] Missão de satélite finalizada com sucesso.");
            }
            else
            {
                StatusMessage = $"Estabilizando para registro do contrato ({elapsed:F0}s / 10s)...";
            }
        }

        #endregion

        private void OnFlyByWireCallback(FlightCtrlState state)
        {
            if (!IsActive || _vessel == null) return;

            state.mainThrottle = _cmdThrottle;
            state.pitch = _cmdPitch;
            state.yaw = _cmdYaw;
        }
    }
}
