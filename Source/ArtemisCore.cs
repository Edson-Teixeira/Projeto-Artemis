using System;
using UnityEngine;
using Artemis.Telemetry;
using Artemis.Flight;
using Artemis.Systems;
using Artemis.UI;

namespace Artemis
{
    /// <summary>
    /// ArtemisCore: O coração do mod dentro do KSP.
    /// É um MonoBehaviour inicializado automaticamente pelo KSP sempre que entramos na cena de voo (Flight).
    /// Coordena todos os módulos, lê o teclado (F8) e desenha a interface na tela.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ArtemisCore : MonoBehaviour
    {
        public static ArtemisCore Instance { get; private set; }

        // Subsistemas modulares
        private readonly TelemetryReader _telemetry = new TelemetryReader();
        private readonly AutoStaging _autoStaging = new AutoStaging();
        private readonly DeployableManager _deployableManager = new DeployableManager();
        private readonly ParachuteManager _parachuteManager = new ParachuteManager();

        // Pilotos automáticos
        private readonly AscentAutopilot _ascent = new AscentAutopilot();
        private readonly ReentryAutopilot _reentry = new ReentryAutopilot();

        // Interface Gráfica
        private readonly MainWindow _mainWindow = new MainWindow();

        private Vessel _hookedVessel = null;

        private void Awake()
        {
            Instance = this;
            Debug.Log("[Artemis] ArtemisCore inicializado com sucesso.");
        }

        private void Start()
        {
            HookActiveVessel();
        }

        private void Update()
        {
            // Atalho de teclado: F8 abre ou fecha a janela
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _mainWindow.Toggle();
            }

            // Garante que estamos conectados à nave atualmente controlada
            if (_hookedVessel != FlightGlobals.ActiveVessel)
            {
                HookActiveVessel();
            }
        }

        private void FixedUpdate()
        {
            if (FlightGlobals.ActiveVessel == null) return;

            // 1. Atualiza sensores e telemetria
            _telemetry.UpdateVessel(FlightGlobals.ActiveVessel);

            // 2. Roda a física da decolagem (se ativa)
            if (_ascent.IsActive)
            {
                _ascent.Update(_telemetry, _autoStaging, _deployableManager);
            }

            // 3. Roda a física da reentrada (se ativa)
            if (_reentry.IsActive)
            {
                _reentry.Update(_telemetry, _parachuteManager);
            }
        }

        private void OnGUI()
        {
            // Desenha a janela do Artemis
            _mainWindow.Draw(_telemetry, _ascent, _reentry);
        }

        /// <summary>
        /// Conecta o piloto automático aos controles de voo da nave ativa.
        /// </summary>
        private void HookActiveVessel()
        {
            if (_hookedVessel != null)
            {
                _hookedVessel.OnFlyByWire -= OnFlyByWireCallback;
            }

            _hookedVessel = FlightGlobals.ActiveVessel;
            if (_hookedVessel != null)
            {
                _hookedVessel.OnFlyByWire += OnFlyByWireCallback;
                _telemetry.UpdateVessel(_hookedVessel);
            }
        }

        /// <summary>
        /// Chamado pelo KSP a cada frame de controle físico para aplicar throttle (acelerador).
        /// </summary>
        private void OnFlyByWireCallback(FlightCtrlState state)
        {
            if (_ascent.IsActive)
            {
                state.mainThrottle = _ascent.CommandedThrottle;
                FlightInputHandler.state.mainThrottle = _ascent.CommandedThrottle;
            }
        }

        private void OnDestroy()
        {
            if (_hookedVessel != null)
            {
                _hookedVessel.OnFlyByWire -= OnFlyByWireCallback;
            }
            Instance = null;
            Debug.Log("[Artemis] ArtemisCore descarregado.");
        }
    }
}
