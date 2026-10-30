using System;
using UnityEngine;
using MuMech;

namespace Artemis.Telemetry
{
    /// <summary>
    /// TelemetryReader: Responsável por ler e simplificar os dados de voo da nave ativa.
    /// Combina a API nativa do KSP com o MechJeb2 (que já calcula empuxo e massas).
    /// Pense nele como o módulo de "sensores" do nosso foguete.
    /// </summary>
    public class TelemetryReader
    {
        private Vessel _vessel;
        private MechJebCore _mechJebCore;

        /// <summary>
        /// Atualiza a referência da nave ativa e localiza o núcleo do MechJeb.
        /// </summary>
        public void UpdateVessel(Vessel vessel)
        {
            _vessel = vessel;
            if (_vessel != null)
            {
                // Busca o MechJeb na nave ou adiciona na peça raiz caso o foguete não tenha a pecinha AR201
                _mechJebCore = _vessel.GetMasterMechJeb();
                if (_mechJebCore == null && _vessel.rootPart != null)
                {
                    _mechJebCore = _vessel.rootPart.AddModule("MechJebCore") as MechJebCore;
                }
            }
            else
            {
                _mechJebCore = null;
            }
        }

        public MechJebCore MechJeb => _mechJebCore;
        public Vessel Vessel => _vessel;

        // --- DADOS ORBITAIS BÁSICOS ---
        public double AltitudeASL => _vessel != null ? _vessel.altitude : 0;
        public double Apoapsis => _vessel != null ? _vessel.orbit.ApA : 0;
        public double Periapsis => _vessel != null ? _vessel.orbit.PeA : 0;
        public double TimeToApoapsis => _vessel != null ? _vessel.orbit.timeToAp : 0;
        public double VerticalSpeed => _vessel != null ? _vessel.verticalSpeed : 0;
        public double SurfaceSpeed => _vessel != null ? _vessel.srfSpeed : 0;

        // --- ATMOSFERA ---
        public double AtmosphereDepth => (_vessel != null && _vessel.mainBody != null && _vessel.mainBody.atmosphere) 
            ? _vessel.mainBody.atmosphereDepth 
            : 0;

        public bool IsInAtmosphere => _vessel != null && _vessel.mainBody != null && _vessel.mainBody.atmosphere && _vessel.altitude < AtmosphereDepth;

        // --- MOTORES E EMPUXO ---

        /// <summary>
        /// Empuxo atual total sendo gerado pelos motores ativos (kN).
        /// </summary>
        public float CurrentThrust
        {
            get
            {
                if (_vessel == null) return 0f;
                float thrust = 0f;
                var engines = _vessel.FindPartModulesImplementing<ModuleEngines>();
                for (int i = 0; i < engines.Count; i++)
                {
                    var eng = engines[i];
                    if (eng.isOperational && eng.EngineIgnited)
                    {
                        thrust += eng.GetCurrentThrust();
                    }
                }
                return thrust;
            }
        }

        /// <summary>
        /// Detecta se algum dos motores ativos sofreu "flameout" (apagou por falta de combustível).
        /// </summary>
        public bool HasFlameout
        {
            get
            {
                if (_vessel == null) return false;
                var engines = _vessel.FindPartModulesImplementing<ModuleEngines>();
                int activeCount = 0;
                int flameoutCount = 0;

                for (int i = 0; i < engines.Count; i++)
                {
                    var eng = engines[i];
                    if (eng.isOperational && eng.EngineIgnited)
                    {
                        activeCount++;
                        if (eng.flameout)
                        {
                            flameoutCount++;
                        }
                    }
                }

                // Se temos motores ativos e ao menos um deles apagou
                return activeCount > 0 && flameoutCount > 0;
            }
        }

        /// <summary>
        /// Converte um vetor 3D qualquer (como velocidade ou retrógrado) em Heading (0-360) e Pitch (-90 a +90)
        /// relativo à superfície do planeta em que a nave se encontra.
        /// </summary>
        public static void VectorToHeadingPitch(Vessel vessel, Vector3d direction, out double heading, out double pitch)
        {
            if (vessel == null || direction.sqrMagnitude < 0.0001)
            {
                heading = 90.0;
                pitch = 90.0;
                return;
            }

            Vector3d up = vessel.upAxis;
            Vector3d north = Vector3d.Exclude(up, vessel.mainBody.transform.up).normalized;
            Vector3d east = Vector3d.Cross(up, north).normalized;

            pitch = 90.0 - Vector3d.Angle(up, direction);

            Vector3d horiz = Vector3d.Exclude(up, direction).normalized;
            heading = Vector3d.Angle(north, horiz);
            if (Vector3d.Dot(east, horiz) < 0)
            {
                heading = 360.0 - heading;
            }
        }

        /// <summary>
        /// Verifica se o estágio/módulo de propulsão atual está vazio de combustível.
        /// Avalia se os motores ativos sofreram flameout ou se os propelentes (LiquidFuel, Oxidizer, SolidFuel) acabaram.
        /// </summary>
        public static bool IsCurrentFuelEmpty(Vessel vessel)
        {
            if (vessel == null) return false;

            var engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            int stagedEngines = 0;
            int emptyEngines = 0;

            for (int i = 0; i < engines.Count; i++)
            {
                var eng = engines[i];
                if (eng.isOperational && (eng.EngineIgnited || eng.staged))
                {
                    stagedEngines++;

                    if (eng.flameout)
                    {
                        emptyEngines++;
                        continue;
                    }

                    // Avalia se há propelente disponível no motor
                    bool hasFuel = false;
                    if (eng.propellants != null && eng.propellants.Count > 0)
                    {
                        for (int p = 0; p < eng.propellants.Count; p++)
                        {
                            var prop = eng.propellants[p];
                            if (!prop.isDeprived && prop.currentAmount > 0.5)
                            {
                                hasFuel = true;
                                break;
                            }
                        }
                    }

                    if (!hasFuel)
                    {
                        emptyEngines++;
                    }
                }
            }

            return stagedEngines > 0 && emptyEngines >= stagedEngines;
        }
    }
}
