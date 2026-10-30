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
            StatusMessage = "Iniciando subida vertical...";
            ScreenMessages.PostScreenMessage($"[Artemis] Lançamento iniciado! Alvo: {TargetApoapsis / 1000:F0}km | Azimute: {TargetHeading:F0}°", 5f, ScreenMessageStyle.UPPER_CENTER);
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
        /// Fase 1: Sobe reto apenas para livrar a torre de lançamento (~500m ou 50m/s).
        /// </summary>
        private void HandleVerticalClimb(TelemetryReader telemetry)
        {
            _commandedThrottle = 1.0f;
            SetAttitude(telemetry, TargetHeading, 90.0, TargetRoll);

            StatusMessage = "Subida Vertical: Livrando a plataforma...";

            // Inicia a curva bem cedo para não subir "em pé"
            if (telemetry.AltitudeASL >= 500.0 || telemetry.SurfaceSpeed >= 50.0)
            {
                CurrentPhase = AscentPhase.GravityTurn;
                StatusMessage = "Iniciando Curva Gravitacional...";
            }
        }

        /// <summary>
        /// Fase 2: Inclina progressivamente em direção ao horizonte.
        /// Aos 1.000m: pitch ~84°
        /// Aos 5.000m: pitch ~67°
        /// Aos 10.000m: pitch ~54°
        /// Aos 20.000m: pitch ~35°
        /// Aos 35.000m: pitch ~15°
        /// Acima de 45.000m: pitch ~0° (horizontal)
        /// </summary>
        private void HandleGravityTurn(TelemetryReader telemetry)
        {
            double alt = telemetry.AltitudeASL;
            double turnStart = 500.0;
            double turnEnd = 45000.0;

            double linear = Mathf.Clamp01((float)((alt - turnStart) / (turnEnd - turnStart)));
            double progress = Math.Pow(linear, 0.6);
            double pitch = 90.0 - (progress * 90.0);
            pitch = Math.Max(0.0, pitch);

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
                StatusMessage = $"Curva Gravitacional: Pitch {pitch:F0}° | Ap {telemetry.Apoapsis/1000:F1}km";
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
