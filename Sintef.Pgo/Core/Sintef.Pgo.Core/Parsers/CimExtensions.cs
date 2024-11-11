using Sintef.Pgo.Cim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Voltage = UnitsNet.ElectricPotential;

namespace Sintef.Pgo.Core.IO
{
	/// <summary>
	/// Extension methods for CIM model classes
	/// </summary>
	public static class CimExtensions
	{
		/// <summary>
		/// Returns the base voltage for the given equipment, or throws an exception if none could be found
		/// </summary>
		public static Voltage GetBaseVoltage(this ConductingEquipment equipment)
		{
			var baseVoltage = BaseVoltage(equipment);

			return BaseVoltage(equipment).RequireValue(b => b.NominalVoltage, nameof(baseVoltage.NominalVoltage));
		}

		/// <summary>
		/// Returns the base voltage for the given equipment, or throws an exception if none could be found
		/// </summary>
		private static BaseVoltage BaseVoltage(ConductingEquipment equipment)
		{
			if (equipment.BaseVoltage != null)
				return equipment.BaseVoltage;

			VoltageLevel vl = equipment.EquipmentContainer switch
			{
				Bay bay => bay.RequireValue(b => b.VoltageLevel, nameof(bay.VoltageLevel)),
				VoltageLevel v => v,
				null => throw new ArgumentException($"Cannot determine base voltage: {equipment.Describe()} has no EquipmentContainer"),
				_ => throw new ArgumentException($"Cannot determine base voltage: {equipment.Describe()} has unsupported EquipmentContainer type"),
			}; ;

			return vl.RequireValue(v => v.BaseVoltage, nameof(vl.BaseVoltage));
		}

		/// <summary>
		/// Extracts a value from the given object using the given accessor. If the value is null, throws an
		/// exception, otherwise returns the value.
		/// </summary>
		/// <param name="theObject">The object that contains the value</param>
		/// <param name="accessor">The accessor that extracts the value</param>
		/// <param name="attributeName">The name of the attribute that contains the value</param>
		public static TValue RequireValue<TObject, TValue>(this TObject theObject, Func<TObject, TValue> accessor, string attributeName)
			where TObject : IdentifiedObject
			where TValue : class
		{
			TValue value = accessor(theObject);

			if (value is not null)
				return value;

			if (typeof(TValue).IsSubclassOf(typeof(IdentifiedObject)))
				throw new ArgumentException($"Missing associated '{attributeName}' for {theObject.Describe()}");

			string camelCaseAttributeName = $"{char.ToLower(attributeName[0])}{attributeName.Substring(1)}";

			throw new ArgumentException($"Missing attribute '{camelCaseAttributeName}' on {theObject.Describe()}");
		}

		/// <summary>
		/// Extracts a value from the given object using the given accessor. If the value is null, throws an
		/// exception, otherwise returns the value.
		/// </summary>
		/// <param name="theObject">The object that contains the value</param>
		/// <param name="accessor">The accessor that extracts the value</param>
		/// <param name="attributeName">The name of the attribute that contains the value</param>
		public static TValue RequireValue<TObject, TValue>(this TObject theObject, Func<TObject, TValue?> accessor, string attributeName)
			where TObject : IdentifiedObject
			where TValue : struct
		{
			TValue? maybeValue = accessor(theObject);

			if (maybeValue is TValue value)
				return value;

			string camelCaseAttributeName = $"{char.ToLower(attributeName[0])}{attributeName.Substring(1)}";

			throw new ArgumentException($"Missing attribute '{camelCaseAttributeName}' on {theObject.Describe()}");
		}

		/// <summary>
		/// Creates a new <see cref="IdentifiedObject"/> of the same class and with the same MRID
		/// as <paramref name="identifiedObject"/>
		/// </summary>
		public static T Clone<T>(this T identifiedObject) where T : IdentifiedObject
		{
			var result = identifiedObject
				.GetType()
				.GetConstructor(Array.Empty<Type>())
				.Invoke(Array.Empty<object>())
				as T;

			result.MRID = identifiedObject.MRID;

			return result;
		}

		/// <summary>
		/// Returns a string that identifies the given object, giving the type 
		/// and the most appropriate of MRID, Name and Description, when present.
		/// </summary>
		public static string Describe(this IdentifiedObject cimObject)
		{
			string type = cimObject.GetType().Name;

			if (cimObject.Name != null && cimObject.MRID != null)
				return $"{type} with name '{cimObject.Name}' (MRID {cimObject.MRID})";

			if (cimObject.Name != null)
				return $"{type} with name '{cimObject.Name}' (no MRID)";

			if (cimObject.MRID != null)
				return $"{type} with MRID '{cimObject.MRID}'";

			if (cimObject.Description != null)
				return $"{type} with description '{cimObject.Description}' (no MRID)";

			return $"{type} with no MRID, name or description";
		}
	}
}
