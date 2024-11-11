using System;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Sintef.Pgo.Core;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Server;
using Sintef.Pgo.REST.Extensions;

namespace Sintef.Pgo.REST.Controllers
{
	/// <summary>
	/// The controller for network endpoints that are the same for json and cim networks
	/// </summary>
	public abstract class NetworksController : PgoControllerBase
	{
		/// <summary>
		/// Initializes a <see cref="NetworksController"/> instance.
		/// </summary>
		/// <param name="serverCollection">The server collection to use</param>
		public NetworksController(IMultiUserServer serverCollection) : base(serverCollection)
		{
		}

		/// <summary>
		/// Analyses the network, and returns a human-readable report.
		/// </summary>
		/// <param name="id">The ID of the network.</param>
		/// <param name="verbose">If true, the analysis contains more detailed information</param>
		/// <remarks>
		/// This is useful during integration development, to get a sense of how compact the 
		/// input network is, and if it can in any way be configured radially and connected 
		/// (in the sense that each consumers can be connected to exactly one provider).
		/// 
		/// Returns HTTP status
		///   * `OK`, if the action was successful
		///   * `BadRequest`, if the network ID has not been loaded 
		/// </remarks>		
		[HttpGet("{id}/analysis")]
		[DisableRequestSizeLimit]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<string> AnalyseNetwork([Required] string id, bool verbose = false)
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			var analysis = server.AnalyseNetwork(id, verbose);
			if (analysis == null)
				return BadRequest($"The network '{id}' has not been loaded.");

			try
			{
				return analysis;
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred: {ex.Message}");
			}
		}

		/// <summary>
		/// Checks whether the network can be configured with a valid radial configuration.
		/// </summary>
		/// <param name="id">The ID of the network.</param>
		/// <returns>
		/// Returns HTTP status
		///   * `OK`, if the action was successful
		///   * `BadRequest`, if the network ID has not been loaded
		/// </returns>
		[HttpGet("{id}/connectivityStatus")]
		[ProducesResponseType(typeof(NetworkConnectivityStatus), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<NetworkConnectivityStatus> CheckNetworkConnectivity([Required] string id)
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			var analysis = server.GetNetworkConnectivityStatus(id);
			if (analysis == null)
				return BadRequest($"The network '{id}' has not been loaded.");

			try
			{
				return Ok(analysis);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred: {ex.Message}");
			}
		}

		/// <summary>
		/// Deletes the network with the given ID, if it exists.
		/// </summary>
		/// <param name="id">The ID of the network.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified network does not exist.
		/// </remarks>		
		[HttpDelete("{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public IActionResult DeleteNetwork(string id)
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			if (!server.NetworkIds.Contains(id))
				return NotFound();

			server.DeleteNetwork(id);

			return Ok();
		}

		/// <summary>
		/// Throws an error if the given network violates the network size quota
		/// </summary>
		protected void VerifySizeQuota(PowerNetwork network)
		{
			UserQuotas quotas = User.GetQuotas();

			if (network.Buses.Count() > quotas.NetworkMaxSize)
				throw new UserQuotaException($"This user is limited to networks with a maximum of {quotas.NetworkMaxSize} nodes");
		}
	}

	/// <summary>
	/// Thrown when a user quota is exceeded
	/// </summary>
	[Serializable]
	internal class UserQuotaException : Exception
	{
		public UserQuotaException()
		{
		}

		public UserQuotaException(string message) : base(message)
		{
		}

		public UserQuotaException(string message, Exception innerException) : base(message, innerException)
		{
		}

		protected UserQuotaException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}
	}
}

namespace Sintef.Pgo.REST.Controllers.Json
{
	/// <summary>
	/// The controller for json-specific network endpoints
	/// </summary>
	[Route("api/networks")]
	[ApiController]
	public class NetworksController : Controllers.NetworksController
	{
		/// <summary>
		/// Initializes a <see cref="NetworksController"/> instance.
		/// </summary>
		/// <param name="serverCollection">The server collection to use</param>
		public NetworksController(IMultiUserServer serverCollection)
			: base(serverCollection)
		{
		}

		/// <summary>
		/// Returns a network that has previously been loaded into the server.
		/// </summary>
		/// <param name="id">The ID of the network.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified network does not exist.
		/// </remarks>		
		[HttpGet("{id}")]
		[ProducesResponseType(typeof(PowerGrid), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public ActionResult<PowerGrid> GetNetwork([Required] string id)
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			try
			{
				return Ok(server.GetNetwork(id));
			}
			catch (Exception ex)
			{
				return BadRequest($"Error getting network: {ex.Message}");
			}
		}

		/// <summary>
		/// Creates a power network from the given JSON data, and stores it for use in optimization sessions. The given ID is used when creating a session to start optimization on this network.
		/// </summary>
		/// <param name="id">The ID of the new network.</param>
		/// <param name="networkDescription">A file containing the network information in PGO's JSON format.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `BadRequest`, if a network with the given ID was already was loaded -or- if there was a problem parsing the input data
		/// </remarks>		
		[HttpPost("{id}")]
		[DisableRequestSizeLimit]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult LoadNetworkFromJson(
			[Required] string id,
			[Required] IFormFile networkDescription)
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			if (server.NetworkIds.Contains(id))
			{
				return BadRequest($"A network with ID '{id}' is already loaded.");
			}

			UserQuotas quotas = User.GetQuotas();

			if (server.NetworkIds.Count() >= quotas.NetworkLimit)
				return base.StatusCode(StatusCodes.Status403Forbidden, $"This user is limited to {quotas.NetworkLimit} networks at the same time");

			Stream networkDescriptionMemoryStream = ReadToMemoryStream(networkDescription);

			try
			{
				server.LoadNetworkFromJson(id, networkDescriptionMemoryStream, VerifySizeQuota);
			}
			catch (UserQuotaException ex)
			{
				return base.StatusCode(StatusCodes.Status403Forbidden, ex.Message);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred while parsing the network data: {ex.Message}");
			}

			return Created(Request.Path, new { id });
		}
	}
}

namespace Sintef.Pgo.REST.Controllers.CIM
{
	/// <summary>
	/// The controller for cim-specific network endpoints
	/// </summary>
	[Route("api/cim/networks")]
	[ApiController]
	public class NetworksController : Controllers.NetworksController
	{
		/// <summary>
		/// Initializes a <see cref="NetworksController"/> instance.
		/// </summary>
		/// <param name="serverCollection">The server collection to use</param>
		public NetworksController(IMultiUserServer serverCollection)
			: base(serverCollection)
		{
		}

		///// <summary>
		///// Returns a network that has previously been loaded into the server.
		///// </summary>
		///// <param name="id">The ID of the network.</param>
		///// <remarks>
		///// Returns HTTP status
		/////   * `Ok`, if the action was successful
		/////   * `NotFound`, if the specified network does not exist.
		///// </remarks>		
		//[HttpGet("{id}")]
		//[ProducesResponseType(typeof(PowerGrid), StatusCodes.Status200OK)]
		//[ProducesResponseType(StatusCodes.Status404NotFound)]
		//public ActionResult<PowerGrid> GetNetwork([Required] string id)
		//{
		//	if (!FindServerForUser(out var server, out var errorResult))
		//		return errorResult;

		//	try
		//	{
		//		return Ok(server.GetNetwork(id));
		//	}
		//	catch (Exception ex)
		//	{
		//		return BadRequest($"Error getting network: {ex.Message}");
		//	}
		//}

		/// <summary>
		/// Creates a power network from the given CIM network on JSON-LD format, and stores it for use in optimization sessions. 
		/// </summary>
		/// <param name="id">The ID of the network.</param>
		/// <param name="networkData">Data for creating a network from CIM data on JSON-LD format</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `BadRequest`, if a network with the given ID was already was loaded -or- if there was a problem parsing the input data
		/// </remarks>		
		[HttpPost("{id}")]
		[DisableRequestSizeLimit]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult LoadNetworkFromCim(
			[Required] string id,
			[Required, FromBody] CimJsonLdNetworkData networkData)
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			if (server.NetworkIds.Contains(id))
			{
				return BadRequest($"A network with ID '{id}' is already loaded.");
			}

			UserQuotas quotas = User.GetQuotas();

			if (server.NetworkIds.Count() >= quotas.NetworkLimit)
				return base.StatusCode(StatusCodes.Status403Forbidden, $"This user is limited to {quotas.NetworkLimit} networks at the same time");

			try
			{
				server.LoadCimNetworkFromJsonLd(id, networkData, VerifySizeQuota);
			}
			catch (UserQuotaException ex)
			{
				return base.StatusCode(StatusCodes.Status403Forbidden, ex.Message);
			}
			catch (Exception ex)
			{
				return BadRequest(ex.Message);
			}

			return Created(Request.Path, new { id });
		}
	}
}
