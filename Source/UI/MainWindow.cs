using System;
using UnityEngine;
using Artemis.Flight;
using Artemis.Telemetry;

namespace Artemis.UI
{
    /// <summary>
    /// MainWindow: Interface gráfica flutuante dentro do jogo (KSP IMGUI).
    /// Pressione F8 para abrir ou fechar a janela.
    /// Abas: [Decolagem], [Reentrada] e [Missões (Turismo Sub)].
    /// </summary>
    public class MainWindow
    {
        private Rect _windowRect = new Rect(100, 100, 360, 420);
        private int _windowId = 987654;
        private bool _isOpen = true;
        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "Decolagem", "Reentrada", "Missões" };

        // Campos de texto editáveis pelo usuário
        private string _apoapsisInput = "80000";
        private string _headingInput = "90";

        public bool IsOpen
        {
            get => _isOpen;
            set => _isOpen = value;
        }

        public void Toggle()
        {
            _isOpen = !_isOpen;
        }

        public void Draw(TelemetryReader telemetry, AscentAutopilot ascent, ReentryAutopilot reentry, SuborbitalTourismController tourism, ArtemisCore core)
        {
            if (!_isOpen) return;

            GUI.skin = HighLogic.Skin;
            _windowRect = GUILayout.Window(_windowId, _windowRect, (id) => DrawWindowContent(id, telemetry, ascent, reentry, tourism, core), " Artemis - Piloto de Voo");
        }

        private void DrawWindowContent(int windowId, TelemetryReader telemetry, AscentAutopilot ascent, ReentryAutopilot reentry, SuborbitalTourismController tourism, ArtemisCore core)
        {
            GUILayout.BeginVertical();

            // 1. Barra de ferramentas / Abas
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            GUILayout.Space(8);

            // 2. Conteúdo da aba selecionada
            switch (_selectedTab)
            {
                case 0:
                    DrawAscentTab(telemetry, ascent);
                    break;
                case 1:
                    DrawReentryTab(telemetry, reentry);
                    break;
                case 2:
                    DrawMissionsTab(telemetry, tourism, reentry);
                    break;
            }

            GUILayout.FlexibleSpace();

            // 3. Rodapé com opção Global de Roll e atalho
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=11><b>Roll 0° Global:</b></size>");
            string rollStatus = core.GlobalRollHold ? "<color=#00ff00>ATIVADO</color>" : "<color=#ff5555>DESATIVADO</color>";
            if (GUILayout.Button(rollStatus, GUILayout.Width(110)))
            {
                core.GlobalRollHold = !core.GlobalRollHold;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("<size=10><color=#888888>Pressione [F8] para ocultar/exibir esta janela.</color></size>");

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        /// <summary>
        /// Aba 1: Decolagem Orbital
        /// </summary>
        private void DrawAscentTab(TelemetryReader telemetry, AscentAutopilot ascent)
        {
            GUILayout.Label("<b>Configurações de Lançamento:</b>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Apoastro Alvo (m):", GUILayout.Width(140));
            _apoapsisInput = GUILayout.TextField(_apoapsisInput);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Direção / Azimute (°):", GUILayout.Width(140));
            _headingInput = GUILayout.TextField(_headingInput);
            GUILayout.EndHorizontal();

            GUILayout.Label("<color=#aaaaaa>Roll travado em 0° (Orientação Estável)</color>");
            GUILayout.Space(6);

            // Botão Iniciar / Abortar
            if (!ascent.IsActive)
            {
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("INICIAR DECOLAGEM", GUILayout.Height(30)))
                {
                    if (double.TryParse(_apoapsisInput, out double targetAp) &&
                        double.TryParse(_headingInput, out double heading))
                    {
                        ascent.StartAscent(targetAp, heading);
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("ABORTAR DECOLAGEM", GUILayout.Height(30)))
                {
                    ascent.Abort(telemetry);
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Telemetria de Voo:</b>");
            GUILayout.Label($"Status: <color=#ffff00>{ascent.StatusMessage}</color>");
            GUILayout.Label($"Altitude: {telemetry.AltitudeASL / 1000:F1} km");
            GUILayout.Label($"Apoastro: {telemetry.Apoapsis / 1000:F1} km");
            GUILayout.Label($"Periastro: {telemetry.Periapsis / 1000:F1} km");
            GUILayout.Label($"Acelerador: {ascent.CommandedThrottle * 100:F0}%");
        }

        /// <summary>
        /// Aba 2: Reentrada Atmosférica (Lander)
        /// </summary>
        private void DrawReentryTab(TelemetryReader telemetry, ReentryAutopilot reentry)
        {
            GUILayout.Label("<b>Controle de Reentrada (Modo Lander):</b>");
            GUILayout.Label("<size=11><color=#cccccc>Trava no vetor Retrograde com Roll 0° (escudo térmico) e abre paraquedas (Drogue MK-12 -> Principais).</color></size>");
            GUILayout.Space(6);

            if (!reentry.IsActive)
            {
                GUI.backgroundColor = new Color(0.2f, 0.7f, 1f);
                if (GUILayout.Button("INICIAR REENTRADA LANDER", GUILayout.Height(30)))
                {
                    reentry.StartReentry(telemetry);
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("ABORTAR REENTRADA", GUILayout.Height(30)))
                {
                    reentry.Abort(telemetry);
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Telemetria de Descida:</b>");
            GUILayout.Label($"Status: <color=#ffff00>{reentry.StatusMessage}</color>");
            GUILayout.Label($"Altitude: {telemetry.AltitudeASL / 1000:F1} km");
            GUILayout.Label($"Vel. Vertical: {telemetry.VerticalSpeed:F1} m/s");
            GUILayout.Label($"Vel. Superfície: {telemetry.SurfaceSpeed:F1} m/s");
        }

        /// <summary>
        /// Aba 3: Missões Automatizadas (Turismo Suborbital)
        /// </summary>
        private void DrawMissionsTab(TelemetryReader telemetry, SuborbitalTourismController tourism, ReentryAutopilot reentry)
        {
            GUILayout.Label("<b>Missão: Turismo Suborbital (Kerbin)</b>");
            GUILayout.Label("<size=11><color=#cccccc>Decola até o espaço, atinge Apoastro de 75km (cumprindo turismo), corta os motores e inicia retorno autônomo com o módulo Lander.</color></size>");
            GUILayout.Space(8);

            if (!tourism.IsActive)
            {
                GUI.backgroundColor = new Color(0.9f, 0.6f, 0.1f);
                if (GUILayout.Button("INICIAR TURISMO SUB", GUILayout.Height(34)))
                {
                    tourism.StartMission();
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("ABORTAR MISSÃO DE TURISMO", GUILayout.Height(34)))
                {
                    tourism.Abort(telemetry, reentry);
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Progresso da Missão:</b>");
            GUILayout.Label($"Fase: <color=#00ffff>{tourism.CurrentPhase}</color>");
            GUILayout.Label($"Status: <color=#ffff00>{tourism.StatusMessage}</color>");
            GUILayout.Label($"Altitude: {telemetry.AltitudeASL / 1000:F1} km");
            GUILayout.Label($"Apoastro: {telemetry.Apoapsis / 1000:F1} km");
            GUILayout.Label($"Acelerador: {tourism.CommandedThrottle * 100:F0}%");
        }
    }
}
