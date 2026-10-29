using System;
using UnityEngine;
using MuMech;

namespace OraculoGuidance.Modules.Landing
{
    public class RunwaySite
    {
        public string Name { get; }
        public double Latitude { get; }
        public double Longitude { get; }
        public double AltitudeASL { get; }
        public float RunwayHeading { get; }

        public RunwaySite(string name, double lat, double lon, double alt, float heading)
        {
            Name = name;
            Latitude = lat;
            Longitude = lon;
            AltitudeASL = alt;
            RunwayHeading = heading;
        }
    }

    public enum RunwayLandingPhase
    {
        Idle,
        AligningGlideSlope,
        ShortFinal,
        Flare,
        RolloutAndBrake,
        Stopped
    }

    /// <summary>
    /// Piloto automático de pouso na pista para aviões e Spaceplanes / SSTOs.
    /// Suporta a Pista Principal do KSC e o Aeródromo da Ilha Antiga.
    /// Cuida do alinhamento, rampa de planeio, trem de pouso, freios e paraquedas de arrasto.
    /// </summary>
    public class RunwayLandingController
    {
        public bool IsActive { get; private set; }
        public RunwayLandingPhase CurrentPhase { get; private set; } = RunwayLandingPhase.Idle;
        public string StatusMessage { get; private set; } = "Pronto";

        public static readonly RunwaySite KSCRunway = new RunwaySite("KSC Runway", -0.0486, -74.7245, 70.0, 90f);
        public static readonly RunwaySite IslandAirfield = new RunwaySite("Pista da Ilha Antiga", -1.5178, -71.9655, 133.0, 90f);

        public RunwaySite SelectedRunway { get; set; } = KSCRunway;

        private Vessel _vessel;
        private MechJebCore _core;

        private bool _chutesDeployed = false;
        private bool _gearDeployed = false;

        public void Initialize(Vessel vessel, MechJebCore core)
        {
            _vessel = vessel;
            _core = core;
        }

        public void StartLanding(RunwaySite runway)
        {
            if (_vessel == null)
            {
                StatusMessage = "Erro: Nenhuma aeronave ativa.";
                return;
            }

            SelectedRunway = runway ?? KSCRunway;
            IsActive = true;
            CurrentPhase = RunwayLandingPhase.AligningGlideSlope;
            _chutesDeployed = false;
            _gearDeployed = false;

            StatusMessage = $"Aproximação iniciada para {SelectedRunway.Name}. Alinhando com a pista...";
            Debug.Log($"[Oraculo] Pouso de avião iniciado para {SelectedRunway.Name}");
        }

        public void Abort()
        {
            IsActive = false;
            CurrentPhase = RunwayLandingPhase.Idle;
            StatusMessage = "Pouso na pista cancelado.";
            FlightInputHandler.state.mainThrottle = 0f;
            FlightInputHandler.state.brakes = false;
        }

        public void FixedUpdate()
        {
            if (!IsActive || _vessel == null) return;

            double radarAlt = _vessel.radarAltitude;
            double srfSpeed = _vessel.srfSpeed;

            switch (CurrentPhase)
            {
                case RunwayLandingPhase.AligningGlideSlope:
                    StatusMessage = $"Rampa de planeio: Vel={srfSpeed:F0} m/s | Alt={radarAlt:F0} m";

                    // Mantém proa da pista e descida suave de -3.5 graus
                    SetAttitudeHeadingAndPitch(SelectedRunway.RunwayHeading, -3.5f);

                    // Controle de velocidade de aproximação ideal (~90 m/s)
                    if (srfSpeed > 105.0)
                    {
                        FlightInputHandler.state.mainThrottle = 0f;
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true); // abre airbrakes
                    }
                    else if (srfSpeed < 80.0)
                    {
                        FlightInputHandler.state.mainThrottle = 0.4f;
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
                    }
                    else
                    {
                        FlightInputHandler.state.mainThrottle = 0.15f;
                    }

                    // A menos de 60m do solo, transiciona para final curta
                    if (radarAlt <= 60.0)
                    {
                        CurrentPhase = RunwayLandingPhase.ShortFinal;
                        StatusMessage = "Final curta: Baixando trem de pouso e armando freios de ar...";
                    }
                    break;

                case RunwayLandingPhase.ShortFinal:
                    // Baixa trem de pouso
                    if (!_gearDeployed)
                    {
                        _gearDeployed = true;
                        _vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
                    }

                    // Abre freios aerodinâmicos
                    _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                    SetAttitudeHeadingAndPitch(SelectedRunway.RunwayHeading, -2.0f);

                    // A menos de 12m do solo, inicia o flare (arredondamento)
                    if (radarAlt <= 12.0)
                    {
                        CurrentPhase = RunwayLandingPhase.Flare;
                        StatusMessage = "Arredondamento (Flare): Puxando nariz para toque suave nas rodas traseiras...";
                    }
                    break;

                case RunwayLandingPhase.Flare:
                    // Corta motor totalmente
                    FlightInputHandler.state.mainThrottle = 0f;

                    // Puxa o nariz suavemente para cima (+4° de pitch) para quebrar a velocidade vertical
                    SetAttitudeHeadingAndPitch(SelectedRunway.RunwayHeading, 4.0f);

                    // Detecta toque no solo
                    if (_vessel.LandedOrSplashed)
                    {
                        CurrentPhase = RunwayLandingPhase.RolloutAndBrake;
                        StatusMessage = "Toque no solo confirmado! Frenagem máxima e paraquedas acionados.";
                        Debug.Log("[Oraculo] Avião tocou na pista!");
                    }
                    break;

                case RunwayLandingPhase.RolloutAndBrake:
                    FlightInputHandler.state.mainThrottle = 0f;

                    // 1. Aciona os freios de roda
                    FlightInputHandler.state.brakes = true;
                    _vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);

                    // 2. Aciona paraquedas de frenagem (drag chutes)
                    if (!_chutesDeployed)
                    {
                        DeployDragChutes();
                        _chutesDeployed = true;
                    }

                    // 3. Controle de leme no solo para manter no meio da pista
                    KeepRunwayCenterline();

                    // Se a velocidade cair abaixo de 0.8 m/s, aeronave está parada
                    if (srfSpeed <= 0.8)
                    {
                        CurrentPhase = RunwayLandingPhase.Stopped;
                        IsActive = false;
                        StatusMessage = $"Pouso concluído com sucesso em {SelectedRunway.Name}!";
                        ScreenMessages.PostScreenMessage(
                            $"<color=#00ff88>[Oráculo] {SelectedRunway.Name}: Aeronave pousada e imobilizada na pista!</color>",
                            8f,
                            ScreenMessageStyle.UPPER_CENTER
                        );
                        Debug.Log("[Oraculo] Pouso na pista finalizado.");
                    }
                    break;
            }
        }

        private void DeployDragChutes()
        {
            // Aciona todos os módulos de paraquedas instalados na nave
            var parachutes = _vessel.FindPartModulesImplementing<ModuleParachute>();
            foreach (var chute in parachutes)
            {
                try
                {
                    chute.Deploy();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Oraculo] Falha ao acionar paraquedas: {ex.Message}");
                }
            }

            // Também aciona o Action Group Custom02 (comumente usado para drogue chutes em mods)
            _vessel.ActionGroups.SetGroup(KSPActionGroup.Custom02, true);
        }

        private void SetAttitudeHeadingAndPitch(float heading, float pitch)
        {
            if (_core != null)
            {
                var attitude = _core.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeTo(new AttitudeTarget(AttitudeReference.SURFACE_NORTH, Quaternion.Euler(-pitch, heading, 0f)));
                }
            }
        }

        private void KeepRunwayCenterline()
        {
            // Mantém proa reta na pista durante a rolagem
            SetAttitudeHeadingAndPitch(SelectedRunway.RunwayHeading, 0f);
        }
    }
}
