using System;
using UnityEngine;
using OraculoGuidance.Core;

namespace OraculoGuidance.Modules.Landing
{
    public enum LandingPhase
    {
        Idle,
        WaitingForBurnDistance,
        ExecutingSuicideBurn,
        SoftTouchdownHover,
        Landed
    }

    /// <summary>
    /// Controlador autônomo de pouso propulsivo de precisão (Suicide Burn / Hoverslam).
    /// Migrado e aprimorado de SuicideBurn.py e Navegacao.py do MechPeste.
    /// Utiliza empuxo real disponível, cálculo quadrático de distância de parada e controle PID duplo.
    /// </summary>
    public class SuicideBurnController
    {
        public bool IsActive { get; private set; }
        public LandingPhase CurrentPhase { get; private set; } = LandingPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        public double HoverMarginAltitude = 50.0; // Altura para transição para toque suave
        public double TargetTouchdownSpeed = -1.0; // 1 m/s para baixo no toque

        private Vessel _vessel;
        private PIDController _accelPid;
        private PIDController _landingPid;

        private double _commandedThrottle = 0.0;
        private float _commandedPitch = 0.0f;
        private float _commandedYaw = 0.0f;
        private bool _gearDeployed = false;

        public SuicideBurnController()
        {
            // PID 1: Controle de desaceleração agressiva (Altitude vs Distância de parada)
            _accelPid = new PIDController(kp: 0.05, ki: 0.001, kd: 0.05, min: 0.0, max: 1.0, sampleTime: 0.02);

            // PID 2: Controle de velocidade vertical para toque macio nos últimos metros
            _landingPid = new PIDController(kp: 0.2, ki: 0.005, kd: 0.15, min: 0.0, max: 1.0, sampleTime: 0.02);
            _landingPid.Setpoint = TargetTouchdownSpeed;
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

        public void StartSuicideBurn()
        {
            if (_vessel == null)
            {
                StatusMessage = "Erro: Nave não disponível.";
                return;
            }

            _gearDeployed = false;
            _accelPid.Reset();
            _landingPid.Reset();

            IsActive = true;
            CurrentPhase = LandingPhase.WaitingForBurnDistance;
            StatusMessage = "Calculando trajetória de queda e ponto de ignição...";

            Debug.Log($"[Oraculo 2.0] Suicide Burn ativado para {_vessel.vesselName} em {_vessel.mainBody.bodyName}.");
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = LandingPhase.Idle;
            _commandedThrottle = 0.0;
            _commandedPitch = 0.0f;
            _commandedYaw = 0.0f;

            if (_vessel != null)
            {
                _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }

            StatusMessage = "Pouso propulsivo abortado.";
        }

        public void Update()
        {
            if (!IsActive || _vessel == null) return;

            // 1. Verificação de Pouso Concluído
            if (_vessel.LandedOrSplashed && CurrentPhase != LandingPhase.WaitingForBurnDistance)
            {
                FinishLanding();
                return;
            }

            double radarAlt = _vessel.radarAltitude;
            double vertSpeed = _vessel.verticalSpeed;
            double localGravity = _vessel.mainBody.GeeASL * NavigationMath.StandardGravity;
            double maxTwr = NavigationMath.GetMaxTWR(_vessel);

            // Aceleração máxima resultante de frenagem
            double netMaxAccel = Math.Max(1.0, (maxTwr * localGravity) - localGravity);
            double burnTime = Math.Abs(vertSpeed) / netMaxAccel;
            double stoppingDistance = (Math.Abs(vertSpeed) * burnTime) + (0.5 * netMaxAccel * Math.Pow(burnTime, 2.0));

            // 2. Orientação para o Retrógrado de Superfície (anula deriva horizontal)
            Vector3d retrogradeDir = NavigationMath.GetSurfaceRetrograde(_vessel);
            NavigationMath.CalculateSteering(_vessel, retrogradeDir, ref _commandedPitch, ref _commandedYaw, sensitivity: 3.0);

            switch (CurrentPhase)
            {
                case LandingPhase.WaitingForBurnDistance:
                    // Ativa freios aerodinâmicos e RCS durante a queda livre
                    if (radarAlt > 100.0 && radarAlt < 15000.0)
                    {
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                    }
                    if (radarAlt < 5000.0)
                    {
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
                    }

                    _commandedThrottle = 0.0;
                    double distanceMargin = radarAlt - stoppingDistance - HoverMarginAltitude;

                    // Condição de ignição: margem zerada caindo a mais de 1 m/s
                    if (distanceMargin <= 0.0 && vertSpeed < -1.0)
                    {
                        CurrentPhase = LandingPhase.ExecutingSuicideBurn;
                        _commandedThrottle = 1.0;
                        Debug.Log($"[Oraculo 2.0] Ignição do Suicide Burn iniciada! Alt={radarAlt:F0}m | DistParada={stoppingDistance:F0}m.");
                    }
                    else
                    {
                        StatusMessage = $"Em queda livre: Alt={radarAlt:F0}m | Ignição em {Math.Max(0.0, distanceMargin):F0}m | TWR={maxTwr:F1}";
                    }
                    break;

                case LandingPhase.ExecutingSuicideBurn:
                    // Abre o trem de pouso abaixo de 120m
                    if (radarAlt <= 120.0 && !_gearDeployed)
                    {
                        _gearDeployed = true;
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
                        Debug.Log("[Oraculo 2.0] Trem de pouso baixado automaticamente!");
                    }

                    // Se estiver abaixo da altitude de transição de hover, passa para o toque suave
                    if (radarAlt <= HoverMarginAltitude)
                    {
                        CurrentPhase = LandingPhase.SoftTouchdownHover;
                        StatusMessage = "Toque suave: Estabilizando descida a 1 m/s...";
                    }
                    else
                    {
                        // Modula o acelerador para casar altitude atual com a distância de frenagem
                        _accelPid.Setpoint = stoppingDistance;
                        double throttleCmd = _accelPid.Compute(radarAlt);
                        // Compensa a gravidade local
                        double gravityOffset = maxTwr > 0 ? (1.0 / maxTwr) : 0.5;
                        _commandedThrottle = Mathf.Clamp((float)(throttleCmd + gravityOffset), 0.2f, 1.0f);

                        StatusMessage = $"Frenagem Hoverslam: Alt={radarAlt:F0}m | Vel={vertSpeed:F1} m/s | Acel={(int)(_commandedThrottle * 100)}%";
                    }
                    break;

                case LandingPhase.SoftTouchdownHover:
                    // Abre trem de pouso se ainda não estiver aberto
                    if (!_gearDeployed)
                    {
                        _gearDeployed = true;
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
                    }

                    // PID regula a velocidade vertical até a meta suave (-1.0 m/s)
                    double descentThrottle = _landingPid.Compute(vertSpeed);
                    double hoverThrottle = maxTwr > 0 ? (1.0 / maxTwr) : 0.5;
                    _commandedThrottle = Mathf.Clamp((float)(hoverThrottle + descentThrottle), 0.05f, 1.0f);

                    StatusMessage = $"Toque final suave: Alt={radarAlt:F1}m | Vel={vertSpeed:F1} m/s | Alvo={TargetTouchdownSpeed:F1} m/s";
                    break;

                case LandingPhase.Landed:
                    // Pousado
                    break;
            }
        }

        private void FinishLanding()
        {
            _commandedThrottle = 0.0;
            _commandedPitch = 0.0f;
            _commandedYaw = 0.0f;
            CurrentPhase = LandingPhase.Landed;
            IsActive = false;

            _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

            StatusMessage = "Pouso realizado com sucesso!";
            ScreenMessages.PostScreenMessage(
                $"<color=#00ff88>[Oráculo 2.0] {_vessel.vesselName} pousou suavemente na superfície!</color>",
                8f,
                ScreenMessageStyle.UPPER_CENTER
            );
            Debug.Log($"[Oraculo 2.0] Pouso propulsivo finalizado com sucesso.");
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
