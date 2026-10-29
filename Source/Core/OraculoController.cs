using System;
using UnityEngine;
using MuMech;
using OraculoGuidance.UI;
using OraculoGuidance.Modules.Ascent;
using OraculoGuidance.Modules.Landing;
using OraculoGuidance.Modules.Rendezvous;
using OraculoGuidance.Modules.Contracts;
using OraculoGuidance.Modules.Tourism;
using OraculoGuidance.Modules.Missions;

namespace OraculoGuidance.Core
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class OraculoController : MonoBehaviour
    {
        public static OraculoController Instance { get; private set; }

        private Vessel _currentVessel;
        private MechJebCore _mechJebCore;

        // Módulos Nativos 2.0 (Baseados no MechPeste + Física Avançada)
        private OrbitalAscentController _nativeAscent;
        private SuicideBurnController _suicideBurn;
        private FullMissionDockingController _fullDocking;
        private TourismFlightController _tourism;
        private SatelliteGuidanceController _satelliteGuidance;
        private LunarTransferController _lunarTransfer;
        private DeployablePartsManager _deployables;
        private BatterySafetyGuard _batteryGuard;

        // Módulos Auxiliares Legados
        private AscentController _ascent;
        private SSTOController _ssto;
        private RunwayLandingController _runwayLanding;
        private KSCOrbitLandingController _kscOrbitLanding;
        private MultiBoosterManager _multiBooster;
        private PrecisionLanding _verticalLander;
        private AutoDockingController _docking;
        private SatelliteContractController _satellite;

        // Interface Gráfica
        private MainWindow _ui;

        private void Awake()
        {
            Instance = this;

            // Inicializa novos módulos nativos do Oráculo 2.0
            _nativeAscent = new OrbitalAscentController();
            _suicideBurn = new SuicideBurnController();
            _fullDocking = new FullMissionDockingController(_nativeAscent);
            _tourism = new TourismFlightController(_nativeAscent, _suicideBurn);
            _satelliteGuidance = new SatelliteGuidanceController();
            _lunarTransfer = new LunarTransferController();
            _deployables = new DeployablePartsManager();
            _batteryGuard = new BatterySafetyGuard();

            _ascent = new AscentController();
            _ssto = new SSTOController();
            _runwayLanding = new RunwayLandingController();
            _kscOrbitLanding = new KSCOrbitLandingController();
            _multiBooster = new MultiBoosterManager();
            _verticalLander = new PrecisionLanding();
            _docking = new AutoDockingController();
            _satellite = new SatelliteContractController();

            _ui = new MainWindow(
                _nativeAscent,
                _suicideBurn,
                _fullDocking,
                _tourism,
                _satelliteGuidance,
                _lunarTransfer,
                _deployables,
                _batteryGuard,
                _ascent,
                _ssto,
                _runwayLanding,
                _kscOrbitLanding,
                _multiBooster,
                _verticalLander,
                _docking,
                _satellite
            );

            Debug.Log("[Oraculo 2.0] Inicializado com todos os sistemas e expansões completas!");
        }

        private void Start()
        {
            GameEvents.onVesselChange.Add(OnVesselChange);
            UpdateCurrentVessel();
        }

        private void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            _multiBooster?.Dispose();
        }

        private void Update()
        {
            // Atalho de teclado: Tecla F8 ou Alt + O abre/fecha a janelinha do Oráculo
            if ((GameSettings.MODIFIER_KEY.GetKey() && Input.GetKeyDown(KeyCode.O)) || Input.GetKeyDown(KeyCode.F8))
            {
                _ui.IsVisible = !_ui.IsVisible;
            }

            if (_currentVessel != FlightGlobals.ActiveVessel)
            {
                UpdateCurrentVessel();
            }

            // Atualiza módulos nativos do Oráculo 2.0
            _nativeAscent.Update();
            _suicideBurn.Update();
            _fullDocking.Update();
            _tourism.Update();
            _satelliteGuidance.Update();
            _lunarTransfer.Update();
            _deployables.Update();
            _batteryGuard.Update();

            _ascent.Update();
            _kscOrbitLanding.Update();
            _verticalLander.Update();
            _multiBooster.Update();
            _docking.Update();
            _satellite.Update();
        }

        private void FixedUpdate()
        {
            _ssto.FixedUpdate();
            _runwayLanding.FixedUpdate();
        }

        private void OnGUI()
        {
            _ui.DrawGUI();
        }

        private void OnVesselChange(Vessel newVessel)
        {
            UpdateCurrentVessel();
        }

        private void UpdateCurrentVessel()
        {
            _currentVessel = FlightGlobals.ActiveVessel;
            if (_currentVessel == null) return;

            // Inicializa os módulos nativos 2.0
            _nativeAscent.Initialize(_currentVessel);
            _suicideBurn.Initialize(_currentVessel);
            _fullDocking.Initialize(_currentVessel);
            _tourism.Initialize(_currentVessel);
            _satelliteGuidance.Initialize(_currentVessel);
            _lunarTransfer.Initialize(_currentVessel);
            _deployables.Initialize(_currentVessel);
            _batteryGuard.Initialize(_currentVessel);

            // Busca ou assegura o MechJebCore na nave atual para módulos legados
            _mechJebCore = _currentVessel.GetMasterMechJeb();
            if (_mechJebCore == null && _currentVessel.rootPart != null)
            {
                _mechJebCore = _currentVessel.rootPart.AddModule("MechJebCore") as MechJebCore;
            }

            _ascent.Initialize(_currentVessel, _mechJebCore);
            _ssto.Initialize(_currentVessel, _mechJebCore);
            _runwayLanding.Initialize(_currentVessel, _mechJebCore);
            _kscOrbitLanding.Initialize(_currentVessel, _mechJebCore);
            _verticalLander.Initialize(_currentVessel, _mechJebCore);
            _docking.Initialize(_currentVessel, _mechJebCore);
            _satellite.Initialize(_currentVessel, _mechJebCore);
        }
    }
}
