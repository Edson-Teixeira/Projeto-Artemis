using System;
using UnityEngine;
using Artemis.Telemetry;
using Artemis.Flight;
using Artemis.Systems;
using Artemis.UI;
using MuMech;

namespace Artemis
{
    /// <summary>
    /// ArtemisCore: O coração do mod dentro do KSP.
    /// É um MonoBehaviour inicializado automaticamente pelo KSP sempre que entramos na cena de voo (Flight).
    /// Coordena todos os módulos, gerencia o Roll global em 0°, lê o teclado (F8) e desenha a interface na tela.
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
        private readonly SuborbitalTourismController _tourism = new SuborbitalTourismController();

        // Interface Gráfica
        private readonly MainWindow _mainWindow = new MainWindow();

        private Vessel _hookedVessel = null;

        /// <summary>
        /// Mantém o Roll sempre em 0° de forma global.
        /// </summary>
        public bool GlobalRollHold { get; set; } = true;

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

            // 2. Gerenciador de partes retráteis (sempre ativo: >75km abre / <70km recolhe tudo)
            _deployableManager.Update(_telemetry);

            // 3. Atualiza pilotos automáticos
            if (_ascent.IsActive)
            {
                _ascent.Update(_telemetry, _autoStaging, _deployableManager);
            }
            else if (_tourism.IsActive)
            {
                _tourism.Update(_telemetry, _autoStaging, _deployableManager, _parachuteManager, _reentry);
            }
            else if (_reentry.IsActive)
            {
                _reentry.Update(_telemetry, _parachuteManager);
            }
            else if (GlobalRollHold)
            {
                // Se nenhum piloto específico estiver controlando o rumo, mas o Roll global estiver ligado:
                // mantém o Roll travado em 0.0° na direção em que a nave já está apontada
                ApplyGlobalRollHold();
            }
        }

        private void OnGUI()
        {
            // Desenha a janela do Artemis
            _mainWindow.Draw(_telemetry, _ascent, _reentry, _tourism, this);
        }

        /// <summary>
        /// Aplica o travamento global de Roll em 0° enquanto a nave está em voo livre.
        /// </summary>
        private void ApplyGlobalRollHold()
        {
            if (_telemetry.Vessel == null || _telemetry.MechJeb == null) return;

            // Converte a orientação atual da nave em Heading e Pitch
            Vector3d forward = _telemetry.Vessel.ReferenceTransform.up;
            TelemetryReader.VectorToHeadingPitch(_telemetry.Vessel, forward, out double heading, out double pitch);

            var attitude = _telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
            if (attitude != null)
            {
                // Comanda a direção atual porém com ROLL = 0.0°
                attitude.attitudeTo(heading, pitch, 0.0, this);
            }
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
        /// Chamado pelo KSP a cada frame de controle físico para aplicar aceleração do acelerador.
        /// </summary>
        private void OnFlyByWireCallback(FlightCtrlState state)
        {
            float commanded = 0f;
            if (_ascent.IsActive)
            {
                commanded = _ascent.CommandedThrottle;
            }
            else if (_tourism.IsActive)
            {
                commanded = _tourism.CommandedThrottle;
            }

            if (_ascent.IsActive || _tourism.IsActive)
            {
                state.mainThrottle = commanded;
                FlightInputHandler.state.mainThrottle = commanded;
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
