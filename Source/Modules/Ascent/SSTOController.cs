using System;
using UnityEngine;
using MuMech;

namespace OraculoGuidance.Modules.Ascent
{
    public enum SSTOPhase
    {
        Idle,
        TakeoffRoll,
        InitialClimb,
        SpeedRun,
        ClosedCycleClimb,
        CoastingToApoapsis,
        Circularizing,
        InOrbit
    }

    /// <summary>
    /// Piloto automático dedicado para aviões espaciais (SSTO) da pista até a órbita.
    /// </summary>
    public class SSTOController
    {
        public bool IsActive { get; private set; }
        public SSTOPhase CurrentPhase { get; private set; } = SSTOPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        public double TargetOrbitAltitude = 85000; // 85 km padrão
        public float PitchTakeoff = 15f;
        public float PitchClimb = 18f;
        public float PitchSpeedRun = 6f;
        public float PitchRocketClimb = 20f;

        private Vessel _vessel;
        private MechJebCore _core;

        public void Initialize(Vessel vessel, MechJebCore core)
        {
            _vessel = vessel;
            _core = core;
        }

        public void StartSSTOLaunch()
        {
            if (_vessel == null) return;

            IsActive = true;
            CurrentPhase = SSTOPhase.TakeoffRoll;
            FlightInputHandler.state.mainThrottle = 1.0f;
            _vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

            StatusMessage = "Corrida de decolagem na pista (100% de empuxo)...";
            Debug.Log("[Oraculo] Decolagem SSTO iniciada!");
        }

        public void Stop()
        {
            IsActive = false;
            CurrentPhase = SSTOPhase.Idle;
            StatusMessage = "SSTO Desativado";
            FlightInputHandler.state.mainThrottle = 0f;
        }

        public void FixedUpdate()
        {
            if (!IsActive || _vessel == null) return;

            double altitude = _vessel.altitude;
            double surfaceSpeed = _vessel.srfSpeed;
            double apoapsis = _vessel.orbit.ApA;

            switch (CurrentPhase)
            {
                case SSTOPhase.TakeoffRoll:
                    // Rotação quando passar de 100 m/s
                    if (surfaceSpeed > 100 && !_vessel.LandedOrSplashed)
                    {
                        CurrentPhase = SSTOPhase.InitialClimb;
                        StatusMessage = "Subida inicial em direção a 10km...";
                    }
                    else if (surfaceSpeed > 95)
                    {
                        // Puxa o manche suavemente
                        FlightInputHandler.state.pitch = -0.5f;
                    }
                    break;

                case SSTOPhase.InitialClimb:
                    // Sobe em ângulo constante até a camada de aceleração (10km)
                    SetAttitudePitch(PitchClimb);

                    if (altitude >= 10000)
                    {
                        CurrentPhase = SSTOPhase.SpeedRun;
                        StatusMessage = "Speed Run: Nivelando para quebrar a barreira de som (1400+ m/s)...";
                    }
                    break;

                case SSTOPhase.SpeedRun:
                    // Nivela o nariz para acumular velocidade horizontal em regime aeróbio
                    SetAttitudePitch(PitchSpeedRun);

                    // Quando a velocidade passar de 1350 m/s ou a altitude passar de 18km
                    if (surfaceSpeed >= 1350 || altitude >= 18000)
                    {
                        CurrentPhase = SSTOPhase.ClosedCycleClimb;
                        StatusMessage = "Transição para Foguete (Ciclo Fechado). Empurrando até apoastro...";

                        // Ativa grupo de ação 1 (comum em KSP para alternar motores RAPIER para foguete)
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Custom01, true);
                    }
                    break;

                case SSTOPhase.ClosedCycleClimb:
                    SetAttitudePitch(PitchRocketClimb);

                    if (apoapsis >= TargetOrbitAltitude)
                    {
                        // Corta motor
                        FlightInputHandler.state.mainThrottle = 0f;
                        CurrentPhase = SSTOPhase.CoastingToApoapsis;
                        StatusMessage = $"Apoastro atingido ({apoapsis / 1000:F1} km). Em inércia para circularização.";
                    }
                    break;

                case SSTOPhase.CoastingToApoapsis:
                    // Aponta para o prógrado orbital
                    OrientToPrograde();

                    // Se estiver a menos de 30 segundos do apoastro
                    if (_vessel.orbit.timeToAp <= 30 && _vessel.orbit.timeToAp > 0)
                    {
                        CurrentPhase = SSTOPhase.Circularizing;
                        StatusMessage = "Circularizando órbita...";
                    }
                    break;

                case SSTOPhase.Circularizing:
                    OrientToPrograde();

                    // Queima até que o periastro saia da atmosfera (75km+)
                    if (_vessel.orbit.PeA < TargetOrbitAltitude * 0.95)
                    {
                        FlightInputHandler.state.mainThrottle = 1.0f;
                    }
                    else
                    {
                        FlightInputHandler.state.mainThrottle = 0f;
                        CurrentPhase = SSTOPhase.InOrbit;
                        StatusMessage = $"Órbita estável alcançada! ({_vessel.orbit.PeA/1000:F1}km x {_vessel.orbit.ApA/1000:F1}km)";
                        IsActive = false;
                    }
                    break;
            }
        }

        private void SetAttitudePitch(float pitchDegrees)
        {
            if (_core != null && _core.attitudeTo != null)
            {
                // Usa o AttitudeController do MechJeb para manter atitude perfeita
                var attitude = _core.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    // Orientação de superfície com pitch desejado e heading 90 (leste)
                    attitude.attitudeTo(new AttitudeTarget(AttitudeReference.SURFACE_NORTH, Quaternion.Euler(-pitchDegrees, 90f, 0f)));
                }
            }
        }

        private void OrientToPrograde()
        {
            if (_core != null)
            {
                var attitude = _core.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeTo(new AttitudeTarget(AttitudeReference.ORBIT, Vector3d.forward));
                }
            }
        }
    }
}
