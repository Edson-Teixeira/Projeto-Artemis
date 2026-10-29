using System;
using UnityEngine;

namespace OraculoGuidance.Core
{
    /// <summary>
    /// Controlador Proporcional, Integral e Derivativo (PID) em C# de alta precisão.
    /// Migrado e otimizado diretamente de ControlePID.py do MechPeste.
    /// Possui anti-windup na integral e derivada calculada sobre a variável de processo (dInput).
    /// </summary>
    public class PIDController
    {
        public double Kp { get; private set; }
        public double Ki { get; private set; }
        public double Kd { get; private set; }

        public double MinOutput { get; private set; } = -1.0;
        public double MaxOutput { get; private set; } = 1.0;
        public double SampleTime { get; private set; } = 0.02; // 20ms padrão (50Hz - FixedUpdate do Unity)

        public double Setpoint { get; set; } = 0.0;
        public double Input { get; set; } = 0.0;
        public double Output { get; private set; } = 0.0;

        private double _lastInput = 0.0;
        private double _integralTerm = 0.0;
        private double _lastCalculationTime = 0.0;

        public PIDController(double kp = 0.025, double ki = 0.001, double kd = 1.0, double min = -1.0, double max = 1.0, double sampleTime = 0.02)
        {
            SetGains(kp, ki, kd);
            SetOutputLimits(min, max);
            SampleTime = sampleTime;
        }

        public void SetGains(double kp, double ki, double kd)
        {
            Kp = kp >= 0 ? kp : 0.025;
            Ki = ki >= 0 ? ki : 0.001;
            Kd = kd >= 0 ? kd : 1.0;
        }

        public void SetOutputLimits(double min, double max)
        {
            if (min > max) return;
            MinOutput = min;
            MaxOutput = max;

            _integralTerm = Math.Max(MinOutput, Math.Min(MaxOutput, _integralTerm));
            Output = Math.Max(MinOutput, Math.Min(MaxOutput, Output));
        }

        public void SetSampleTimeSeconds(double seconds)
        {
            if (seconds > 0)
            {
                SampleTime = seconds;
            }
        }

        /// <summary>
        /// Reseta os termos acumulados do controlador.
        /// </summary>
        public void Reset()
        {
            _integralTerm = 0.0;
            _lastInput = Input;
            _lastCalculationTime = Planetarium.GetUniversalTime();
            Output = 0.0;
        }

        /// <summary>
        /// Computa o novo valor de saída do PID.
        /// </summary>
        public double Compute(double currentInput)
        {
            Input = currentInput;
            double now = Planetarium.GetUniversalTime();
            double dt = now - _lastCalculationTime;

            if (dt >= SampleTime)
            {
                // Erro atual
                double error = Setpoint - Input;

                // Termo Integral com proteção anti-windup (clamp)
                _integralTerm += Ki * error * dt;
                _integralTerm = Math.Max(MinOutput, Math.Min(MaxOutput, _integralTerm));

                // Derivada sobre a entrada (evita derivative kick quando o setpoint muda)
                double dInput = dt > 0 ? (Input - _lastInput) / dt : 0.0;

                // Cálculo da saída
                Output = (Kp * error) + _integralTerm - (Kd * dInput);

                // Limitador de saturação de saída
                Output = Math.Max(MinOutput, Math.Min(MaxOutput, Output));

                _lastInput = Input;
                _lastCalculationTime = now;
            }

            return Output;
        }

        /// <summary>
        /// Atalho para computar quando o setpoint e input mudam simultaneamente.
        /// </summary>
        public double Compute(double currentInput, double currentSetpoint)
        {
            Setpoint = currentSetpoint;
            return Compute(currentInput);
        }
    }
}
