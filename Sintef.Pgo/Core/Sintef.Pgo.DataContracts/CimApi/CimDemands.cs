using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sintef.Pgo.Cim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Consumer demands for one period, as CIM data on JSON-LD format
	/// </summary>
	public class CimJsonLdPeriodAndDemands
	{
		/// <summary>
		/// The period that the demands are for
		/// </summary>
		[JsonRequired]
		public Period Period { get; set; }

		/// <summary>
		/// The CIM objects defining the demands in the period.
		/// 
		/// This is a JSON-LD graph object containing a collection of CIM objects. 
		/// For information on which CIM objects to include and how they are used in the PGO internal model,
		/// see the documentation of <see cref="CimDemands"/>.
		/// </summary>
		[JsonRequired]
		public JObject Demands { get; set; }
	}

	/// <summary>
	/// Consumer demands for one period, as CIM objects
	/// </summary>
	public class CimPeriodAndDemands
	{
		/// <summary>
		/// The period that the demands are for
		/// </summary>
		public Period Period { get; set; }

		/// <summary>
		/// The CIM objects defining the demands in the period
		/// </summary>
		public CimDemands Demands { get; set; }
	}

	/// <summary>
	/// A collection of CIM objects that define demands.
	/// 
	/// These objects do not need to define complete CIM objects, but only supply certain properties of CIM objects whose
	/// primary definition is in the network data. The referenced network object is identified as follows:
	///  - If using CIM objects directly in the .NET API, the MRID property MUST be set and identifies the network object.
	///  - If using JSON-LD (through the REST API or .NET API), the MRID property MAY be set to identify the network object.
	///    If it is not, the network object is identified by URI.
	/// </summary>
	public class CimDemands
	{
		/// <summary>
		/// Demands for energy consumers.
		/// 
		/// The demands for the energy consumer are defined by the
		/// 'p' (active power) and 'q' (reactive power) properties.
		/// The active power must be positive.
		/// </summary>
		public List<EnergyConsumer> EnergyConsumers { get; set; } = new();

		/// <summary>
		/// Demands for energy consumers that are modelled using equivalent injections.
		/// 
		/// The demands for the energy consumer are defined by the
		/// 'p' (active power) and 'q' (reactive power) properties.
		/// The active power must be positive.
		/// </summary>
		public List<EquivalentInjection> EquivalentInjections { get; set; } = new();

		/// <summary>
		/// Creates a <see cref="CimDemands"/> by collecting the relevant CIM objects in <paramref name="cimObjects"/>
		/// </summary>
		public static CimDemands FromObjects(IEnumerable<IdentifiedObject> cimObjects)
		{
			return new CimDemands()
			{
				EnergyConsumers = cimObjects.OfType<EnergyConsumer>().ToList(),
				EquivalentInjections = cimObjects.OfType<EquivalentInjection>().ToList()
			};
		}
	}
}
