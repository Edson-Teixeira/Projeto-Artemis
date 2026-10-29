using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using MuMech;
using Contracts;

namespace OraculoGuidance.Modules.Contracts
{
    public class OrbitTarget
    {
        public string Title;
        public CelestialBody Body;
        public double ApoapsisKm;
        public double PeriapsisKm;
        public double InclinationDeg;
        public double LongitudeOfAscendingNode;
        public double ArgumentOfPeriapsis;

        public OrbitTarget(string title, CelestialBody body, double apKm, double peKm, double incDeg, double lan = 0, double argPe = 0)
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

    public enum AlignmentPhase
    {
        Idle,
        CalculatingPlaneChange,
        ExecutingPlaneChange,
        CalculatingApoapsisChange,
        ExecutingApoapsisChange,
        CalculatingPeriapsisChange,
        ExecutingPeriapsisChange,
        Finished
    }

    /// <summary>
    /// Controlador automático para inserção de satélites em órbitas específicas de contratos.
    /// Lê os contratos ativos da carreira e calcula/executa a sequência de manobras orbitais.
    /// </summary>
    public class SatelliteContractController
    {
        public bool IsActive { get; private set; }
        public AlignmentPhase CurrentPhase { get; private set; } = AlignmentPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        public List<OrbitTarget> DetectedContracts { get; private set; } = new List<OrbitTarget>();
        public OrbitTarget SelectedTarget { get; set; }

        private Vessel _vessel;
        private MechJebCore _core;

        public void Initialize(Vessel vessel, MechJebCore core)
        {
            _vessel = vessel;
            _core = core;
            RefreshActiveContracts();
        }

        /// <summary>
        /// Varre o ContractSystem do KSP em busca de missões ativas que exijam órbita específica
        /// </summary>
        public void RefreshActiveContracts()
        {
            DetectedContracts.Clear();

            if (ContractSystem.Instance == null || ContractSystem.Instance.Contracts == null) return;

            foreach (var contract in ContractSystem.Instance.Contracts)
            {
                if (contract.ContractState != Contract.State.Active) continue;

                // Procura parâmetros de órbita específica nos contratos ativos
                foreach (var param in contract.AllParameters)
                {
                    var paramType = param.GetType();

                    // Verifica se o parâmetro possui campos clássicos de SpecificOrbitParameter
                    PropertyInfo bodyProp = paramType.GetProperty("TargetBody") ?? paramType.GetProperty("targetBody");
                    PropertyInfo smaProp = paramType.GetProperty("semiMajorAxis") ?? paramType.GetProperty("SemiMajorAxis");
                    PropertyInfo eccProp = paramType.GetProperty("eccentricity") ?? paramType.GetProperty("Eccentricity");
                    PropertyInfo incProp = paramType.GetProperty("inclination") ?? paramType.GetProperty("Inclination");
                    PropertyInfo lanProp = paramType.GetProperty("longitudeOfAscendingNode") ?? paramType.GetProperty("LAN");

                    if (incProp != null && (smaProp != null || eccProp != null))
                    {
                        try
                        {
                            CelestialBody body = (bodyProp != null) ? (CelestialBody)bodyProp.GetValue(param, null) : FlightGlobals.currentMainBody;
                            double sma = smaProp != null ? Convert.ToDouble(smaProp.GetValue(param, null)) : 700000;
                            double ecc = eccProp != null ? Convert.ToDouble(eccProp.GetValue(param, null)) : 0;
                            double inc = Convert.ToDouble(incProp.GetValue(param, null));
                            double lan = (lanProp != null) ? Convert.ToDouble(lanProp.GetValue(param, null)) : 0;

                            double apMeters = (sma * (1.0 + ecc)) - (body != null ? body.Radius : 600000);
                            double peMeters = (sma * (1.0 - ecc)) - (body != null ? body.Radius : 600000);

                            string title = $"{contract.Title} ({body?.bodyName ?? "Kerbin"}: {apMeters/1000:F0}x{peMeters/1000:F0}km, {inc:F1}°)";
                            DetectedContracts.Add(new OrbitTarget(title, body, apMeters / 1000.0, peMeters / 1000.0, inc, lan));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Oraculo] Erro ao extrair dados de órbita do contrato: {ex.Message}");
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

        public void StartAlignment(OrbitTarget target)
        {
            if (target == null || _vessel == null || _core == null)
            {
                StatusMessage = "Erro: Alvo ou nave inválida.";
                return;
            }

            SelectedTarget = target;
            IsActive = true;
            CurrentPhase = AlignmentPhase.CalculatingPlaneChange;
            StatusMessage = "Iniciando correção de plano (Inclinação e LAN)...";

            PlanNextManeuver();
            Debug.Log($"[Oraculo] Iniciado alinhamento com contrato: {target.Title}");
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = AlignmentPhase.Idle;
            StatusMessage = "Alinhamento abortado.";

            // Cancela nós pendentes no KSP
            if (_vessel != null && _vessel.patchedConicSolver != null)
            {
                while (_vessel.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    _vessel.patchedConicSolver.RemoveManeuverNode(_vessel.patchedConicSolver.maneuverNodes[0]);
                }
            }

            if (_core != null)
            {
                var executor = _core.GetComputerModule<MechJebModuleNodeExecutor>();
                if (executor != null)
                {
                    executor.Users.Remove(this);
                    executor.Deactivate();
                }
            }
        }

        public void Update()
        {
            if (!IsActive || _vessel == null || _core == null) return;

            var executor = _core.GetComputerModule<MechJebModuleNodeExecutor>();
            bool isNodeExecuting = (executor != null && executor.enabled);

            // Se o nó terminou de executar, passa para a próxima etapa da órbita
            if (!isNodeExecuting)
            {
                switch (CurrentPhase)
                {
                    case AlignmentPhase.ExecutingPlaneChange:
                        CurrentPhase = AlignmentPhase.CalculatingApoapsisChange;
                        PlanNextManeuver();
                        break;

                    case AlignmentPhase.ExecutingApoapsisChange:
                        CurrentPhase = AlignmentPhase.CalculatingPeriapsisChange;
                        PlanNextManeuver();
                        break;

                    case AlignmentPhase.ExecutingPeriapsisChange:
                        CurrentPhase = AlignmentPhase.Finished;
                        IsActive = false;
                        StatusMessage = "Órbita alinhada com sucesso! Aguardando validação do contrato...";
                        ScreenMessages.PostScreenMessage(
                            $"<color=#00ff88>[Oráculo] Satélite em órbita do contrato ({SelectedTarget.ApoapsisKm:F0}x{SelectedTarget.PeriapsisKm:F0}km)!</color>",
                            8f,
                            ScreenMessageStyle.UPPER_CENTER
                        );
                        break;
                }
            }
        }

        private void PlanNextManeuver()
        {
            if (_vessel == null || _core == null || SelectedTarget == null) return;

            var planner = _core.GetComputerModule<MechJebModuleManeuverPlanner>();
            var executor = _core.GetComputerModule<MechJebModuleNodeExecutor>();

            if (executor == null)
            {
                StatusMessage = "Módulo de execução de nós não disponível.";
                return;
            }

            double ut = Planetarium.GetUniversalTime();

            switch (CurrentPhase)
            {
                case AlignmentPhase.CalculatingPlaneChange:
                    // Calcula diferença de inclinação
                    double currentInc = _vessel.orbit.inclination;
                    double deltaInc = SelectedTarget.InclinationDeg - currentInc;

                    if (Math.Abs(deltaInc) > 0.2)
                    {
                        StatusMessage = $"Ajustando inclinação em {deltaInc:F1}° no próximo nó...";
                        // Cria nó no nó ascendente ou descendente
                        double timeToNode = _vessel.orbit.timeToAp; // aproximação segura
                        double normalDv = 2.0 * _vessel.orbit.getOrbitalSpeedAt(ut + timeToNode) * Math.Sin((deltaInc * Math.PI / 180.0) / 2.0);

                        CreateManeuverNode(ut + timeToNode, 0, normalDv, 0);
                        ExecuteCurrentNode();
                        CurrentPhase = AlignmentPhase.ExecutingPlaneChange;
                    }
                    else
                    {
                        // Já está com inclinação correta, pula para apoastro
                        CurrentPhase = AlignmentPhase.CalculatingApoapsisChange;
                        PlanNextManeuver();
                    }
                    break;

                case AlignmentPhase.CalculatingApoapsisChange:
                    StatusMessage = $"Ajustando Apoastro para {SelectedTarget.ApoapsisKm:F0} km...";
                    double targetAp = SelectedTarget.ApoapsisKm * 1000;
                    double currentAp = _vessel.orbit.ApA;

                    if (Math.Abs(currentAp - targetAp) > 2000)
                    {
                        // Queima no Periastro para ajustar o Apoastro
                        double burnUT = ut + _vessel.orbit.timeToPe;
                        double progradeDv = CalculateHohmannDvAtPe(_vessel.orbit, targetAp);

                        CreateManeuverNode(burnUT, 0, 0, progradeDv);
                        ExecuteCurrentNode();
                        CurrentPhase = AlignmentPhase.ExecutingApoapsisChange;
                    }
                    else
                    {
                        CurrentPhase = AlignmentPhase.CalculatingPeriapsisChange;
                        PlanNextManeuver();
                    }
                    break;

                case AlignmentPhase.CalculatingPeriapsisChange:
                    StatusMessage = $"Ajustando Periastro para {SelectedTarget.PeriapsisKm:F0} km...";
                    double targetPe = SelectedTarget.PeriapsisKm * 1000;
                    double currentPe = _vessel.orbit.PeA;

                    if (Math.Abs(currentPe - targetPe) > 2000)
                    {
                        // Queima no Apoastro para ajustar o Periastro
                        double burnUT = ut + _vessel.orbit.timeToAp;
                        double progradeDv = CalculateHohmannDvAtAp(_vessel.orbit, targetPe);

                        CreateManeuverNode(burnUT, 0, 0, progradeDv);
                        ExecuteCurrentNode();
                        CurrentPhase = AlignmentPhase.ExecutingPeriapsisChange;
                    }
                    else
                    {
                        CurrentPhase = AlignmentPhase.Finished;
                        IsActive = false;
                        StatusMessage = "Órbita idêntica ao contrato!";
                    }
                    break;
            }
        }

        private void CreateManeuverNode(double ut, double radial, double normal, double prograde)
        {
            if (_vessel.patchedConicSolver == null) return;
            var node = _vessel.patchedConicSolver.AddManeuverNode(ut);
            node.DeltaV = new Vector3d(radial, normal, prograde);
            _vessel.patchedConicSolver.UpdateFlightPlan();
        }

        private void ExecuteCurrentNode()
        {
            var executor = _core.GetComputerModule<MechJebModuleNodeExecutor>();
            if (executor != null)
            {
                executor.autowarp = true;
                executor.Users.Add(this);
                executor.ExecuteOneNode(this);
            }
        }

        private double CalculateHohmannDvAtPe(Orbit orbit, double newAp)
        {
            double mu = orbit.referenceBody.gravParameter;
            double rPe = orbit.PeR;
            double v1 = Math.Sqrt(mu * ((2.0 / rPe) - (1.0 / orbit.semiMajorAxis)));
            double aNew = (rPe + newAp + orbit.referenceBody.Radius) / 2.0;
            double v2 = Math.Sqrt(mu * ((2.0 / rPe) - (1.0 / aNew)));
            return v2 - v1;
        }

        private double CalculateHohmannDvAtAp(Orbit orbit, double newPe)
        {
            double mu = orbit.referenceBody.gravParameter;
            double rAp = orbit.ApR;
            double v1 = Math.Sqrt(mu * ((2.0 / rAp) - (1.0 / orbit.semiMajorAxis)));
            double aNew = (rAp + newPe + orbit.referenceBody.Radius) / 2.0;
            double v2 = Math.Sqrt(mu * ((2.0 / rAp) - (1.0 / aNew)));
            return v2 - v1;
        }
    }
}
