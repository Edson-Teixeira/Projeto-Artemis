using System;
using UnityEngine;
using OraculoGuidance.Core;
using OraculoGuidance.Modules.Ascent;
using OraculoGuidance.Modules.Landing;
using OraculoGuidance.Modules.Rendezvous;
using OraculoGuidance.Modules.Contracts;
using OraculoGuidance.Modules.Tourism;
using OraculoGuidance.Modules.Missions;

namespace OraculoGuidance.UI
{
    public class MainWindow
    {
        public bool IsVisible = true;
        private Rect _windowRect = new Rect(100, 100, 490, 590);
        private const int WINDOW_ID = 884219;

        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "Telemetria", "Lançamento", "Pouso", "Docking 2.0", "Missões" };

        // Sub-opções de Missões
        private int _selectedMissionSubTab = 0;
        private readonly string[] _missionSubTabs = { "Satélites de Contrato", "Voos Turísticos", "Missões Lunares" };

        // Sub-opções de Lançamento
        private int _launchModeIndex = 0;
        private readonly string[] _launchModes = { "Oráculo 2.0 (Nativo)", "MechJeb (Legado)", "SSTO (Pista)" };
        private string _targetOrbitKmStr = "85";
        private int _selectedProfileIndex = 0;
        private readonly string[] _profileNames = { "Normal", "Rasa", "Aguda" };

        private string _targetIncStr = "0.0";
        private bool _enableFalconMECO = false;
        private string _boosterReserveDvStr = "1200";

        // Sub-opções de Pouso
        private int _landingCategoryIndex = 0;
        private readonly string[] _landingCategories = { "Suicide Burn 2.0", "Foguete KSC", "Avião Pista", "Lander" };
        private int _selectedRunwayIndex = 0;
        private int _kscSiteIndex = 0;

        // Sub-opções de Satélite / Contratos
        private bool _manualSatelliteEntry = false;
        private string _manualApKm = "500";
        private string _manualPeKm = "500";
        private string _manualInc = "0.0";
        private string _manualArgPe = "0.0";
        private Vector2 _contractScroll = Vector2.zero;

        // Sub-opções de Turismo
        private int _tourismModeIndex = 0;
        private readonly string[] _tourismModes = { "Salto Suborbital (75km)", "Voo Orbital (85km)" };
        private int _recoveryMethodIndex = 0;
        private readonly string[] _recoveryMethods = { "Paraquedas Automáticos", "Hoverslam Propulsivo" };

        // Sub-opções Lunares
        private int _selectedMoonIndex = 0;
        private readonly string[] _moonNames = { "Mun (25km)", "Minmus (15km)" };

        // Referências aos controladores do Oráculo 2.0
        private readonly OrbitalAscentController _nativeAscent;
        private readonly SuicideBurnController _suicideBurn;
        private readonly FullMissionDockingController _fullDocking;
        private readonly TourismFlightController _tourism;
        private readonly SatelliteGuidanceController _satelliteGuidance;
        private readonly LunarTransferController _lunarTransfer;
        private readonly DeployablePartsManager _deployables;
        private readonly BatterySafetyGuard _batteryGuard;

        // Referências aos controladores legados
        private readonly AscentController _ascent;
        private readonly SSTOController _ssto;
        private readonly RunwayLandingController _runwayLanding;
        private readonly KSCOrbitLandingController _kscOrbitLanding;
        private readonly MultiBoosterManager _multiBooster;
        private readonly PrecisionLanding _verticalLander;
        private readonly AutoDockingController _docking;
        private readonly SatelliteContractController _satellite;

        public MainWindow(
            OrbitalAscentController nativeAscent,
            SuicideBurnController suicideBurn,
            FullMissionDockingController fullDocking,
            TourismFlightController tourism,
            SatelliteGuidanceController satelliteGuidance,
            LunarTransferController lunarTransfer,
            DeployablePartsManager deployables,
            BatterySafetyGuard batteryGuard,
            AscentController ascent,
            SSTOController ssto,
            RunwayLandingController runwayLanding,
            KSCOrbitLandingController kscOrbitLanding,
            MultiBoosterManager multiBooster,
            PrecisionLanding verticalLander,
            AutoDockingController docking,
            SatelliteContractController satellite)
        {
            _nativeAscent = nativeAscent;
            _suicideBurn = suicideBurn;
            _fullDocking = fullDocking;
            _tourism = tourism;
            _satelliteGuidance = satelliteGuidance;
            _lunarTransfer = lunarTransfer;
            _deployables = deployables;
            _batteryGuard = batteryGuard;

            _ascent = ascent;
            _ssto = ssto;
            _runwayLanding = runwayLanding;
            _kscOrbitLanding = kscOrbitLanding;
            _multiBooster = multiBooster;
            _verticalLander = verticalLander;
            _docking = docking;
            _satellite = satellite;
        }

        public void DrawGUI()
        {
            if (!IsVisible) return;

            GUI.skin = HighLogic.Skin;
            _windowRect = GUILayout.Window(WINDOW_ID, _windowRect, DrawWindowContent, "ORÁCULO 2.0 - Central de Controle", GUILayout.Width(490));
        }

        private void DrawWindowContent(int windowID)
        {
            GUILayout.BeginVertical();

            // Abas de navegação principais
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            GUILayout.Space(8);

            switch (_selectedTab)
            {
                case 0:
                    DrawTelemetryTab();
                    break;
                case 1:
                    DrawLaunchTab();
                    break;
                case 2:
                    DrawLandingTab();
                    break;
                case 3:
                    DrawDockingTab();
                    break;
                case 4:
                    DrawMissionsTab();
                    break;
            }

            GUILayout.FlexibleSpace();

            if (_multiBooster.ActiveBoosters.Count > 0)
            {
                GUILayout.Label($"<color=#33ccff>⚙ {_multiBooster.GetSummary()}</color>");
            }

            GUILayout.Box($"Status: {GetCurrentStatus()}", GUILayout.ExpandWidth(true));

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawTelemetryTab()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null)
            {
                GUILayout.Label("<i>Nenhuma nave ativa no momento.</i>");
                return;
            }

            GUILayout.Label($"<b>Telemetria Geral: <color=#00ff88>{v.vesselName}</color> ({v.mainBody.bodyName})</b>");
            GUILayout.Space(4);

            // 1. DADOS ORBITAIS
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>Órbita:</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Apoastro: <b>{v.orbit.ApA / 1000.0:F1} km</b> (T-{FormatTime(v.orbit.timeToAp)})", GUILayout.Width(230));
            GUILayout.Label($"Periastro: <b>{v.orbit.PeA / 1000.0:F1} km</b> (T-{FormatTime(v.orbit.timeToPe)})");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Inclinação: <b>{v.orbit.inclination:F2}°</b>", GUILayout.Width(230));
            GUILayout.Label($"Excentricidade: <b>{v.orbit.eccentricity:F4}</b>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Arg. Periastro (ω): <b>{v.orbit.argumentOfPeriapsis:F1}°</b>", GUILayout.Width(230));
            GUILayout.Label($"Período: <b>{FormatTime(v.orbit.period)}</b>");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // 2. DINÂMICA DE VOO E VELOCIDADES
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>Dinâmica de Voo:</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Vel. Orbital: <b>{v.obt_speed:F1} m/s</b>", GUILayout.Width(230));
            GUILayout.Label($"Vel. Superfície: <b>{v.srfSpeed:F1} m/s</b>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Altitude Nível do Mar: <b>{v.altitude / 1000.0:F2} km</b>", GUILayout.Width(230));
            GUILayout.Label($"Altitude Radar: <b>{v.radarAltitude:F0} m</b>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Vel. Vertical: <b>{v.verticalSpeed:F1} m/s</b>", GUILayout.Width(230));
            GUILayout.Label($"Pressão Dinâmica: <b>{v.dynamicPressurekPa:F2} kPa</b>");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // 3. PROPULSÃO, TWR E DELTA-V
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>Propulsão & Delta-V:</b>");
            double localGravity = v.mainBody.GeeASL * NavigationMath.StandardGravity;
            double mass = v.GetTotalMass();
            double currentThrust = v.GetCurrentThrust();
            double currentTwr = (mass > 0 && localGravity > 0) ? (currentThrust / (mass * localGravity)) : 0.0;
            double maxTwr = NavigationMath.GetMaxTWR(v);

            double stageDV = 0.0;
            double totalDV = 0.0;
            if (v.VesselDeltaV != null)
            {
                stageDV = v.VesselDeltaV.GetStageDeltaV(v.currentStage, DeltaVSituationOptions.Current);
                totalDV = v.VesselDeltaV.TotalDeltaVActual;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"TWR Atual: <b>{currentTwr:F2}</b> | TWR Máx: <b>{maxTwr:F2}</b>", GUILayout.Width(230));
            GUILayout.Label($"Empuxo Máx: <b>{NavigationMath.GetAvailableThrust(v):F1} kN</b>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Delta-V Estágio Atual: <b>{stageDV:F0} m/s</b>", GUILayout.Width(230));
            GUILayout.Label($"Delta-V Total: <b><color=#00ff88>{totalDV:F0} m/s</color></b>");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // 4. CONEXÃO COMMNET E ENERGIA
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>Conexão CommNet & Salvaguarda de Energia:</b>");
            bool connected = v.connection != null && v.connection.IsConnected;
            double signalStrength = v.connection != null ? v.connection.SignalStrength : 0.0;
            string signalColor = signalStrength > 0.7 ? "#00ff88" : (signalStrength > 0.25 ? "#ffcc00" : "#ff3333");
            int hops = (v.connection != null && v.connection.ControlPath != null) ? v.connection.ControlPath.Count : 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Sinal: <b>{(connected ? "<color=#00ff88>Conectado</color>" : "<color=#ff3333>Sem Sinal</color>")} ({(signalStrength * 100.0):F0}%)</b>", GUILayout.Width(230));
            GUILayout.Label($"Controle: <b>{v.CurrentControlLevel}</b> | Saltos: <b>{hops}</b>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Status Energia: <b>{_batteryGuard.StatusSummary}</b>", GUILayout.Width(300));
            _batteryGuard.GuardEnabled = GUILayout.Toggle(_batteryGuard.GuardEnabled, "Auto-Hibernação");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // 5. REGRAS DE AUTOMAÇÃO DE PEÇAS RETRÁTEIS
            GUILayout.BeginVertical("box");
            GUILayout.Label("<b>Automação Atmosférica de Dispositivos:</b>");
            _deployables.AutoManageEnabled = GUILayout.Toggle(_deployables.AutoManageEnabled, "Auto-estender no espaço (>70km) e recolher na reentrada");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ESTENDER TUDO", GUILayout.Height(24)))
            {
                _deployables.ExtendAllDeployables();
            }
            if (GUILayout.Button("RECOLHER TUDO", GUILayout.Height(24)))
            {
                _deployables.RetractAllDeployables();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"<i>Status: {_deployables.StatusSummary}</i>");
            GUILayout.EndVertical();
        }

        private void DrawLaunchTab()
        {
            GUILayout.Label("<b>Sistema de Decolagem:</b>");
            _launchModeIndex = GUILayout.Toolbar(_launchModeIndex, _launchModes);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Órbita Alvo (km):", GUILayout.Width(130));
            _targetOrbitKmStr = GUILayout.TextField(_targetOrbitKmStr, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            if (_launchModeIndex == 0)
            {
                // MODO ORÁCULO 2.0 (NATIVO - MECHPESTE)
                GUILayout.Space(6);
                GUILayout.Label("<b>Perfil da Curva de Gravidade:</b>");
                _selectedProfileIndex = GUILayout.Toolbar(_selectedProfileIndex, _profileNames);

                GUILayout.Space(4);
                _nativeAscent.EnableAutoStage = GUILayout.Toggle(_nativeAscent.EnableAutoStage, "<b>Auto-Staging:</b> Desacoplar estágios esgotados automaticamente (Flameout)");

                GUILayout.Space(4);
                GUILayout.Label("<i>Curva matemática contínua, corte por Max-Q e circularização por Vis-Viva.</i>");
                GUILayout.Space(12);

                if (!_nativeAscent.IsActive)
                {
                    if (GUILayout.Button("INICIAR DECOLAGEM ORBITAL (ORÁCULO 2.0)", GUILayout.Height(38)))
                    {
                        if (double.TryParse(_targetOrbitKmStr, out double altKm))
                        {
                            TurnProfile profile = (TurnProfile)_selectedProfileIndex;
                            _nativeAscent.StartAscent(altKm * 1000.0, profile);
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button("CANCELAR LANÇAMENTO (2.0)", GUILayout.Height(38)))
                    {
                        _nativeAscent.Abort();
                    }
                }
            }
            else if (_launchModeIndex == 1)
            {
                // MODO MECHJEB LEGADO
                GUILayout.BeginHorizontal();
                GUILayout.Label("Inclinação (°):", GUILayout.Width(130));
                _targetIncStr = GUILayout.TextField(_targetIncStr, GUILayout.Width(80));
                GUILayout.EndHorizontal();

                _enableFalconMECO = GUILayout.Toggle(_enableFalconMECO, "Reservar combustível p/ 1º Estágio (MECO)");
                if (_enableFalconMECO)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("  Reserva Delta-V (m/s):", GUILayout.Width(160));
                    _boosterReserveDvStr = GUILayout.TextField(_boosterReserveDvStr, GUILayout.Width(60));
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(10);
                if (!_ascent.IsActive)
                {
                    if (GUILayout.Button("INICIAR LANÇAMENTO (MECHJEB)", GUILayout.Height(35)))
                    {
                        if (double.TryParse(_targetOrbitKmStr, out double altKm)) _ascent.TargetAltitude = altKm * 1000;
                        if (double.TryParse(_targetIncStr, out double inc)) _ascent.TargetInclination = inc;
                        if (double.TryParse(_boosterReserveDvStr, out double dv)) _ascent.ReservedDeltaV = dv;
                        _ascent.EnableBoosterReserve = _enableFalconMECO;

                        _ascent.StartAscent();
                    }
                }
                else
                {
                    if (GUILayout.Button("CANCELAR LANÇAMENTO", GUILayout.Height(35)))
                    {
                        _ascent.StopAscent();
                    }
                }
            }
            else
            {
                // SSTO
                GUILayout.Space(12);
                if (!_ssto.IsActive)
                {
                    if (GUILayout.Button("DECOLAR SSTO (PISTA)", GUILayout.Height(35)))
                    {
                        if (double.TryParse(_targetOrbitKmStr, out double altKm)) _ssto.TargetOrbitAltitude = altKm * 1000;
                        _ssto.StartSSTOLaunch();
                    }
                }
                else
                {
                    if (GUILayout.Button("INTERROMPER SSTO", GUILayout.Height(35)))
                    {
                        _ssto.Stop();
                    }
                }
            }
        }

        private void DrawLandingTab()
        {
            GUILayout.Label("<b>Escolha o Modo de Pouso:</b>");
            _landingCategoryIndex = GUILayout.Toolbar(_landingCategoryIndex, _landingCategories);
            GUILayout.Space(10);

            switch (_landingCategoryIndex)
            {
                case 0:
                    DrawSuicideBurnSubTab();
                    break;
                case 1:
                    DrawRocketKSCSubTab();
                    break;
                case 2:
                    DrawAirplaneRunwaySubTab();
                    break;
                case 3:
                    DrawVerticalLanderSubTab();
                    break;
            }
        }

        private void DrawSuicideBurnSubTab()
        {
            Vessel active = FlightGlobals.ActiveVessel;
            double maxTwr = NavigationMath.GetMaxTWR(active);
            double availThrust = NavigationMath.GetAvailableThrust(active);

            GUILayout.Label("<b>Suicide Burn / Hoverslam (Oráculo 2.0 Nativo):</b>");
            GUILayout.Label($"Empuxo Máx. Real: <b>{availThrust:F1} kN</b> | TWR Máx.: <b>{maxTwr:F2}</b>");
            GUILayout.Label("<i>Alinha no retrógrado, anula velocidade horizontal e pousa suave a 1 m/s.</i>");
            GUILayout.Space(10);

            if (!_suicideBurn.IsActive)
            {
                if (GUILayout.Button("EXECUTAR SUICIDE BURN", GUILayout.Height(40)))
                {
                    _suicideBurn.StartSuicideBurn();
                }
            }
            else
            {
                if (GUILayout.Button("ABORTAR POUSO (2.0)", GUILayout.Height(40)))
                {
                    _suicideBurn.Abort();
                }
            }
        }

        private void DrawAirplaneRunwaySubTab()
        {
            GUILayout.Label("<b>Pouso na Pista (Avião / SSTO):</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_selectedRunwayIndex == 0, "KSC Runway")) _selectedRunwayIndex = 0;
            if (GUILayout.Toggle(_selectedRunwayIndex == 1, "Pista da Ilha Antiga")) _selectedRunwayIndex = 1;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<i>Abre trem de pouso, freios de ar, rodas e paraquedas de arrasto.</i>");
            GUILayout.Space(10);

            if (!_runwayLanding.IsActive)
            {
                if (GUILayout.Button("POUSAR AVIÃO NA PISTA", GUILayout.Height(35)))
                {
                    var runway = (_selectedRunwayIndex == 0) ? RunwayLandingController.KSCRunway : RunwayLandingController.IslandAirfield;
                    _runwayLanding.StartLanding(runway);
                }
            }
            else
            {
                if (GUILayout.Button("CANCELAR POUSO NA PISTA", GUILayout.Height(35)))
                {
                    _runwayLanding.Abort();
                }
            }
        }

        private void DrawRocketKSCSubTab()
        {
            GUILayout.Label("<b>Foguetes e Sondas: Desorbita e Pouso no KSC:</b>");
            GUILayout.Label("<i>Calcula a queima de desorbita em órbita de Kerbin e pousa na base.</i>");
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Alvo no KSC:", GUILayout.Width(90));
            if (GUILayout.Toggle(_kscSiteIndex == 0, "Launchpad")) _kscSiteIndex = 0;
            if (GUILayout.Toggle(_kscSiteIndex == 1, "LZ-1")) _kscSiteIndex = 1;
            if (GUILayout.Toggle(_kscSiteIndex == 2, "Pista")) _kscSiteIndex = 2;
            GUILayout.EndHorizontal();

            _kscOrbitLanding.DeployGearAutomatically = GUILayout.Toggle(_kscOrbitLanding.DeployGearAutomatically, "Baixar trem de pouso a 100m");
            _kscOrbitLanding.DeployChutesIfAvailable = GUILayout.Toggle(_kscOrbitLanding.DeployChutesIfAvailable, "Acionar paraquedas se disponíveis");

            GUILayout.Space(10);

            if (!_kscOrbitLanding.IsActive)
            {
                if (GUILayout.Button("DESORBITAR E POUSAR NO KSC", GUILayout.Height(35)))
                {
                    _kscOrbitLanding.TargetSite = (KSCLandingSite)_kscSiteIndex;
                    _kscOrbitLanding.StartDeorbitAndLanding();
                }
            }
            else
            {
                if (GUILayout.Button("CANCELAR DESORBITA KSC", GUILayout.Height(35)))
                {
                    _kscOrbitLanding.Abort();
                }
            }
        }

        private void DrawVerticalLanderSubTab()
        {
            var bodyName = FlightGlobals.currentMainBody != null ? FlightGlobals.currentMainBody.bodyName : "Corpo Atual";
            GUILayout.Label($"<b>Lander Vertical ({bodyName} / Outros Corpos):</b>");
            GUILayout.Label("<i>Pouso propulsivo macio (1.2 m/s) em Mun, Minmus, Duna ou terreno aberto.</i>");
            GUILayout.Space(6);

            _verticalLander.DeployGears = GUILayout.Toggle(_verticalLander.DeployGears, "Baixar pernas de pouso");
            _verticalLander.DeployChutes = GUILayout.Toggle(_verticalLander.DeployChutes, "Abrir paraquedas (em corpos com atmosfera)");

            GUILayout.Space(10);

            if (!_verticalLander.IsActive)
            {
                if (GUILayout.Button($"POUSAR LANDER SUAVEMENTE ({bodyName.ToUpper()})", GUILayout.Height(35)))
                {
                    _verticalLander.StartLandingHere();
                }
            }
            else
            {
                if (GUILayout.Button("CANCELAR POUSO VERTICAL", GUILayout.Height(35)))
                {
                    _verticalLander.AbortLanding();
                }
            }
        }

        private void DrawDockingTab()
        {
            GUILayout.Label("<b>Acoplagem Total 2.0 (Do Lançamento ao Docking):</b>");
            GUILayout.Space(4);

            var target = FlightGlobals.fetch?.VesselTarget;
            if (target != null)
            {
                GUILayout.Label($"Alvo Selecionado: <b><color=#00ff88>{target.GetName()}</color></b>");
            }
            else
            {
                GUILayout.Label("<color=yellow>Nenhum alvo selecionado. Clique numa estação ou nave no mapa.</color>");
            }

            Vessel active = FlightGlobals.ActiveVessel;
            bool onGround = (active != null && (active.situation == Vessel.Situations.PRELAUNCH || active.situation == Vessel.Situations.LANDED));

            if (onGround)
            {
                GUILayout.Label("<i>Status Atual: <b>Na Plataforma de Lançamento</b></i>");
                GUILayout.Label("<i>O piloto calculará a janela de plano, lançará no azimute do alvo e fará o encontro completo.</i>");
            }
            else
            {
                GUILayout.Label("<i>Status Atual: <b>Já em Voo / Órbita</b></i>");
                GUILayout.Label("<i>O piloto alinhará planos orbitais (AN/DN), fará transferência de Hohmann e docking por RCS.</i>");
            }

            GUILayout.Space(10);

            if (!_fullDocking.IsActive)
            {
                GUI.enabled = (target != null);
                string btnLabel = onGround ? "LANÇAR, ENCONTRAR E ACOPLAR (TOTAL)" : "ENCONTRAR E ACOPLAR NO ALVO (EM ÓRBITA)";
                if (GUILayout.Button(btnLabel, GUILayout.Height(40)))
                {
                    _fullDocking.StartMission(target);
                }
                GUI.enabled = true;
            }
            else
            {
                GUILayout.Label($"<color=#33ccff>Fase Atual: {_fullDocking.CurrentPhase}</color>");
                if (GUILayout.Button("ABORTAR MISSÃO DE ACOPLAGEM", GUILayout.Height(40)))
                {
                    _fullDocking.Abort();
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>Etapas Automatizadas:</b>");
            GUILayout.Label("1. Janela de Lançamento no Plano do Alvo\n2. Subida com Azimute Casado\n3. Órbita de Perseguição (Chase Orbit)\n4. Correção Fina de Nós AN/DN\n5. Transferência de Hohmann (<150m)\n6. Frenagem e Cancelamento de Vel. Relativa\n7. Alinhamento de Portas e Docking Fino por RCS (PID 3D)");
        }

        private void DrawMissionsTab()
        {
            GUILayout.Label("<b>Central de Missões do Oráculo 2.0:</b>");
            _selectedMissionSubTab = GUILayout.Toolbar(_selectedMissionSubTab, _missionSubTabs);
            GUILayout.Space(10);

            switch (_selectedMissionSubTab)
            {
                case 0:
                    DrawSatelliteContent();
                    break;
                case 1:
                    DrawTourismContent();
                    break;
                case 2:
                    DrawLunarContent();
                    break;
            }
        }

        private void DrawSatelliteContent()
        {
            GUILayout.Label("<b>Alinhador de Órbita de Satélites 2.0 (Nativo):</b>");
            GUILayout.Space(4);

            _manualSatelliteEntry = GUILayout.Toggle(_manualSatelliteEntry, "Entrada Manual (ignorar contratos da carreira)");

            if (_manualSatelliteEntry)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Apoastro (km):", GUILayout.Width(110));
                _manualApKm = GUILayout.TextField(_manualApKm, GUILayout.Width(70));
                GUILayout.Label("Periastro (km):", GUILayout.Width(110));
                _manualPeKm = GUILayout.TextField(_manualPeKm, GUILayout.Width(70));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Inclinação (°):", GUILayout.Width(110));
                _manualInc = GUILayout.TextField(_manualInc, GUILayout.Width(70));
                GUILayout.Label("Arg. Pe (ω°):", GUILayout.Width(110));
                _manualArgPe = GUILayout.TextField(_manualArgPe, GUILayout.Width(70));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<b>Contratos Ativos Detectados na Carreira:</b>");
                _contractScroll = GUILayout.BeginScrollView(_contractScroll, GUILayout.Height(100));
                if (_satelliteGuidance.DetectedContracts.Count == 0)
                {
                    GUILayout.Label("<i>Nenhum contrato de órbita ativo encontrado no momento.</i>");
                }
                else
                {
                    for (int i = 0; i < _satelliteGuidance.DetectedContracts.Count; i++)
                    {
                        var c = _satelliteGuidance.DetectedContracts[i];
                        bool isSelected = (_satelliteGuidance.SelectedTarget == c);
                        if (GUILayout.Toggle(isSelected, $"{c.Title}"))
                        {
                            _satelliteGuidance.SelectedTarget = c;
                        }
                    }
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(10);

            if (!_satelliteGuidance.IsActive)
            {
                if (GUILayout.Button("EXECUTAR ALINHAMENTO DE ÓRBITA (3 ETAPAS)", GUILayout.Height(38)))
                {
                    SpecificOrbit targetToUse = _satelliteGuidance.SelectedTarget;
                    if (_manualSatelliteEntry)
                    {
                        double.TryParse(_manualApKm, out double ap);
                        double.TryParse(_manualPeKm, out double pe);
                        double.TryParse(_manualInc, out double inc);
                        double.TryParse(_manualArgPe, out double argPe);
                        targetToUse = new SpecificOrbit("Órbita Manual", FlightGlobals.currentMainBody, ap, pe, inc, 0, argPe);
                    }

                    if (targetToUse != null)
                    {
                        _satelliteGuidance.StartAlignment(targetToUse);
                    }
                }
            }
            else
            {
                GUILayout.Label($"<color=#33ccff>Fase Atual: {_satelliteGuidance.CurrentPhase}</color>");
                if (GUILayout.Button("ABORTAR ALINHAMENTO (2.0)", GUILayout.Height(38)))
                {
                    _satelliteGuidance.Abort();
                }
            }

            GUILayout.Space(6);
            GUILayout.Label("<i>Garante alinhamento de plano, Apoastro e Periastro no lado exato do planeta (Argumento do Periastro ω).</i>");
        }

        private void DrawTourismContent()
        {
            GUILayout.Label("<b>Voos Turísticos Automatizados 2.0:</b>");
            GUILayout.Space(4);

            GUILayout.Label("<b>Modalidade do Voo Turístico:</b>");
            _tourismModeIndex = GUILayout.Toolbar(_tourismModeIndex, _tourismModes);
            GUILayout.Space(6);

            GUILayout.Label("<b>Método de Aterrissagem / Recuperação:</b>");
            _recoveryMethodIndex = GUILayout.Toolbar(_recoveryMethodIndex, _recoveryMethods);
            GUILayout.Space(10);

            if (!_tourism.IsActive)
            {
                string btnLabel = (_tourismModeIndex == 0) ? "INICIAR SALTO SUBORBITAL TURÍSTICO" : "INICIAR VOO ORBITAL TURÍSTICO COMPLETO";
                if (GUILayout.Button(btnLabel, GUILayout.Height(42)))
                {
                    TourismMode mode = (_tourismModeIndex == 0) ? TourismMode.SuborbitalHop : TourismMode.OrbitalCruise;
                    LandingRecoveryType recovery = (_recoveryMethodIndex == 0) ? LandingRecoveryType.Parachutes : LandingRecoveryType.SuicideBurnHoverslam;
                    _tourism.StartMission(mode, recovery);
                }
            }
            else
            {
                GUILayout.Label($"<color=#33ccff>Fase Turística: {_tourism.CurrentPhase}</color>");
                if (GUILayout.Button("ABORTAR VOO TURÍSTICO", GUILayout.Height(40)))
                {
                    _tourism.Abort();
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>Automações Inclusas:</b>");
            GUILayout.Label("• Subida vertical / orbital segura com TWR controlado\n• Saída garantida da atmosfera (>70km) p/ validação de contrato\n• Orientação retrógrada automática no escudo térmico durante reentrada\n• Abertura de paraquedas a 2.000m ou Hoverslam propulsivo nos metros finais");
        }

        private void DrawLunarContent()
        {
            GUILayout.Label("<b>Transferência e Inserção Orbital em Luas (1-Clique):</b>");
            GUILayout.Space(4);

            GUILayout.Label("<b>Escolha a Lua Destino:</b>");
            _selectedMoonIndex = GUILayout.Toolbar(_selectedMoonIndex, _moonNames);
            GUILayout.Space(10);

            string chosenMoon = (_selectedMoonIndex == 0) ? "Mun" : "Minmus";

            if (!_lunarTransfer.IsActive)
            {
                if (GUILayout.Button($"TRANSFERIR E CIRCULARIZAR EM {chosenMoon.ToUpper()}", GUILayout.Height(42)))
                {
                    _lunarTransfer.StartTransfer(chosenMoon);
                }
            }
            else
            {
                GUILayout.Label($"<color=#33ccff>Fase Lunar: {_lunarTransfer.CurrentPhase}</color>");
                if (GUILayout.Button("ABORTAR TRANSFERÊNCIA LUNAR", GUILayout.Height(40)))
                {
                    _lunarTransfer.Abort();
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>Sequência de Voo Lunar:</b>");
            GUILayout.Label("1. Cálculo do ângulo de fase de injeção translunar (TLI)\n2. Queima Prograde de aceleração (~860 m/s para Mun)\n3. Viagem no tempo até a Esfera de Influência (SOI)\n4. Queima Retrógrada de captura e circularização em órbita baixa");
        }

        private string GetCurrentStatus()
        {
            if (_lunarTransfer.IsActive) return _lunarTransfer.StatusMessage;
            if (_fullDocking.IsActive) return _fullDocking.StatusMessage;
            if (_tourism.IsActive) return _tourism.StatusMessage;
            if (_satelliteGuidance.IsActive) return _satelliteGuidance.StatusMessage;
            if (_nativeAscent.IsActive) return _nativeAscent.StatusMessage;
            if (_suicideBurn.IsActive) return _suicideBurn.StatusMessage;
            if (_ascent.IsActive) return _ascent.StatusMessage;
            if (_ssto.IsActive) return _ssto.StatusMessage;
            if (_runwayLanding.IsActive) return _runwayLanding.StatusMessage;
            if (_kscOrbitLanding.IsActive) return _kscOrbitLanding.StatusMessage;
            if (_verticalLander.IsActive) return _verticalLander.StatusMessage;
            if (_docking.IsActive) return _docking.StatusMessage;
            if (_satellite.IsActive) return _satellite.StatusMessage;
            return "Pronto para comando.";
        }

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1)
            {
                return $"{t.Hours:D2}h {t.Minutes:D2}m {t.Seconds:D2}s";
            }
            return $"{t.Minutes:D2}m {t.Seconds:D2}s";
        }
    }
}
