using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace Sintef.Pgo.Core
{


	/// <summary>
	/// Converts numerical values between V, A, VA, Ohms, etc. to/from per-unit 
	/// values.
	/// Valid for a certain (grid) voltage level, as given in the contructor.
	/// Valid for 1-phase or 3-phase systems, depending on flag in the constructor
	/// </summary>
	public class PowerUnitConverter
	{		
		
		#region Public properties 

		/// <summary>
		/// True if the converter is for 3-phase systems, false if it is for 1-phase.
		/// </summary>
		public Phases Phases { get; private set; }

		/// <summary>
		/// The voltage base that is used.
		/// </summary>
		public double VoltageBase => _vBase;

		/// <summary>
		/// The power base.
		/// </summary>
		public double PowerBase => _powerBase;

		#endregion

		#region Private data members

		/// <summary>
		/// Power base (in VA)
		/// </summary>
		double _powerBase;

		/// <summary>
		/// The voltage base (in V)
		/// </summary>
		double _vBase;

		/// <summary>
		/// The base impedance (in Ohms)
		/// </summary>
		double _impBase;

		/// <summary>
		/// The current base (in A)
		/// </summary>
		double _currentBase;

		/// <summary>
		/// Admittance base (1/Ohms)
		/// </summary>
		double _admittanceBase;

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="voltageBase">The voltage base in V (the level of voltage expected in the part of the grid that the converter is used for.</param>
		/// <param name="powerBase">The power base in VA</param>
		/// <param name="threePhase">True if the converter is for 3-phase systems, false if it is for 1-phase.</param>
		public PowerUnitConverter(double voltageBase, double powerBase, Phases threePhase)
		{
			_vBase = voltageBase;
			_powerBase = powerBase;

			Phases = threePhase;
			switch (Phases)
			{
				case Phases.OnePhase:
					_currentBase = _powerBase / _vBase;
					break;
				case Phases.ThreePhase:
					_currentBase = _powerBase / (_vBase * Math.Sqrt(3));
					break;
			}
			_impBase = _vBase * _vBase / _powerBase;
			_admittanceBase = 1 / _impBase;
		}

		#endregion

		#region Public methods

		#region Converting to per-unit

		/// <summary>
		/// Converts the given <paramref name="impedance"/> (in Ohms) to a per unit value.
		/// </summary>
		/// <param name="impedance"></param>
		/// <returns></returns>
		public Complex PerUnitImpedance(Complex impedance) =>  impedance / _impBase;

		/// <summary>
		/// Converts the given <paramref name="resistance"/> (in Ohms) to a per unit value.
		/// </summary>
		/// <param name="resistance"></param>
		/// <returns></returns>
		public double PerUnitResistance(double resistance) => resistance / _impBase;

		/// <summary>
		/// Converts the given <paramref name="reactance"/> (in Ohms) to a per unit value.
		/// </summary>
		/// <param name="reactance"></param>
		/// <returns></returns>
		public double PerUnitReactance(double reactance) => reactance / _impBase;


		/// <summary>
		/// Converts the given <paramref name="voltage"/> amplitude (in V) to a per unit value.
		/// </summary>
		/// <param name="voltage"></param>
		/// <returns></returns>
		public double PerUnitVoltage(double voltage) => voltage / _vBase;

		/// <summary>
		/// Converts the given complex <paramref name="voltage"/> (in V) to per unit values.
		/// </summary>
		/// <param name="voltage"></param>
		/// <returns></returns>
		public Complex PerUnitVoltage(Complex voltage) => voltage / _vBase;

		/// <summary>
		/// Converts the given <paramref name="current"/> amplitude (in A) to a per unit value.
		/// </summary>
		/// <param name="current"></param>
		/// <returns></returns>
		public double PerUnitCurrent(double current) => current / _currentBase;

		/// <summary>
		/// Converts the given complex <paramref name="current"/> (in A) to per unit values.
		/// </summary>
		/// <param name="current"></param>
		/// <returns></returns>
		public Complex PerUnitCurrent(Complex current) => current / _currentBase;

		/// <summary>
		/// Conversion of power (in VA) to per unit value. 
		/// </summary>
		/// <param name="power">the power, as a complex number.</param>
		/// <returns></returns>
		public Complex PerUnitPower(Complex power) => power / _powerBase;

		/// <summary>
		/// Conversion of power (real or reactive, in VA) to per unit value. 
		/// </summary>
		/// <param name="power"></param>
		/// <returns></returns>
		public double PerUnitPower(double power) => power / _powerBase;

        /// <summary>
        /// Conversion of admittance to per unit value.
        /// </summary>
        /// <param name="admittance"></param>
        /// <returns></returns>
        public double PerUnitAdmittance(double admittance) => admittance / _admittanceBase;
		#endregion

		#region Converting from per-unit

		/// <summary>
		/// Converts the given per-unit <paramref name="perUnitImpedance"/> to an actual value (in Ohms).
		/// </summary>
		/// <param name="perUnitImpedance"></param>
		/// <returns></returns>
		public Complex Impetance(Complex perUnitImpedance) => perUnitImpedance * _impBase;

		/// <summary>
		/// Converts the given per-unit <paramref name="perUnitResistance"/>to an actual value (in Ohms) .
		/// </summary>
		/// <param name="perUnitResistance"></param>
		/// <returns></returns>
		public double Resistance(double perUnitResistance) => perUnitResistance * _impBase;

		/// <summary>
		/// Converts the given  per-unit <paramref name="perUnitReactance"/>  to an actual value (in Ohms).
		/// </summary>
		/// <param name="perUnitReactance"></param>
		/// <returns></returns>
		public double Reactance(double perUnitReactance) => perUnitReactance * _impBase;


		/// <summary>
		/// Converts the given  per-unit <paramref name="perUnitVoltage"/> amplitude to an actual value (in V) .
		/// </summary>
		/// <param name="perUnitVoltage"></param>
		/// <returns></returns>
		public double Voltage(double perUnitVoltage) => perUnitVoltage * _vBase;

		/// <summary>
		/// Converts the given  per-unit  complex <paramref name="perUnitVoltage"/> to per unit values(in V) .
		/// </summary>
		/// <param name="perUnitVoltage"></param>
		/// <returns></returns>
		public Complex Voltage(Complex perUnitVoltage) => perUnitVoltage * _vBase;

		/// <summary>
		/// Converts the given  per-unit <paramref name="perUnitCurrent"/> amplitude to an actual value (in A).
		/// </summary>
		/// <param name="perUnitCurrent"></param>
		/// <returns></returns>
		public double Current(double perUnitCurrent) => perUnitCurrent * _currentBase;

		/// <summary>
		/// Converts the given  per-unit  complex <paramref name="perUnitCurrent"/> to per unit values (in A).
		/// </summary>
		/// <param name="perUnitCurrent"></param>
		/// <returns></returns>
		public Complex Current(Complex perUnitCurrent) => perUnitCurrent * _currentBase;

		//		double PerUnitSusceptance(double susceptance) => susceptance / _susceptanceBase;

		/// <summary>
		/// Conversion of per-unit  power to per unit value (in VA). 
		/// </summary>
		/// <param name="perUnitPower">the poser, as a complex number.</param>
		/// <returns></returns>
		public Complex Power(Complex perUnitPower) => perUnitPower * _powerBase;

		/// <summary>
		/// Conversion of per-unit  power (real or reactive) to per unit value in VA. 
		/// </summary>
		/// <param name="perUnitPower"></param>
		/// <returns></returns>
		public double Power(double perUnitPower) => perUnitPower * _powerBase;


		#endregion

		#endregion
			 	 

		#region Private methods

		#endregion
	}
}
