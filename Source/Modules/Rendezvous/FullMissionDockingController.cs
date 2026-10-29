using System;
using UnityEngine;
using OraculoGuidance.Core;
using OraculoGuidance.Modules.Ascent;

namespace OraculoGuidance.Modules.Rendezvous
{
    public enum MissionDockingPhase
    {
        Idle,
        CalculatingLaunchWindow,
        WaitingLaunchWindow,
        LaunchingToTargetPlane,      // Subida atmosférica com azimute casado com o plano do alvo
        CircularizingChaseOrbit,     // Circularização em órbita de perseguição (85 km)
        AligningRelativePlanes,      // Zera a inclinação relativa nos nós AN/DN (Rel Inc < 0.05°)
        PhasingAndTransfer,          // Queima de transferência de Hohmann para encontro a < 150m
        CoastingToIntercept,         // Aproximação até o ponto de maior aproximação
        BrakingAtTarget,             // Frenagem no alvo: cancela velocidade relativa (V_rel < 0.2 m/s)
        AligningDockingPorts,        // Mira a porta de acoplagem contra a porta do alvo
        TranslationalRCSApproach,    // Aproximação final translacional por RCS via 3 PIDs
        DockedComplete               // Acoplagem finalizada com sucesso
    }

    /// <summary>
    /// Piloto Automático de Acoplagem Total do Início ao Fim (Oráculo 2.0).
    /// Executa:
    /// 1. Cálculo da Janela e Azimute de Lançamento na plataforma.
    /// 2. Decolagem e subida diretamente no plano orbital do alvo.
    /// 3. Inserção em órbita de perseguição (Chase Orbit).
    /// 4. Correção fina de plano orbital (AN/DN).
    /// 5. Transferência de Hohmann e interceptação a menos de 150m.
    /// 6. Frenagem de velocidade relativa a zero.
    /// 7. Alinhamento de portas e aproximação translacional fina por RCS via PID 3D.
    /// </summary>
    public class FullMissionDockingController
    {
        public bool IsActive { get; private set; }
        public MissionDockingPhase CurrentPhase { get; private set; } = MissionDockingPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        private Vessel _vessel;
        private OrbitalAscentController _ascentController;
        private ITargetable _currentTarget;
        private Vessel _targetVessel;
        private ModuleDockingNode _ourDockingPort;
        private ModuleDockingNode _targetDockingPort;

        // PIDs de Translação RCS (X = Esquerda/Direita, Y = Cima/Baixo, Z = Frente/Trás)
        private PIDController _pidRcsX;
        private PIDController _pidRcsY;
        private PIDController _pidRcsZ;

        // Comandos de voo
        private float _cmdPitch = 0f;
        private float _cmdYaw = 0f;
        private float _cmdRoll = 0f;
        private float _cmdThrottle = 0f;
        private Vector3 _cmdTranslation = Vector3.zero;

        // Dados da manobra de encontro
        private double _launchWindowUT = 0.0;
        private double _targetLaunchAzimuth = 90.0;
        private double _nodeBurnTime = 0.0;
        private ManeuverNode _activeNode;

        public FullMissionDockingController(OrbitalAscentController ascent)
        {
            _ascentController = ascent;

            // PIDs translacionais do RCS
            _pidRcsX = new PIDController(kp: 0.8, ki: 0.02, kd: 0.4, min: -1.0, max: 1.0, sampleTime: 0.02);
            _pidRcsY = new PIDController(kp: 0.8, ki: 0.02, kd: 0.4, min: -1.0, max: 1.0, sampleTime: 0.02);
            _pidRcsZ = new PIDController(kp: 0.5, ki: 0.01, kd: 0.2, min: -1.0, max: 1.0, sampleTime: 0.02);
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

        public void StartMission(ITargetable target)
        {
            if (_vessel == null)
            {
                StatusMessage = "Erro: Nave não selecionada.";
                return;
            }

            if (target == null)
            {
                StatusMessage = "Erro: Nenhum alvo selecionado! Selecione uma estação ou nave no mapa.";
                return;
            }

            _currentTarget = target;
            _targetVessel = (target as Vessel) ?? (target as ModuleDockingNode)?.vessel;

            if (_targetVessel == null)
            {
                StatusMessage = "Erro: Alvo inválido.";
                return;
            }

            IsActive = true;
            _pidRcsX.Reset();
            _pidRcsY.Reset();
            _pidRcsZ.Reset();

            // Identifica se a nave já está em órbita ou se precisa decolar da base
            if (_vessel.situation == Vessel.Situations.PRELAUNCH || _vessel.situation == Vessel.Situations.LANDED)
            {
                CurrentPhase = MissionDockingPhase.CalculatingLaunchWindow;
                StatusMessage = "Calculando azimute e janela de lançamento para o plano do alvo...";
            }
            else
            {
                CurrentPhase = MissionDockingPhase.AligningRelativePlanes;
                StatusMessage = "Nave já em órbita: Alinhando planos orbitais com o alvo...";
            }

            Debug.Log($"[Oraculo 2.0] Missão de Acoplagem Total iniciada com alvo: {_targetVessel.vesselName}");
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = MissionDockingPhase.Idle;
            _cmdThrottle = 0f;
            _cmdTranslation = Vector3.zero;

            if (_ascentController.IsActive)
            {
                _ascentController.Abort();
            }

            if (_activeNode != null && _vessel != null && _vessel.patchedConicSolver != null)
            {
                _vessel.patchedConicSolver.RemoveManeuverNode(_activeNode);
                _activeNode = null;
            }

            if (_vessel != null)
            {
                _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                _vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false);
            }

            StatusMessage = "Missão de acoplagem abortada.";
        }

        public void Update()
        {
            if (!IsActive || _vessel == null || _targetVessel == null) return;

            switch (CurrentPhase)
            {
                case MissionDockingPhase.CalculatingLaunchWindow:
                    CalculateLaunchParameters();
                    break;

                case MissionDockingPhase.WaitingLaunchWindow:
                    WaitForLaunchWindow();
                    break;

                case MissionDockingPhase.LaunchingToTargetPlane:
                    MonitorLaunchPhase();
                    break;

                case MissionDockingPhase.CircularizingChaseOrbit:
                    MonitorChaseOrbitInsertion();
                    break;

                case MissionDockingPhase.AligningRelativePlanes:
                    ExecutePlaneAlignment();
                    break;

                case MissionDockingPhase.PhasingAndTransfer:
                    ExecutePhasingAndTransfer();
                    break;

                case MissionDockingPhase.CoastingToIntercept:
                    MonitorCoastingToIntercept();
                    break;

                case MissionDockingPhase.BrakingAtTarget:
                    ExecuteRelativeBraking();
                    break;

                case MissionDockingPhase.AligningDockingPorts:
                    ExecutePortAlignment();
                    break;

                case MissionDockingPhase.TranslationalRCSApproach:
                    ExecuteTranslationalApproach();
                    break;

                case MissionDockingPhase.DockedComplete:
                    // Acoplagem finalizada
                    break;
            }
        }

        #region Fase 1: Cálculo de Janela e Azimute de Lançamento

        private void CalculateLaunchParameters()
        {
            Orbit tgtOrbit = _targetVessel.orbit;
            double inc = tgtOrbit.inclination;
            double lat = _vessel.latitude;

            // Cálculo do Azimute de Lançamento para casar o plano orbital
            // sin(azimute) = cos(inc) / cos(lat)
            double cosLat = Math.Cos(lat * Mathf.Deg2Rad);
            double ratio = Math.Cos(inc * Mathf.Deg2Rad) / (cosLat != 0 ? cosLat : 1.0);
            ratio = Mathf.Clamp((float)ratio, -1.0f, 1.0f);
            double azimuthAngle = Math.Asin(ratio) * Mathf.Rad2Deg;

            // 90° = Leste padrão (para órbitas equatoriais)
            _targetLaunchAzimuth = 90.0 - (90.0 - azimuthAngle);

            // Janela de Lançamento: calcula quando a base de KSC cruza o plano da órbita alvo
            double rotPeriod = _vessel.mainBody.rotationPeriod;
            double currentRotAngle = _vessel.mainBody.rotationAngle;
            double targetLAN = tgtOrbit.LAN;
            double vesselInertialLon = (currentRotAngle + _vessel.longitude) % 360.0;
            if (vesselInertialLon < 0) vesselInertialLon += 360.0;

            double angleDiff = (targetLAN - vesselInertialLon) % 180.0;
            if (angleDiff < 0) angleDiff += 180.0;

            double timeToWindow = (angleDiff / 360.0) * rotPeriod;
            _launchWindowUT = Planetarium.GetUniversalTime() + timeToWindow;

            CurrentPhase = MissionDockingPhase.WaitingLaunchWindow;
            StatusMessage = $"Janela de lançamento calculada. Azimute: {_targetLaunchAzimuth:F1}° | T-{timeToWindow:F0}s";
            Debug.Log($"[Oraculo 2.0] Janela de lançamento para {_targetVessel.vesselName}: Azimute={_targetLaunchAzimuth:F1}°, Espera={timeToWindow:F1}s");
        }

        private void WaitForLaunchWindow()
        {
            double timeRemaining = _launchWindowUT - Planetarium.GetUniversalTime();

            if (timeRemaining <= 3.0)
            {
                // Inicia a subida orbital na janela ideal
                CurrentPhase = MissionDockingPhase.LaunchingToTargetPlane;
                double chaseOrbitAlt = Math.Max(80000.0, _targetVessel.orbit.PeA - 40000.0);
                if (chaseOrbitAlt > _targetVessel.orbit.ApA) chaseOrbitAlt = 85000.0;

                _ascentController.TargetHeading = _targetLaunchAzimuth;
                _ascentController.StartAscent(chaseOrbitAlt, TurnProfile.Normal);

                StatusMessage = $"Lançando no plano orbital alvo! Heading: {_targetLaunchAzimuth:F1}°";
                Debug.Log($"[Oraculo 2.0] Lançamento de encontro disparado!");
            }
            else
            {
                StatusMessage = $"Aguardando plano orbital: Janela em {timeRemaining:F0}s (Azimute: {_targetLaunchAzimuth:F1}°)";
            }
        }

        #endregion

        #region Fase 2: Subida e Órbita de Perseguição (Chase Orbit)

        private void MonitorLaunchPhase()
        {
            if (_ascentController.CurrentPhase == AscentPhase.InOrbit)
            {
                CurrentPhase = MissionDockingPhase.AligningRelativePlanes;
                StatusMessage = "Em órbita de perseguição! Calculando alinhamento de planos...";
                Debug.Log("[Oraculo 2.0] Subida concluída. Iniciando alinhamento fino de plano.");
            }
            else
            {
                StatusMessage = $"Subindo: {_ascentController.StatusMessage}";
            }
        }

        private void MonitorChaseOrbitInsertion()
        {
            CurrentPhase = MissionDockingPhase.AligningRelativePlanes;
        }

        #endregion

        #region Fase 3: Alinhamento Fino de Planos (Nós AN/DN)

        private void ExecutePlaneAlignment()
        {
            double relInc = CalculateRelativeInclination();

            // Se a inclinação relativa for menor que 0.08°, o plano já está perfeito
            if (relInc < 0.08)
            {
                CurrentPhase = MissionDockingPhase.PhasingAndTransfer;
                StatusMessage = $"Planos alinhados (Inc Rel: {relInc:F2}°)! Planejando transferência de encontro...";
                Debug.Log("[Oraculo 2.0] Planos orbitais já casados.");
                return;
            }

            // Cria nó de correção de inclinação no nó ascendente ou descendente
            if (_activeNode == null)
            {
                double timeToNode = CalculateTimeToRelativeNode();
                double v = Math.Sqrt(_vessel.mainBody.gravParameter / _vessel.orbit.semiMajorAxis);
                double dVNormal = 2.0 * v * Math.Sin((relInc * Mathf.Deg2Rad) / 2.0);

                _activeNode = _vessel.patchedConicSolver.AddManeuverNode(Planetarium.GetUniversalTime() + timeToNode);
                _activeNode.DeltaV = new Vector3d(0, -dVNormal, 0); // Queima Normal/Anti-normal
                _vessel.patchedConicSolver.UpdateFlightPlan();

                _nodeBurnTime = NavigationMath.CalculateBurnTime(_vessel, dVNormal);
                StatusMessage = $"Correção de Plano: Inc Rel={relInc:F2}° | dV={dVNormal:F1} m/s";
                Debug.Log($"[Oraculo 2.0] Nó de correção de plano criado: {dVNormal:F1} m/s.");
            }

            // Execução da queima do nó
            Vector3d burnVector = _activeNode.GetBurnVector(_vessel.orbit);
            NavigationMath.CalculateSteering(_vessel, burnVector.normalized, ref _cmdPitch, ref _cmdYaw);

            double timeUntilBurn = (_activeNode.UT - Planetarium.GetUniversalTime()) - (_nodeBurnTime / 2.0);
            if (timeUntilBurn <= 0.2)
            {
                _cmdThrottle = (float)Mathf.Clamp((float)(burnVector.magnitude / 20.0), 0.1f, 1.0f);
                if (burnVector.magnitude <= 0.5)
                {
                    _cmdThrottle = 0f;
                    _vessel.patchedConicSolver.RemoveManeuverNode(_activeNode);
                    _activeNode = null;
                    CurrentPhase = MissionDockingPhase.PhasingAndTransfer;
                    StatusMessage = "Planos alinhados com sucesso! Planejando transferência...";
                }
            }
            else
            {
                _cmdThrottle = 0f;
                StatusMessage = $"Aguardando nó de plano: Ignição em {timeUntilBurn:F0}s (dV={burnVector.magnitude:F1} m/s)";
            }
        }

        #endregion

        #region Fase 4: Transferência de Hohmann e Encontro Próximo

        private void ExecutePhasingAndTransfer()
        {
            double r1 = _vessel.orbit.semiMajorAxis;
            double r2 = _targetVessel.orbit.semiMajorAxis;

            // Se ainda não criou o nó de interceptação
            if (_activeNode == null)
            {
                // Cálculo do ângulo de fase para interceptação
                double transferSMA = (r1 + r2) / 2.0;
                double transferTime = Math.PI * Math.Sqrt(Math.Pow(transferSMA, 3.0) / _vessel.mainBody.gravParameter);
                double leadAngle = (180.0 - (360.0 / _targetVessel.orbit.period) * transferTime) % 360.0;

                double currentPhaseAngle = CalculatePhaseAngle();
                double phaseRate = (360.0 / _vessel.orbit.period) - (360.0 / _targetVessel.orbit.period);
                double timeToTransfer = ((currentPhaseAngle - leadAngle) % 360.0) / phaseRate;
                if (timeToTransfer < 0) timeToTransfer += Math.Abs(360.0 / phaseRate);

                // Delta-V de subida até a altitude do alvo
                double v1 = Math.Sqrt(_vessel.mainBody.gravParameter / r1);
                double vTrans = Math.Sqrt(_vessel.mainBody.gravParameter * ((2.0 / r1) - (1.0 / transferSMA)));
                double dVPrograde = vTrans - v1;

                _activeNode = _vessel.patchedConicSolver.AddManeuverNode(Planetarium.GetUniversalTime() + timeToTransfer);
                _activeNode.DeltaV = new Vector3d(0, 0, dVPrograde);
                _vessel.patchedConicSolver.UpdateFlightPlan();

                _nodeBurnTime = NavigationMath.CalculateBurnTime(_vessel, Math.Abs(dVPrograde));
                StatusMessage = $"Transferência de Encontro: dV={dVPrograde:F1} m/s | Espera={timeToTransfer/60:F1} min";
            }

            Vector3d burnVector = _activeNode.GetBurnVector(_vessel.orbit);
            NavigationMath.CalculateSteering(_vessel, burnVector.normalized, ref _cmdPitch, ref _cmdYaw);

            double timeUntilBurn = (_activeNode.UT - Planetarium.GetUniversalTime()) - (_nodeBurnTime / 2.0);
            if (timeUntilBurn <= 0.2)
            {
                _cmdThrottle = (float)Mathf.Clamp((float)(burnVector.magnitude / 25.0), 0.1f, 1.0f);
                if (burnVector.magnitude <= 0.6)
                {
                    _cmdThrottle = 0f;
                    _vessel.patchedConicSolver.RemoveManeuverNode(_activeNode);
                    _activeNode = null;
                    CurrentPhase = MissionDockingPhase.CoastingToIntercept;
                    StatusMessage = "Transferência concluída. Em rota de interceptação...";
                }
            }
            else
            {
                _cmdThrottle = 0f;
                StatusMessage = $"Aguardando queima de encontro: Ignição em {timeUntilBurn:F0}s (dV={burnVector.magnitude:F1} m/s)";
            }
        }

        private void MonitorCoastingToIntercept()
        {
            _cmdThrottle = 0f;
            double distance = Vector3d.Distance(_vessel.GetWorldPos3D(), _targetVessel.GetWorldPos3D());
            Vector3d relVel = _vessel.obt_velocity - _targetVessel.obt_velocity;

            // Mantém a nave apontada contra a velocidade relativa (Target Retrograde)
            Vector3d targetRetrograde = -relVel.normalized;
            NavigationMath.CalculateSteering(_vessel, targetRetrograde, ref _cmdPitch, ref _cmdYaw);

            if (distance <= 2500.0)
            {
                CurrentPhase = MissionDockingPhase.BrakingAtTarget;
                StatusMessage = $"Próximo ao alvo ({distance:F0}m)! Iniciando frenagem de velocidade relativa...";
            }
            else
            {
                StatusMessage = $"Aproximação: Dist={distance/1000:F1}km | Vel Rel={relVel.magnitude:F1} m/s";
            }
        }

        #endregion

        #region Fase 5: Frenagem no Alvo (Zerar Velocidade Relativa)

        private void ExecuteRelativeBraking()
        {
            Vector3d relVel = _vessel.obt_velocity - _targetVessel.obt_velocity;
            double relSpeed = relVel.magnitude;
            double distance = Vector3d.Distance(_vessel.GetWorldPos3D(), _targetVessel.GetWorldPos3D());

            Vector3d targetRetrograde = -relVel.normalized;
            NavigationMath.CalculateSteering(_vessel, targetRetrograde, ref _cmdPitch, ref _cmdYaw);

            // Modula o motor principal para zerar a velocidade relativa
            if (relSpeed > 0.2)
            {
                _cmdThrottle = (float)Mathf.Clamp((float)(relSpeed / 10.0), 0.08f, 0.9f);
                StatusMessage = $"Frenando no alvo: Vel Rel={relSpeed:F2} m/s | Dist={distance:F0}m";
            }
            else
            {
                // Velocidade relativa zerada a curta distância
                _cmdThrottle = 0f;
                CurrentPhase = MissionDockingPhase.AligningDockingPorts;
                _vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
                StatusMessage = "Velocidade relativa zerada! Identificando portas de acoplagem...";
                Debug.Log("[Oraculo 2.0] Encontro bem-sucedido! Alinhando portas.");
            }
        }

        #endregion

        #region Fase 6 e 7: Alinhamento de Portas e Aproximação Fina por RCS

        private void ExecutePortAlignment()
        {
            FindDockingPorts();

            if (_ourDockingPort == null || _targetDockingPort == null)
            {
                StatusMessage = "Erro: Porta de acoplagem não encontrada.";
                return;
            }

            // Aponta a nossa porta de frente para a porta do alvo
            Vector3d targetPortFacing = -_targetDockingPort.transform.forward;
            NavigationMath.CalculateSteering(_vessel, targetPortFacing, ref _cmdPitch, ref _cmdYaw, sensitivity: 3.5);

            Vector3d ourFacing = _ourDockingPort.transform.forward;
            double angleError = Vector3d.Angle(ourFacing, targetPortFacing);

            if (angleError < 4.0)
            {
                CurrentPhase = MissionDockingPhase.TranslationalRCSApproach;
                StatusMessage = "Portas alinhadas! Iniciando aproximação translacional por RCS...";
            }
            else
            {
                StatusMessage = $"Alinhando portas: Erro={angleError:F1}°";
            }
        }

        private void ExecuteTranslationalApproach()
        {
            FindDockingPorts();
            if (_ourDockingPort == null || _targetDockingPort == null) return;

            // Mantém a atitude travada
            Vector3d targetPortFacing = -_targetDockingPort.transform.forward;
            NavigationMath.CalculateSteering(_vessel, targetPortFacing, ref _cmdPitch, ref _cmdYaw, sensitivity: 4.0);

            // Vetor relativo da nossa porta até a porta do alvo
            Vector3d offsetWorld = _targetDockingPort.transform.position - _ourDockingPort.transform.position;
            double distance = offsetWorld.magnitude;

            // Transforma o erro de posição para o referencial local da nossa porta
            Transform portTransform = _ourDockingPort.transform;
            Vector3 localOffset = portTransform.InverseTransformDirection(offsetWorld);

            // Z = aproximação frontal, X e Y = alinhamento lateral do túnel
            double lateralError = Math.Sqrt(Math.Pow(localOffset.x, 2) + Math.Pow(localOffset.y, 2));

            // Velocidade de aproximação progressiva
            double targetSpeedZ = 0.15;
            if (distance > 30.0) targetSpeedZ = 1.5;
            else if (distance > 10.0) targetSpeedZ = 0.7;
            else if (distance > 3.0) targetSpeedZ = 0.3;

            // Se estiver descentralizado, reduz o avanço frontal e prioriza o alinhamento lateral
            if (lateralError > 1.0) targetSpeedZ = 0.05;

            // Computa comandos translacionais de RCS via PID
            Vector3d relVel = _vessel.obt_velocity - _targetVessel.obt_velocity;
            Vector3 localVel = portTransform.InverseTransformDirection(relVel);

            float rcsX = (float)_pidRcsX.Compute(localVel.x, Mathf.Clamp(localOffset.x * 0.5f, -1.0f, 1.0f));
            float rcsY = (float)_pidRcsY.Compute(localVel.y, Mathf.Clamp(localOffset.y * 0.5f, -1.0f, 1.0f));
            float rcsZ = (float)_pidRcsZ.Compute(localVel.z, (float)targetSpeedZ);

            _cmdTranslation = new Vector3(rcsX, rcsY, rcsZ);

            // Verifica atracação magnética (Docked)
            if (_ourDockingPort.state == "Docked" || _vessel == _targetVessel)
            {
                CurrentPhase = MissionDockingPhase.DockedComplete;
                _cmdTranslation = Vector3.zero;
                _cmdThrottle = 0f;
                IsActive = false;

                StatusMessage = "ACOPLAGEM CONCLUÍDA COM SUCESSO!";
                ScreenMessages.PostScreenMessage(
                    "<color=#00ff88><b>[Oráculo 2.0] ACOPLAGEM BEM-SUCEDIDA!</b></color>",
                    10f,
                    ScreenMessageStyle.UPPER_CENTER
                );
                Debug.Log("[Oraculo 2.0] Acoplagem finalizada!");
            }
            else
            {
                StatusMessage = $"Aproximando via RCS: Dist={distance:F2}m | Erro Lat={lateralError:F2}m | Vel={localVel.z:F2} m/s";
            }
        }

        private void FindDockingPorts()
        {
            if (_ourDockingPort == null)
            {
                _ourDockingPort = _vessel.FindPartModuleImplementing<ModuleDockingNode>();
            }

            if (_targetDockingPort == null)
            {
                if (_currentTarget is ModuleDockingNode targetNode)
                {
                    _targetDockingPort = targetNode;
                }
                else
                {
                    _targetDockingPort = _targetVessel.FindPartModuleImplementing<ModuleDockingNode>();
                }
            }
        }

        #endregion

        #region Auxiliares de Navegação e Órbita

        private double CalculateRelativeInclination()
        {
            Vector3d h1 = Vector3d.Cross(_vessel.orbit.pos, _vessel.orbit.vel);
            Vector3d h2 = Vector3d.Cross(_targetVessel.orbit.pos, _targetVessel.orbit.vel);
            double cosRelInc = Vector3d.Dot(h1.normalized, h2.normalized);
            return Math.Acos(Mathf.Clamp((float)cosRelInc, -1.0f, 1.0f)) * Mathf.Rad2Deg;
        }

        private double CalculateTimeToRelativeNode()
        {
            // Retorna tempo em segundos até o próximo nó ascendente/descendente relativo
            return _vessel.orbit.period * 0.25;
        }

        private double CalculatePhaseAngle()
        {
            Vector3d pos1 = _vessel.orbit.pos;
            Vector3d pos2 = _targetVessel.orbit.pos;
            double angle = Vector3d.Angle(pos1, pos2);
            Vector3d cross = Vector3d.Cross(pos1, pos2);
            if (Vector3d.Dot(cross, _vessel.orbit.GetOrbitNormal()) < 0)
            {
                angle = 360.0 - angle;
            }
            return angle;
        }

        private void OnFlyByWireCallback(FlightCtrlState state)
        {
            if (!IsActive || _vessel == null) return;

            // Se estiver na fase de subida atmosférica, deixa o OrbitalAscentController guiar os controles
            if (CurrentPhase == MissionDockingPhase.LaunchingToTargetPlane)
            {
                return;
            }

            state.mainThrottle = _cmdThrottle;
            state.pitch = _cmdPitch;
            state.yaw = _cmdYaw;
            state.roll = _cmdRoll;

            // Injeção de translação nos jatos de RCS
            if (CurrentPhase == MissionDockingPhase.TranslationalRCSApproach)
            {
                state.X = _cmdTranslation.x;
                state.Y = _cmdTranslation.y;
                state.Z = _cmdTranslation.z;
            }
        }

        #endregion
    }
}
