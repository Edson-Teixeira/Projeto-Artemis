using System;
using UnityEngine;

namespace OraculoGuidance.Core
{
    /// <summary>
    /// Biblioteca matemática, vetorial e de navegação de alta precisão para o KSP.
    /// Migrada e aprimorada a partir de Vetor.py e Navegacao.py do MechPeste.
    /// Resolve todas as conversões de coordenadas, cálculo de atitude e empuxo real de motores.
    /// </summary>
    public static class NavigationMath
    {
        public const double StandardGravity = 9.80665;

        #region Cálculo de Empuxo e TWR Real

        /// <summary>
        /// Retorna o empuxo MÁXIMO disponível real de todos os motores operacionais da nave.
        /// Corrige definitivamente o bug de GetCurrentThrust() == 0 quando os motores estão em idle.
        /// </summary>
        public static double GetAvailableThrust(Vessel vessel)
        {
            if (vessel == null) return 0.0;

            double totalThrust = 0.0;
            for (int i = 0; i < vessel.Parts.Count; i++)
            {
                var part = vessel.Parts[i];
                for (int j = 0; j < part.Modules.Count; j++)
                {
                    if (part.Modules[j] is ModuleEngines engine && engine.isOperational && !engine.flameout)
                    {
                        totalThrust += engine.maxThrust;
                    }
                }
            }
            return totalThrust;
        }

        /// <summary>
        /// Calcula a média harmônica ponderada do Impulso Específico (Isp) de todos os motores ativos.
        /// </summary>
        public static double GetAverageIsp(Vessel vessel)
        {
            if (vessel == null) return 300.0;

            double totalThrust = 0.0;
            double flowRateSum = 0.0;

            for (int i = 0; i < vessel.Parts.Count; i++)
            {
                var part = vessel.Parts[i];
                for (int j = 0; j < part.Modules.Count; j++)
                {
                    if (part.Modules[j] is ModuleEngines engine && engine.isOperational && !engine.flameout)
                    {
                        double thrust = engine.maxThrust;
                        double isp = engine.realIsp > 0 ? engine.realIsp : 300.0;
                        totalThrust += thrust;
                        flowRateSum += thrust / (isp * StandardGravity);
                    }
                }
            }

            if (flowRateSum <= 0 || totalThrust <= 0) return 300.0;
            return totalThrust / (flowRateSum * StandardGravity);
        }

        /// <summary>
        /// Calcula a relação empuxo-peso (TWR) máxima disponível para a nave na gravidade local.
        /// </summary>
        public static double GetMaxTWR(Vessel vessel)
        {
            if (vessel == null) return 0.0;

            double mass = vessel.GetTotalMass();
            if (mass <= 0) return 0.0;

            double localGravity = vessel.mainBody.GeeASL * StandardGravity;
            double availableThrust = GetAvailableThrust(vessel);

            return availableThrust / (mass * localGravity);
        }

        #endregion

        #region Vetores de Superfície e Atitude

        /// <summary>
        /// Converte ângulos de Inclinação (Pitch: 0 a 90°) e Rumo (Heading: 0 a 360°) em vetor unitário 3D mundial.
        /// Pitch = 90° aponta para o zênite (cima).
        /// Heading = 90° aponta para o Leste (East).
        /// </summary>
        public static Vector3d GetHeadingAndPitchDirection(Vessel vessel, double pitchDeg, double headingDeg)
        {
            if (vessel == null) return Vector3d.up;

            Vector3d up = vessel.upAxis; // Vetor para cima da superfície
            Vector3d north = Vector3d.Exclude(up, vessel.mainBody.transform.up).normalized;
            Vector3d east = Vector3d.Cross(up, north).normalized;

            double headRad = headingDeg * Mathf.Deg2Rad;
            double pitchRad = pitchDeg * Mathf.Deg2Rad;

            Vector3d horizontal = (north * Math.Cos(headRad) + east * Math.Sin(headRad)).normalized;
            Vector3d direction = (horizontal * Math.Cos(pitchRad) + up * Math.Sin(pitchRad)).normalized;

            return direction;
        }

        /// <summary>
        /// Retorna o vetor retrógrado relativo à velocidade de superfície.
        /// </summary>
        public static Vector3d GetSurfaceRetrograde(Vessel vessel)
        {
            if (vessel == null) return -Vector3d.up;

            Vector3d srfVel = vessel.srf_velocity;
            if (srfVel.sqrMagnitude < 0.04) // Se estiver quase parado, aponta para cima
            {
                return vessel.upAxis;
            }

            return -srfVel.normalized;
        }

        /// <summary>
        /// Converte um vetor desejado no espaço mundial para comandos de controle local (Pitch, Yaw, Roll).
        /// Aplica amortecimento para controle suave sem oscilações violentas.
        /// </summary>
        public static void CalculateSteering(Vessel vessel, Vector3d targetDirection, ref float pitchCmd, ref float yawCmd, double sensitivity = 2.5)
        {
            if (vessel == null) return;

            Transform refTransform = vessel.ReferenceTransform;
            Vector3 localTarget = refTransform.InverseTransformDirection(targetDirection.normalized);

            // No KSP, localTarget.y é para a frente da nave
            // localTarget.x é guinada (Yaw)
            // localTarget.z é arfagem (Pitch)
            float rawPitch = (float)(localTarget.z * sensitivity);
            float rawYaw = (float)(localTarget.x * sensitivity);

            pitchCmd = Mathf.Clamp(rawPitch, -1.0f, 1.0f);
            yawCmd = Mathf.Clamp(rawYaw, -1.0f, 1.0f);
        }

        #endregion

        #region Mecânica Orbital

        /// <summary>
        /// Calcula o Delta-V necessário para circularizar a órbita no Apoastro via Equação Vis-Viva.
        /// deltaV = sqrt(mu / r_ap) - sqrt(mu * (2/r_ap - 1/sma))
        /// </summary>
        public static double CalculateCircularizationDeltaV(Vessel vessel)
        {
            if (vessel == null || vessel.orbit == null) return 0.0;

            double mu = vessel.mainBody.gravParameter;
            double rAp = vessel.orbit.ApR;
            double sma = vessel.orbit.semiMajorAxis;

            if (rAp <= 0 || sma <= 0) return 0.0;

            double vApoapsisCurrent = Math.Sqrt(mu * ((2.0 / rAp) - (1.0 / sma)));
            double vCircular = Math.Sqrt(mu / rAp);

            return Math.Max(0.0, vCircular - vApoapsisCurrent);
        }

        /// <summary>
        /// Calcula o tempo de queima de um nó de manobra via Equação do Foguete de Tsiolkovsky.
        /// </summary>
        public static double CalculateBurnTime(Vessel vessel, double deltaV)
        {
            if (vessel == null || deltaV <= 0.0) return 0.0;

            double thrust = GetAvailableThrust(vessel);
            if (thrust <= 0.0) return 0.0;

            double isp = GetAverageIsp(vessel);
            double effectiveExhaustVelocity = isp * StandardGravity;
            double totalMass = vessel.GetTotalMass();

            double dryMass = totalMass / Math.Exp(deltaV / effectiveExhaustVelocity);
            double fuelFlowRate = thrust / effectiveExhaustVelocity;

            if (fuelFlowRate <= 0.0) return 0.0;

            return (totalMass - dryMass) / fuelFlowRate;
        }

        #endregion
    }
}
