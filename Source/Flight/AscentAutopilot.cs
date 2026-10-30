using System;
using UnityEngine;
using Artemis.Telemetry;
using Artemis.Systems;
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
        GravityTurn,        // Curva gravitacional com Pitch progressivo e Roll de 90°
        CoastingToSpace,    // Motores desligados, subindo por inércia até o vácuo
        Circularization,    // Queima em prograde no apoastro para fechar a órbita
        Complete            // Órbita estável alcançada com sucesso
    }

    /// <summary>
    /// AscentAutopilot: Controla o voo autônomo desde o chão até a órbita completa.
    /// Executa:
    /// 1. Subida vertical e giro para 90° de Roll (janelas voltadas para cima).
    /// 2. Curva em direção ao azimute escolhido com inclinação agressiva para ganho de velocidade orbital.
    /// 3. Corte exato no Apoastro alvo.
    /// 4. Queima de circularização no Apoastro.
    /// </summary>
    public class AscentAutopilot
    {
        // Parâmetros configuráveis pelo usuário
        public double TargetApoapsis = 80000.0;     // 80 km padrão
        public double TargetHeading = 90.0;         // 90° = Leste
        public double TargetRoll = 90.0;            // 90° = Janelas para cima

        public AscentPhase CurrentPhase { get; private set; } = AscentPhase.Idle;
        public bool IsActive => CurrentPhase != AscentPhase.Idle && CurrentPhase != AscentPhase.Complete;
        public string StatusMessage { get; private set; } = "Pronto para decolar";

        private float _commandedThrottle = 0f;
        public float CommandedThrottle => _commandedThrottle;

        public void StartAscent(double targetAp, double heading)
        {
            TargetApoapsis = Math.Max(72000.0, targetAp);
            TargetHeading = (heading % 360.0 + 360.0) % 360.0;
            CurrentPhase = AscentPhase.VerticalClimb;
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
        /// Fase 1: Sobe reto até ganhar altitude de controle (~1.000m) e velocidade (~70m/s).
        /// </summary>
        private void HandleVerticalClimb(TelemetryReader telemetry)
        {
            _commandedThrottle = 1.0f;
            SetAttitude(telemetry, TargetHeading, 90.0, TargetRoll); // Pitch 90° (para cima), Roll 90°

            StatusMessage = "Subida Vertical: Ganhando velocidade...";

            if (telemetry.AltitudeASL >= 1000.0 || telemetry.SurfaceSpeed >= 70.0)
            {
                CurrentPhase = AscentPhase.GravityTurn;
                StatusMessage = "Iniciando Curva Gravitacional...";
            }
        }

        /// <summary>
        /// Fase 2: Inclina progressivamente com curva otimizada de potência.
        /// Atinge ~50° de pitch a 10km e ~25° a 25km, acelerando forte horizontalmente.
        /// Mantém sempre o Roll travado em 90°.
        /// </summary>
        private void HandleGravityTurn(TelemetryReader telemetry)
        {
            double alt = telemetry.AltitudeASL;
            double turnStart = 1000.0;
            double turnEnd = 48000.0;

            // Fração linear de subida de 0.0 a 1.0
            double linearFraction = Mathf.Clamp01((float)((alt - turnStart) / (turnEnd - turnStart)));
            
            // Curva de potência (expoente 0.55): curva mais cedo e mais pronunciada (aumentando a inclinação)
            double progress = Math.Pow(linearFraction, 0.55);

            // Inclina o Pitch de 90° (vertical) para 5° (quase horizontal)
            double pitch = 90.0 - (progress * 85.0);

            SetAttitude(telemetry, TargetHeading, pitch, TargetRoll);

            // Controle do acelerador: desacelera suavemente perto do apoastro alvo para não ultrapassar
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
                // Estrangula o motor suavemente perto do corte
                _commandedThrottle = Mathf.Clamp01((float)(apError / 3000.0));
                StatusMessage = $"Aproximando do corte do motor: Ap {telemetry.Apoapsis/1000:F1}km";
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

            // Mantém a nave apontada na direção horizontal (prograde) preparando a queima
            SetAttitude(telemetry, TargetHeading, 0.0, TargetRoll);

            double timeToAp = telemetry.TimeToApoapsis;
            StatusMessage = $"Planando até o Apoastro ({timeToAp:F0}s restantes)...";

            // Inicia a queima quando estiver a menos de 15 segundos do Apoastro e fora da atmosfera
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
                // Aceleração fina no final
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
        /// Comanda a atitude da nave utilizando o controlador nativo do MechJeb.
        /// </summary>
        private void SetAttitude(TelemetryReader telemetry, double heading, double pitch, double roll)
        {
            if (telemetry.MechJeb == null) return;
            var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
            if (attitude != null)
            {
                attitude.attitudeTo(heading, pitch, roll, this);
            }
        }

        private void ReleaseAttitude(TelemetryReader telemetry)
        {
            if (telemetry.MechJeb == null) return;
            var attitude = telemetry.MechJeb.GetComputerModule<MechJebModuleAttitudeController>();
            if (attitude != null)
            {
                attitude.attitudeDeactivate();
            }
        }
    }
}
