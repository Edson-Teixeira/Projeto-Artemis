using System;
using UnityEngine;
using Artemis.Telemetry;
using Artemis.Systems;
using KSP.UI.Screens;
using MuMech;

namespace Artemis.Flight
{
    /// <summary>
    /// Fases da subida orbital (Máquina de Estados).
    /// </summary>
    public enum AscentPhase
    {
        Idle,               // Desligado
        VerticalClimb,      // Subida vertical inicial
        GravityTurn,        // Curva gravitacional com Pitch progressivo e Roll 0°
        CoastingToSpace,    // Motores desligados, subindo por inércia até o vácuo
        Circularization,    // Queima em prograde no apoastro para fechar a órbita
        Complete            // Órbita estável alcançada com sucesso
    }

    /// <summary>
    /// AscentAutopilot: Controla o voo autônomo desde o chão até a órbita completa.
    /// Executa:
    /// 1. Subida vertical inicial rápida com Roll em 0°.
    /// 2. Curva gravitacional acentuada em direção ao azimute alvo (ex: 90° Leste).
    /// 3. Corte exato no Apoastro alvo.
    /// 4. Queima de circularização no Apoastro.
    /// </summary>
    public class AscentAutopilot
    {
        // Parâmetros configuráveis pelo usuário
        public double TargetApoapsis = 80000.0;     // 80 km padrão
        public double TargetHeading = 90.0;         // 90° = Leste
        public double TargetRoll = 0.0;             // Roll travado em 0°

        // Modelo de Curva Gravitacional Clássico do MechJeb 2 (Ascent Path Editor)
        public double TurnStartAltitude = 3500.0;   // Início da curva aos 3.5 km
        public double TurnStartVelocity = 100.0;   // Ou ao atingir ~100 m/s (98.9 m/s)
        public double TurnEndAltitude = 60000.0;   // Fim da curva aos 60 km
        public double FinalFlightPathAngle = 0.0;  // Ângulo final de 0° (horizontal)
        public double TurnShape = 0.40;            // Formato da curva: 40% (expoente 0.40)

        private double _actualTurnStartAltitude = 3500.0;

        public AscentPhase CurrentPhase { get; private set; } = AscentPhase.Idle;
        public bool IsActive => CurrentPhase != AscentPhase.Idle && CurrentPhase != AscentPhase.Complete;
        public string StatusMessage { get; private set; } = "Pronto para decolar";

        private float _commandedThrottle = 0f;
        public float CommandedThrottle => _commandedThrottle;
        private bool _spaceFuelJettisonDone = false;

        public void StartAscent(double targetAp, double heading)
        {
            TargetApoapsis = Math.Max(72000.0, targetAp);
            TargetHeading = (heading % 360.0 + 360.0) % 360.0;
            CurrentPhase = AscentPhase.VerticalClimb;
            _spaceFuelJettisonDone = false;
            _actualTurnStartAltitude = TurnStartAltitude;
            StatusMessage = "Iniciando subida vertical...";
            ScreenMessages.PostScreenMessage($"[Artemis] Lançamento iniciado! Alvo: {TargetApoapsis / 1000:F0}km | Curva MechJeb 2 (3.5km -> 60km, Shape 40%)", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void Abort(TelemetryReader telemetry)
        {
            CurrentPhase = AscentPhase.Idle;
            _commandedThrottle = 0f;
            StatusMessage = "Voo abortado.";
            ReleaseAttitude(telemetry);
        }

        /// <summary>
        /// Loop principal executado a cada frame de física (FixedUpdate).
        /// </summary>
        public void Update(TelemetryReader telemetry, AutoStaging staging, DeployableManager deployables)
        {
            if (!IsActive || telemetry.Vessel == null) return;

            // 1. Atualiza subsistemas auxiliares
            deployables.Update(telemetry);
            staging.Update(telemetry, _commandedThrottle);

            // 2. Máquina de Estados da Subida
            switch (CurrentPhase)
            {
                case AscentPhase.VerticalClimb:
                    HandleVerticalClimb(telemetry);
                    break;

                case AscentPhase.GravityTurn:
                    HandleGravityTurn(telemetry);
                    break;

                case AscentPhase.CoastingToSpace:
                    HandleCoasting(telemetry);
                    break;

                case AscentPhase.Circularization:
                    HandleCircularization(telemetry);
                    break;
            }
        }

        /// <summary>
        /// Fase 1: Sobe reto no azimute alvo até atingir a altitude de início (3.5km) ou velocidade de início (100m/s).
        /// Segue exatamente o padrão do MechJeb 2 Ascent Path Editor.
        /// </summary>
        private void HandleVerticalClimb(TelemetryReader telemetry)
        {
            _commandedThrottle = 1.0f;
            SetAttitude(telemetry, TargetHeading, 90.0, TargetRoll);

            StatusMessage = $"Subida Vertical: {telemetry.AltitudeASL / 1000:F1}km / 3.5km ({telemetry.SurfaceSpeed:F0} m/s)";

            // Condição do MechJeb: Inicia a curva ao atingir 3.5 km OU velocidade de 100 m/s (~98.9 m/s)
            if (telemetry.AltitudeASL >= TurnStartAltitude || telemetry.SurfaceSpeed >= TurnStartVelocity)
            {
                _actualTurnStartAltitude = Math.Min(telemetry.AltitudeASL, TurnEndAltitude - 5000.0);
                CurrentPhase = AscentPhase.GravityTurn;
                StatusMessage = "Iniciando Curva Gravitacional (Modelo MechJeb 2)...";
                ScreenMessages.PostScreenMessage("[Artemis] Curva Gravitacional MechJeb iniciada!", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Fase 2: Curva gravitacional baseada no modelo clássico do MechJeb 2:
        /// pitch = 90 - (progress^TurnShape) * (90 - FinalFlightPathAngle)
        /// Onde:
        /// - TurnStart: 3.5 km (ou velocidade de disparo)
        /// - TurnEnd: 60 km
        /// - FinalFlightPathAngle: 0°
        /// - TurnShape: 40% (0.40)
        /// </summary>
        private void HandleGravityTurn(TelemetryReader telemetry)
        {
            double alt = telemetry.AltitudeASL;

            // Fração normalizada de altitude entre o início da curva e o final da atmosfera (60km)
            double range = Math.Max(100.0, TurnEndAltitude - _actualTurnStartAltitude);
            double fraction = Mathf.Clamp01((float)((alt - _actualTurnStartAltitude) / range));

            // Curva de potência exponencial do MechJeb (TurnShape = 0.40)
            double progress = Math.Pow(fraction, TurnShape);

            // Cálculo do pitch alvo
            double pitch = 90.0 - (progress * (90.0 - FinalFlightPathAngle));
            pitch = Mathf.Clamp((float)pitch, (float)FinalFlightPathAngle, 90f);

            SetAttitude(telemetry, TargetHeading, pitch, TargetRoll);

            // Controle do acelerador perto do apoastro alvo
            double apError = TargetApoapsis - telemetry.Apoapsis;
            if (apError <= 0)
            {
                _commandedThrottle = 0f;
                CurrentPhase = AscentPhase.CoastingToSpace;
                StatusMessage = "Apoastro atingido. Cortando motores e planando até o vácuo...";
                ScreenMessages.PostScreenMessage("[Artemis] Apoastro alcançado! Desligando aceleração.", 4f, ScreenMessageStyle.UPPER_CENTER);
            }
            else if (apError < 3000.0)
            {
                // Estrangulamento suave para não passar do apoastro desejado
                _commandedThrottle = Mathf.Clamp01((float)(apError / 3000.0));
                StatusMessage = $"Aproximando do corte: Pitch {pitch:F0}° | Ap {telemetry.Apoapsis/1000:F1}km";
            }
            else
            {
                _commandedThrottle = 1.0f;
                StatusMessage = $"Curva MechJeb ({TurnShape*100:F0}%): Pitch {pitch:F0}° | Ap {telemetry.Apoapsis/1000:F1}km";
            }
        }

        /// <summary>
        /// Fase 3: Espera sair da atmosfera e chegar a poucos segundos do Apoastro para circularizar.
        /// </summary>
        private void HandleCoasting(TelemetryReader telemetry)
        {
            _commandedThrottle = 0f;

            // Mantém a nave apontada na horizontal (prograde)
            SetAttitude(telemetry, TargetHeading, 0.0, TargetRoll);

            // Ao chegar no espaço (>= 70km), verifica se o estágio de combustível está vazio e desacopla
            if (telemetry.AltitudeASL >= 70000.0 && !_spaceFuelJettisonDone)
            {
                _spaceFuelJettisonDone = true;
                if (TelemetryReader.IsCurrentFuelEmpty(telemetry.Vessel) && StageManager.CurrentStage > 0)
                {
                    Debug.Log("[Artemis] Módulo de combustível vazio no espaço. Desacoplando!");
                    ScreenMessages.PostScreenMessage("[Artemis] Chegamos ao espaço: Módulo de combustível vazio liberado!", 5f, ScreenMessageStyle.UPPER_CENTER);
                    StageManager.ActivateNextStage();
                }
            }

            double timeToAp = telemetry.TimeToApoapsis;
            StatusMessage = $"Planando até o Apoastro ({timeToAp:F0}s restantes)...";

            if (!telemetry.IsInAtmosphere && timeToAp <= 15.0 && timeToAp > 0)
            {
                CurrentPhase = AscentPhase.Circularization;
                StatusMessage = "Iniciando queima de circularização orbital...";
                ScreenMessages.PostScreenMessage("[Artemis] Iniciando Queima de Circularização!", 4f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Fase 4: Queima prograde no apoastro até o periastro igualar a altitude alvo.
        /// </summary>
        private void HandleCircularization(TelemetryReader telemetry)
        {
            SetAttitude(telemetry, TargetHeading, 0.0, TargetRoll);

            double peError = TargetApoapsis - telemetry.Periapsis;

            if (peError <= 1000.0 || telemetry.Periapsis >= TargetApoapsis - 1500.0)
            {
                _commandedThrottle = 0f;
                CurrentPhase = AscentPhase.Complete;
                StatusMessage = $"Órbita Estável Concluída! Ap: {telemetry.Apoapsis/1000:F1}km | Pe: {telemetry.Periapsis/1000:F1}km";
                ScreenMessages.PostScreenMessage("[Artemis] Órbita Circular Alcançada com Sucesso!", 6f, ScreenMessageStyle.UPPER_CENTER);
                ReleaseAttitude(telemetry);
            }
            else if (peError < 5000.0)
            {
                _commandedThrottle = Mathf.Clamp((float)(peError / 5000.0), 0.1f, 1.0f);
                StatusMessage = $"Ajuste fino de órbita: Pe {telemetry.Periapsis/1000:F1}km";
            }
            else
            {
                _commandedThrottle = 1.0f;
                StatusMessage = $"Circularizando: Pe {telemetry.Periapsis/1000:F1}km / Alvo {TargetApoapsis/1000:F1}km";
            }
        }

        /// <summary>
        /// Comanda a atitude da nave utilizando o controlador do MechJeb.
        /// Desliga o SAS nativo do KSP para evitar conflitos de controle que travam a nave em pé.
        /// </summary>
        private void SetAttitude(TelemetryReader telemetry, double heading, double pitch, double roll)
        {
            if (telemetry.Vessel == null) return;

            // Desativa o SAS do jogo para permitir que o MechJeb incline livremente a nave
            if (telemetry.Vessel.ActionGroups[KSPActionGroup.SAS])
            {
                telemetry.Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            }

            if (telemetry.MechJeb != null)
            {
                var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeTo(heading, pitch, roll, this);
                }
            }
        }

        private void ReleaseAttitude(TelemetryReader telemetry)
        {
            if (telemetry.MechJeb != null)
            {
                var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
                if (attitude != null)
                {
                    attitude.attitudeDeactivate();
                }
            }

            if (telemetry.Vessel != null)
            {
                telemetry.Vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            }
        }
    }
}
