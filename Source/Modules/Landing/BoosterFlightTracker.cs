using System;
using UnityEngine;
using OraculoGuidance.Core;

namespace OraculoGuidance.Modules.Landing
{
    public enum BoosterPhase
    {
        Separating,
        Flip,
        Boostback,
        Coasting,
        EntryBurn,
        FinalApproach,
        Landed,
        RecoveredInBackground
    }

    /// <summary>
    /// Piloto automático individual para recuperação autônoma de um booster (Falcon 9 / Heavy RTLS).
    /// Funciona mesmo que o booster NÃO seja a nave ativa da câmera,
    /// através do canal de controle OnFlyByWire do Unity/KSP.
    /// Utiliza NavigationMath para alinhamento vetorial exato e cálculo de empuxo real.
    /// </summary>
    public class BoosterFlightTracker
    {
        public Vessel BoosterVessel { get; private set; }
        public BoosterPhase CurrentPhase { get; private set; } = BoosterPhase.Separating;
        public string PadName { get; private set; }
        public double TargetLat { get; private set; }
        public double TargetLon { get; private set; }

        public bool IsFinished => CurrentPhase == BoosterPhase.Landed || CurrentPhase == BoosterPhase.RecoveredInBackground;

        private double _separationTime;
        private float _cmdThrottle = 0f;
        private float _cmdPitch = 0f;
        private float _cmdYaw = 0f;
        private bool _gearDeployed = false;

        public BoosterFlightTracker(Vessel vessel, string padName, double targetLat, double targetLon)
        {
            BoosterVessel = vessel;
            PadName = padName;
            TargetLat = targetLat;
            TargetLon = targetLon;
            _separationTime = Planetarium.GetUniversalTime();

            // Inscreve este booster para receber controle físico direto em segundo plano
            BoosterVessel.OnFlyByWire += OnFlyByWireCallback;

            Debug.Log($"[Oraculo 2.0] Booster '{vessel.vesselName}' assumido! Alvo: {padName} ({targetLat:F4}, {targetLon:F4})");
        }

        public void Unhook()
        {
            if (BoosterVessel != null)
            {
                BoosterVessel.OnFlyByWire -= OnFlyByWireCallback;
            }
        }

        private void OnFlyByWireCallback(FlightCtrlState state)
        {
            if (IsFinished || BoosterVessel == null) return;

            state.mainThrottle = _cmdThrottle;
            state.pitch = _cmdPitch;
            state.yaw = _cmdYaw;
        }

        public void Update()
        {
            if (IsFinished || BoosterVessel == null) return;

            double currentTime = Planetarium.GetUniversalTime();
            double altitude = BoosterVessel.altitude;
            double radarAlt = BoosterVessel.radarAltitude;
            double srfSpeed = BoosterVessel.srfSpeed;

            // 1. Verificação de Pouso Concluído
            if (BoosterVessel.LandedOrSplashed && CurrentPhase != BoosterPhase.Separating)
            {
                _cmdThrottle = 0f;
                _cmdPitch = 0f;
                _cmdYaw = 0f;
                BoosterVessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                BoosterVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                CurrentPhase = BoosterPhase.Landed;
                Unhook();

                ScreenMessages.PostScreenMessage(
                    $"<color=#00ff88><b>[Oráculo 2.0] {BoosterVessel.vesselName} pousou com sucesso em {PadName}!</b></color>",
                    8f,
                    ScreenMessageStyle.UPPER_CENTER
                );
                Debug.Log($"[Oraculo 2.0] {BoosterVessel.vesselName} tocou o chão em {PadName}!");
                return;
            }

            // 2. Proteção de Distância Excessiva (se afastar mais de 220km da nave ativa sem foco)
            if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel != BoosterVessel)
            {
                double dist = Vector3d.Distance(BoosterVessel.GetWorldPos3D(), FlightGlobals.ActiveVessel.GetWorldPos3D());
                if (dist > 220000.0)
                {
                    PerformBackgroundRecovery();
                    return;
                }
            }

            // 3. Máquina de Estados de Voo do Booster
            switch (CurrentPhase)
            {
                case BoosterPhase.Separating:
                    _cmdThrottle = 0f;
                    // Aguarda 2.5 segundos para limpar a distância do 2º estágio com segurança
                    if (currentTime - _separationTime > 2.5)
                    {
                        CurrentPhase = BoosterPhase.Flip;
                        Debug.Log($"[Oraculo 2.0] {BoosterVessel.vesselName}: Iniciando manobra de Flip.");
                    }
                    break;

                case BoosterPhase.Flip:
                    _cmdThrottle = 0f;
                    // Calcula o vetor de retorno em direção ao KSC / Base de pouso
                    Vector3d padWorldPos = BoosterVessel.mainBody.GetWorldSurfacePosition(TargetLat, TargetLon, 0);
                    Vector3d returnDir = (padWorldPos - BoosterVessel.GetWorldPos3D()).normalized;
                    NavigationMath.CalculateSteering(BoosterVessel, returnDir, ref _cmdPitch, ref _cmdYaw, sensitivity: 3.0);

                    // Após o alinhamento com a base (6 segundos)
                    if (currentTime - _separationTime > 7.5)
                    {
                        CurrentPhase = BoosterPhase.Boostback;
                        _cmdThrottle = 1.0f;
                        Debug.Log($"[Oraculo 2.0] {BoosterVessel.vesselName}: Boostback Burn ativado para {PadName}!");
                    }
                    break;

                case BoosterPhase.Boostback:
                    Vector3d padTargetPos = BoosterVessel.mainBody.GetWorldSurfacePosition(TargetLat, TargetLon, 0);
                    Vector3d boostbackDir = (padTargetPos - BoosterVessel.GetWorldPos3D()).normalized;
                    NavigationMath.CalculateSteering(BoosterVessel, boostbackDir, ref _cmdPitch, ref _cmdYaw);

                    // Queima até reverter a velocidade horizontal ou zerar afastamento
                    if (srfSpeed < 180.0 || BoosterVessel.verticalSpeed < -50.0)
                    {
                        _cmdThrottle = 0f;
                        CurrentPhase = BoosterPhase.Coasting;
                        // Aciona os Grid Fins (Airbrakes)
                        BoosterVessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                        BoosterVessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
                        Debug.Log($"[Oraculo 2.0] {BoosterVessel.vesselName}: Boostback concluído. Aletas aerodinâmicas abertas.");
                    }
                    break;

                case BoosterPhase.Coasting:
                    _cmdThrottle = 0f;
                    // Mantém rigorosamente o vetor Retrógrado aerodinâmico
                    Vector3d retrograde = NavigationMath.GetSurfaceRetrograde(BoosterVessel);
                    NavigationMath.CalculateSteering(BoosterVessel, retrograde, ref _cmdPitch, ref _cmdYaw, sensitivity: 3.5);

                    // Queima de reentrada (Entry Burn) entre 45km e 25km para evitar destruição térmica
                    if (altitude <= 45000.0 && srfSpeed > 1000.0)
                    {
                        CurrentPhase = BoosterPhase.EntryBurn;
                        _cmdThrottle = 0.85f;
                    }
                    else if (altitude <= 25000.0)
                    {
                        CurrentPhase = BoosterPhase.FinalApproach;
                    }
                    break;

                case BoosterPhase.EntryBurn:
                    Vector3d entryRetro = NavigationMath.GetSurfaceRetrograde(BoosterVessel);
                    NavigationMath.CalculateSteering(BoosterVessel, entryRetro, ref _cmdPitch, ref _cmdYaw);
                    _cmdThrottle = 0.85f;

                    if (srfSpeed <= 550.0 || altitude <= 22000.0)
                    {
                        _cmdThrottle = 0f;
                        CurrentPhase = BoosterPhase.FinalApproach;
                        Debug.Log($"[Oraculo 2.0] {BoosterVessel.vesselName}: Entrando na aproximação final de pouso.");
                    }
                    break;

                case BoosterPhase.FinalApproach:
                    Vector3d landingRetro = NavigationMath.GetSurfaceRetrograde(BoosterVessel);
                    NavigationMath.CalculateSteering(BoosterVessel, landingRetro, ref _cmdPitch, ref _cmdYaw, sensitivity: 4.0);

                    // A 120m do solo, estende as pernas de pouso (Landing Gear)
                    if (radarAlt <= 120.0 && !_gearDeployed)
                    {
                        _gearDeployed = true;
                        BoosterVessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
                        Debug.Log($"[Oraculo 2.0] {BoosterVessel.vesselName}: Trem de pouso baixado!");
                    }

                    // Queima suicida final (Hoverslam) com empuxo real disponível
                    double g = BoosterVessel.mainBody.GeeASL * NavigationMath.StandardGravity;
                    double thrust = NavigationMath.GetAvailableThrust(BoosterVessel);
                    double mass = BoosterVessel.GetTotalMass();
                    double maxAccel = (mass > 0 && thrust > 0) ? (thrust / mass) : (g + 15.0);
                    double netAccel = Math.Max(2.0, maxAccel - g);

                    double burnTime = Math.Abs(BoosterVessel.verticalSpeed) / netAccel;
                    double stoppingDistance = (Math.Abs(BoosterVessel.verticalSpeed) * burnTime) + (0.5 * netAccel * Math.Pow(burnTime, 2.0));

                    if (radarAlt <= stoppingDistance + 35.0)
                    {
                        // Modula empuxo para toque macio (1.2 m/s)
                        float throttleTarget = (float)Mathf.Clamp((float)(srfSpeed / 12.0), 0.25f, 1.0f);
                        _cmdThrottle = throttleTarget;
                    }
                    else
                    {
                        _cmdThrottle = 0f;
                    }
                    break;
            }
        }

        private void PerformBackgroundRecovery()
        {
            CurrentPhase = BoosterPhase.RecoveredInBackground;
            _cmdThrottle = 0f;
            Unhook();

            ScreenMessages.PostScreenMessage(
                $"<color=#00ff88>[Oráculo 2.0] {BoosterVessel.vesselName} recuperado com sucesso em {PadName} (Simulação em Background)!</color>",
                8f,
                ScreenMessageStyle.UPPER_CENTER
            );

            Debug.Log($"[Oraculo 2.0] {BoosterVessel.vesselName} salvo por simulação em background além de 220km.");

            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                double funds = ShipConstruction.CalculateVesselCost(BoosterVessel.protoVessel);
                Funding.Instance.AddFunds(funds, TransactionReasons.VesselRecovery);
            }

            BoosterVessel.Die();
        }
    }
}
