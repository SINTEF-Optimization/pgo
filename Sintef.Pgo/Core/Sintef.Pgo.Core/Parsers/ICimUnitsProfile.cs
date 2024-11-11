using System;
using UnitsNet;
using Sintef.Pgo.DataContracts;

using Voltage = UnitsNet.ElectricPotential;
using Resistance = UnitsNet.ElectricResistance;
using Conductance = UnitsNet.ElectricConductance;
using CurrentFlow = UnitsNet.ElectricCurrent;
using ActivePower = UnitsNet.Power;
using ReactivePower = UnitsNet.ReactivePower;

namespace Sintef.Pgo.Core.IO
{
	/// <summary>
	/// Interface for classes that handle assigning units to physical numeric values.
	/// 
	/// CIM JSON-LD does not (as far as we can see) specify the units to use for physical
	/// numeric values (e.g. W, kW or MW?). A class implementing this interface is responsible for knowing what units to use.
	/// </summary>
	public interface ICimUnitsProfile
	{
		/// <summary>
		/// Returns true if this class knows how to convert a double into the
		/// given electical type
		/// </summary>
		bool Handles(Type electricalType);

		/// <summary>
		/// Converts the given double value into the given electrical type
		/// </summary>
		object AssignUnit(double value, Type electricalType);
	}

	/// <summary>
	/// Units for the DIGIN dataset.
	/// 
	/// These units are documented in the Digin document ModellingGuide:
	/// 
	/// |Length | m
	/// |Power | MW
	/// |Reactive Power | MVAr
	/// |Voltage | kV
	/// |Current | A
	/// 
	/// The following was not documented, but assumed by us based on the values found in the data:
	/// 
	/// |Resistance | Ohms
	/// |Conductance | Siemens
	/// |CurrentFlow | Ampere 
	/// 
	/// </summary>
	public class DiginUnitsProfile : ICimUnitsProfile
	{
		/// <summary>
		/// Returns true if this class knows how to convert a double into the
		/// given electical type
		/// </summary>
		public bool Handles(Type electricalType)
		{
			return electricalType == typeof(Voltage)
				|| electricalType == typeof(Resistance)
				|| electricalType == typeof(Conductance)
				|| electricalType == typeof(CurrentFlow)
				|| electricalType == typeof(ActivePower)
				|| electricalType == typeof(ReactivePower)
				|| electricalType == typeof(Length);
		}

		/// <summary>
		/// Converts the given double into the given electrical type
		/// </summary>
		public object AssignUnit(double value, Type electricalType)
		{
			if (electricalType == typeof(Voltage))
				return Voltage.FromKilovolts(value);

			if (electricalType == typeof(Resistance))
				return Resistance.FromOhms(value);

			if (electricalType == typeof(Conductance))
				return Conductance.FromSiemens(value);

			if (electricalType == typeof(CurrentFlow))
				return CurrentFlow.FromAmperes(value);

			if (electricalType == typeof(ActivePower))
				return ActivePower.FromMegawatts(value);

			if (electricalType == typeof(ReactivePower))
				return ReactivePower.FromMegavoltamperesReactive(value);

			if (electricalType == typeof(Length))
				return Length.FromMeters(value);

			throw new Exception($"Don't know the unit for type {electricalType}");
		}
	}

	/// <summary>
	/// Uses SI units for all values.
	/// </summary>
	public class SiUnitsProfile : ICimUnitsProfile
	{
		/// <summary>
		/// Returns true if this class knows how to convert a double into the
		/// given electical type
		/// </summary>
		public bool Handles(Type electricalType)
		{
			return electricalType == typeof(Voltage)
				|| electricalType == typeof(Resistance)
				|| electricalType == typeof(Conductance)
				|| electricalType == typeof(CurrentFlow)
				|| electricalType == typeof(ActivePower)
				|| electricalType == typeof(ReactivePower)
				|| electricalType == typeof(Length);
		}

		/// <summary>
		/// Converts the given double value into the given electrical type, asssuming it 
		/// is given in the relevant SI unit
		/// </summary>
		public object AssignUnit(double value, Type electricalType)
		{
			if (electricalType == typeof(Voltage))
				return Voltage.FromVolts(value);

			if (electricalType == typeof(Resistance))
				return Resistance.FromOhms(value);

			if (electricalType == typeof(Conductance))
				return Conductance.FromSiemens(value);

			if (electricalType == typeof(CurrentFlow))
				return CurrentFlow.FromAmperes(value);

			if (electricalType == typeof(ActivePower))
				return ActivePower.FromWatts(value);

			if (electricalType == typeof(ReactivePower))
				return ReactivePower.FromVoltamperesReactive(value);

			if (electricalType == typeof(Length))
				return Length.FromMeters(value);

			throw new Exception($"Don't know the unit for type {electricalType}");
		}
	}

	/// <summary>
	/// Factory class for <see cref="ICimUnitsProfile"/>
	/// </summary>
	public class CimUnitProfileFactory
	{
		/// <summary>
		/// Creates and returns a <see cref="ICimUnitsProfile"/> based on the given parameters
		/// </summary>
		public static ICimUnitsProfile CreateProfile(CimUnitsProfile? unitsProfile)
		{
			return unitsProfile switch
			{
				CimUnitsProfile.Digin => new DiginUnitsProfile(),
				CimUnitsProfile.Si => new SiUnitsProfile(),
				_ => throw new Exception("Unknown units profile")
			};
		}
	}
}
