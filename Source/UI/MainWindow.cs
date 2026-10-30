using System;
using UnityEngine;
using Artemis.Flight;
using Artemis.Telemetry;

namespace Artemis.UI
{
    /// <summary>
    /// MainWindow: Interface gráfica flutuante dentro do jogo (KSP IMGUI).
    /// Pressione F8 para abrir ou fechar a janela.
    /// Contém duas abas limpas: [Decolagem Orbital] e [Reentrada e Pouso].
    /// </summary>
    public class MainWindow
    {
        private Rect _windowRect = new Rect(100, 100, 340, 360);
        private int _windowId = 987654;
        private bool _isOpen = true;
        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "Decolagem", "Reentrada" };

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

        public void Draw(TelemetryReader telemetry, AscentAutopilot ascent, ReentryAutopilot reentry)
        {
            if (!_isOpen) return;

            // Define o visual da janela
            GUI.skin = HighLogic.Skin;
            _windowRect = GUILayout.Window(_windowId, _windowRect, (id) => DrawWindowContent(id, telemetry, ascent, reentry), " Artemis - Piloto Automático");
        }

        private void DrawWindowContent(int windowId, TelemetryReader telemetry, AscentAutopilot ascent, ReentryAutopilot reentry)
        {
            GUILayout.BeginVertical();

            // Seletor de Abas
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            GUILayout.Space(8);

            if (_selectedTab == 0)
            {
                DrawAscentTab(telemetry, ascent);
            }
            else
            {
                DrawReentryTab(telemetry, reentry);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("<size=10><color=#888888>Pressione F8 para ocultar/exibir esta janela.</color></size>");

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

            GUILayout.Label("<color=#aaaaaa>Roll travado em 45° (Janelas para cima)</color>");
            GUILayout.Space(6);

            // Botão Iniciar / Abortar
            if (!ascent.IsActive)
            {
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("INICIAR DECOLAGEM", GUILayout.Height(32)))
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
                if (GUILayout.Button("ABORTAR DECOLAGEM", GUILayout.Height(32)))
                {
                    ascent.Abort(telemetry);
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Telemetria de Voo:</b>");
            GUILayout.Label($"Status: <color=#ffff00>{ascent.StatusMessage}</color>");
            GUILayout.Label($"Altitude: {telemetry.AltitudeASL / 1000:F1} km");
            GUILayout.Label($"Apoastro: {telemetry.Apoapsis / 1000:F1} km");
            GUILayout.Label($"Periastro: {telemetry.Periapsis / 1000:F1} km");
            GUILayout.Label($"Acelerador: {ascent.CommandedThrottle * 100:F0}%");
        }

        /// <summary>
        /// Aba 2: Reentrada Atmosférica
        /// </summary>
        private void DrawReentryTab(TelemetryReader telemetry, ReentryAutopilot reentry)
        {
            GUILayout.Label("<b>Controle de Reentrada e Pouso:</b>");
            GUILayout.Label("Trava no vetor Retrograde para orientar o escudo térmico e abre paraquedas (Drogue MK-12 -> Principais).");
            GUILayout.Space(6);

            if (!reentry.IsActive)
            {
                GUI.backgroundColor = new Color(0.2f, 0.7f, 1f);
                if (GUILayout.Button("INICIAR REENTRADA", GUILayout.Height(32)))
                {
                    reentry.StartReentry(telemetry);
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("ABORTAR REENTRADA", GUILayout.Height(32)))
                {
                    reentry.Abort(telemetry);
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Telemetria de Descida:</b>");
            GUILayout.Label($"Status: <color=#ffff00>{reentry.StatusMessage}</color>");
            GUILayout.Label($"Altitude: {telemetry.AltitudeASL / 1000:F1} km");
            GUILayout.Label($"Vel. Vertical: {telemetry.VerticalSpeed:F1} m/s");
            GUILayout.Label($"Vel. Superfície: {telemetry.SurfaceSpeed:F1} m/s");
        }
    }
}
