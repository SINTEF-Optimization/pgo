using Sintef.Pgo.Cim;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// A collection of CIM objects that define a configuration of the network.
	/// 
	/// These objects do not need to define complete CIM objects, but only supply certain properties of CIM objects whose
	/// primary definition is in the network data. The referenced network object is identified as follows:
	///  - If using CIM objects directly in the .NET API, the MRID property MUST be set and identifies the network object.
	///  - If using JSON-LD (through the REST API or .NET API), the MRID property MAY be set to identify the network object.
	///    If it is not, the network object is identified by URI.
	/// </summary>
	public class CimConfiguration
	{
		/// <summary>
		/// Settings for switches.
		/// 
		/// The setting of a switch is represented by the 'open' property.
		/// </summary>
		public List<Switch> Switches { get; set; }

		/// <summary>
		/// Creates a <see cref="CimConfiguration"/> by collecting the relevant CIM objects in <paramref name="cimObjects"/>
		/// </summary>
		public static CimConfiguration FromObjects(IEnumerable<IdentifiedObject> cimObjects)
		{
			return new CimConfiguration
			{
				Switches = cimObjects.OfType<Switch>().ToList()
			};
		}
	}
}