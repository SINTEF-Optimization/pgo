using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Server;
using Sintef.Pgo.REST.Extensions;
using Sintef.Pgo.REST.Hubs;

namespace Sintef.Pgo.REST.Controllers
{
	using Sintef.Pgo.Server;

	/// <summary>
	/// Base class for session controllers, with common helper methods
	/// </summary>
	public class SessionsControllerBase : PgoControllerBase
	{
		/// <summary>
		/// Context for the solution status hub
		/// </summary>
		protected readonly IHubContext<SolutionStatusHub> _solutionStatusHubContext;

		/// <summary>
		/// Initializes a <see cref="SessionsController"/> instance.
		/// </summary>
		/// <param name="serverCollection">The server collection to use</param>
		/// <param name="solutionStatusHubContext">The context for the solution status hub.</param>
		public SessionsControllerBase(IMultiUserServer serverCollection, IHubContext<SolutionStatusHub> solutionStatusHubContext) : base(serverCollection)
		{
			_solutionStatusHubContext = solutionStatusHubContext;
		}

		/// <summary>
		/// Initializes the controller
		/// </summary>
		public SessionsControllerBase(IMultiUserServer serverCollection) : base(serverCollection)
		{ }

		/// <summary>
		/// Returns the session with the given ID, or null if there is none
		/// </summary>
		protected bool FindSession(string id, out Pgo.Server.ISession session, out ActionResult errorResult)
		{
			session = null;

			if (!FindServerForUser(out var server, out errorResult))
				return false;

			session = server.GetSession(id);
			if (session == null)
			{
				errorResult = NotFound();
				return false;
			}

			return true;
		}

		/// <summary>
		/// Returns true if it's OK to create a session with the given parameters, false if not.
		/// </summary>
		/// <param name="server">The server to add the session to</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="id">The ID of the new session</param>
		/// <param name="userQuotas">On success, is set to the </param>
		/// <param name="errorResult"></param>
		protected bool CanCreateSession(IServer server, string networkId, string id, out UserQuotas userQuotas, out ActionResult errorResult)
		{
			userQuotas = User.GetQuotas();

			if (server.SessionIds.Contains(id))
			{
				errorResult = BadRequest($"A session with ID '{id}' already exists.");
				return false;
			}

			if (!server.NetworkIds.Contains(networkId))
			{
				errorResult = BadRequest($"No network with ID '{networkId}' exists.");
				return false;
			}

			int sessionLimit = userQuotas.SessionLimit;
			if (server.SessionIds.Count() >= sessionLimit)
			{
				errorResult = StatusCode(StatusCodes.Status403Forbidden, $"This user is limited to {sessionLimit} sessions at the same time");
				return false;
			}

			errorResult = null;
			return true;
		}

		/// <summary>
		/// Sends a message to all subscribed clients if a new solution is found during optimization.
		/// </summary>
		/// <param name="userId">The internal ID of the user (from AppAuthenticationHandler)</param>
		/// <param name="session">The session.</param>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		protected void Session_BestSolutionFound(string userId, ISession session, object sender, Sintef.Scoop.Kernel.SolutionEventArgs e)
		{
			var s = (e.Solution as IPgoSolution);
			var solutionInfo = session.Summarize(s);
			var solutionInfoString = JsonConvert.SerializeObject(solutionInfo);

			IClientProxy receivers = SolutionStatusHub.Receivers(_solutionStatusHubContext, userId, session.Id);

			receivers.SendAsync("newSolutionStatus", solutionInfoString);
		}
	}

	/// <summary>
	/// The controller for session endpoints that are the same for json and CIM sessions
	/// </summary>
	[Route("api/sessions")]
	[Route("api/cim/sessions")]
	[ApiController]
	public class SessionsController : SessionsControllerBase
	{
		/// <summary>
		/// Initializes a <see cref="SessionsController"/> instance.
		/// </summary>
		/// <param name="serverCollection">The server collection to use</param>
		/// <param name="solutionStatusHubContext">The context for the solution status hub.</param>
		public SessionsController(IMultiUserServer serverCollection, IHubContext<SolutionStatusHub> solutionStatusHubContext)
			: base(serverCollection, solutionStatusHubContext) { }

		/// <summary>
		/// Returns the current status of a specified session.
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist.
		/// </remarks>
		[HttpGet("{id}")]
		[ProducesResponseType(typeof(SessionStatus), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public ActionResult<SessionStatus> Status(string id)
		{
			if (!FindSession(id, out var session, out var errorResult))
				return errorResult;

			return Ok(session.GetStatus());
		}

		/// <summary>
		/// Controls whether optimization is running in the specified session.
		/// Changing this value causes optimization to start or stop.
		/// A request to stop optimization does not complete before the optimizer has actually finished.
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <param name="optimize">If true, optimization runs in the session. 
		///   If false, optimization does not run.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist.
		/// </remarks>
		[HttpPut("{id}/runOptimization")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> StartOrStopOptimization([Required] string id, [FromBody] bool optimize)
		{
			if (!FindSession(id, out var session, out var errorResult))
				return errorResult;

			if (optimize && !session.OptimizationIsRunning)
			{
				var stopCriterion = new Sintef.Scoop.Kernel.StopCriterion();
				Task optimizeTask = session.StartOptimization(stopCriterion);

				TimeSpan timeout = User.GetQuotas().OptimizationTimeout;
				// If optimizeTask does not complete before the timeout, stop it

				// Cannot use TimeSpan values with millisecond representations larger than representable by `int`.
				int timeoutMilliSeconds = (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);

				Task delay = Task.Delay(timeoutMilliSeconds);

				_ = Task.WhenAny(optimizeTask, delay).ContinueWith((t) => stopCriterion.Trigger()); ;
			}

			if (!optimize && session.OptimizationIsRunning)
			{
				await session.StopOptimization();
			}

			return Ok();
		}

		/// <summary>
		/// Returns information on a solution that has earlier been added to the session's set of solutions.
		/// </summary>
		/// <param name="sessionId">The ID of the session.</param>
		/// <param name="solutionId">The ID of the solution. The special ID 'best' refers to the session's best known solution</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist, or if the session does not have a solution with the given ID
		/// </remarks>
		[HttpGet("{sessionId}/solutions/{solutionId}/info")]
		[ProducesResponseType(typeof(SolutionInfo), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public ActionResult<string> GetSolutionSummary(string sessionId, string solutionId)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
				return errorResult;

			IPgoSolution solution = session.GetSolution(solutionId);
			if (solution == null)
				return NotFound();

			return Ok(session.Summarize(solution));
		}

		/// <summary>
		/// Repair an existing solution that does not allow radial flow because it is unconnected,
		/// has cycles, or uses transformers in invalid modes. The repaired solution will be 
		/// added to the session using the given new solution ID.
		/// </summary>
		/// <param name="sessionId">The ID of the session.</param>
		/// <param name="solutionId">The ID of the solution.</param>
		/// <param name="newSolutionId">A new ID for the repaired solution.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist
		///   * `BadRequest`, if a solution with the same ID already exists
		/// </remarks>
		[HttpPost("{sessionId}/solutions/{solutionId}/repair/{newSolutionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<string> RepairSolution(string sessionId, string solutionId, string newSolutionId)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
				return errorResult;

			PgoSolution oldSolution = session.GetSolution(solutionId) as PgoSolution;
			if (oldSolution == null)
				return NotFound();

			try
			{
				string message = session.Repair(oldSolution, newSolutionId);
				return Ok(message);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred repairing the solution: {ex.Message}");
			}
		}

		/// <summary>
		/// Removes a solution from the session's set of solutions.
		/// </summary>
		/// <param name="sessionId">The ID of the session.</param>
		/// <param name="solutionId">The ID if the solution</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist or does not have a solution with the specified ID
		///   * `BadRequest`, if attempting to remove the 'best' solution
		/// </remarks>
		[HttpDelete("{sessionId}/solutions/{solutionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public IActionResult RemoveSolution(string sessionId, string solutionId)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
				return errorResult;

			IPgoSolution solution = session.GetSolution(solutionId);
			if (solution == null)
				return NotFound();

			try
			{
				session.RemoveSolution(solutionId);
			}
			catch (Exception ex)
			{
				return BadRequest(ex.Message);
			}

			return Ok();
		}

		/// <summary>
		/// Returns a summary of the best known solution.
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist.
		///   * `BadRequest`, if no solution has been produced yet
		/// </remarks>
		[HttpGet("{id}/bestSolutionInfo")]
		[ProducesResponseType(typeof(SolutionInfo), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		//[Produces("application/json")]
		public ActionResult<string> BestSolutionSummary(string id)
		{
			if (!FindSession(id, out var session, out var errorResult))
				return errorResult;

			IPgoSolution sol = session.GetBestSolutionClone();
			if (sol != null)
				return Ok(session.Summarize(sol));
			else
				return BadRequest("No solution is found yet");
		}

		/// <summary>
		/// Deletes the session with the given ID, if it exists.
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist.
		/// </remarks>
		[HttpDelete("{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public IActionResult DeleteSession(string id)
		{
			if (!FindSession(id, out _, out var errorResult))
				return errorResult;

			FindServerForUser(out var server, out _);
			server.DeleteSession(id);

			return Ok();
		}

		/// <summary>
		/// Updates the weights for objective components. This can be called only when the optimisation is not running.
		/// The new weights will be used to compute overall solution objective values,
		/// in subsequent optimizations and solution evaluations (summary requests).
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <param name="objectiveComponentWeights">A JSON string containing an array of ObjectiveWeight's.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist.
		///   * `BadRequest`, if the session is optimizing.
		/// </remarks>
		[HttpPut("{id}/objectiveWeights")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public IActionResult SetObjectiveWeights(string id, [FromBody] List<ObjectiveWeight> objectiveComponentWeights)
		{
			if (!FindSession(id, out var session, out var errorResult))
				return errorResult;

			if (session.OptimizationIsRunning)
				return BadRequest("An attempt was made to set objective weights while the optimisation was running.");
			try
			{
				session.SetObjectiveWeights(objectiveComponentWeights);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred setting objective weights: {ex.Message}");
			}

			return Ok();
		}

		/// <summary>
		/// Returns the currently set objective weights.
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist.
		/// </remarks>
		[HttpGet("{id}/objectiveWeights")]
		[ProducesResponseType(typeof(IEnumerable<ObjectiveWeight>), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		//[Produces("application/json")]
		public ActionResult<string> GetObjectiveWeights(string id)
		{
			if (!FindSession(id, out var session, out var errorResult))
				return errorResult;

			return Ok(session.GetObjectiveWeights());
		}
	}
}

namespace Sintef.Pgo.REST.Controllers.Json
{
	/// <summary>
	/// The controller for json-specific session endpoints
	/// </summary>
	[Route("api/sessions")]
	[ApiController]
	public class SessionsController : SessionsControllerBase
	{
		/// <summary>
		/// Initializes a <see cref="SessionsController"/> instance.
		/// </summary>
		/// <param name="serverCollection">The server collection to use</param>
		/// <param name="solutionStatusHubContext">The context for the solution status hub.</param>
		public SessionsController(IMultiUserServer serverCollection, IHubContext<SolutionStatusHub> solutionStatusHubContext)
			: base(serverCollection, solutionStatusHubContext)
		{
		}

		/// <summary>
		/// Creates a session for solving a configuration problem. 
		/// </summary>
		/// <param name="id">The ID of the new session.</param>
		/// <param name="networkId">The ID of the network.</param>
		/// <param name="demands">Demand forecast definition in PGO's JSON format, with type <see cref="Demand"/>.
		/// Note that if demand is missing for a consumer bus, it will be assumed to be zero. 
		/// However, if a demand is given, then it must be given for each period.</param>
		/// <param name="startConfiguration">Start configuration, in PGO's JSON format, with type <see cref="SinglePeriodSettings"/>.
		/// The 'period' property is ignored.</param>
		/// <param name="allowUnspecifiedConsumerDemands">If true, consumers that are not specified in the forecast will be assumed to have zero demand. If false, all consumers' demands must be specified in the forecast.</param>
		/// <remarks>
		/// Form fields:
		///  - networkId: The ID of the network.
		///  - demands: Demand forecast definition in PGO's JSON format, with type <see cref="Demand"/>.
		///    Note that if demand is missing for a consumer bus, it will be assumed to be zero. 
		///    However, if a demand is given, then it must be given for each period.
		///  - startConfiguration: Start configuration, in PGO's JSON format, with type <see cref="SinglePeriodSettings"/>.
		///		 The 'period' property is ignored.
		///  - allowUnspecifiedConsumerDemands: If true, consumers that are not specified in the forecast 
		///    will be assumed to have zero demand. If false, all consumers' demands must be specified in the forecast.
		/// 
		/// Returns HTTP status
		///   * `Created`, if the action was successful
		///   * `BadRequest`, if no network with the given ID has been loaded 
		///     -or- if a session with the same ID already exists
		///     -or- if there was a problem parsing the demand or configuration data
		/// </remarks>
		[HttpPost("{id}")]
		[DisableRequestSizeLimit]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<string> CreateJsonSession(
			[Required] string id,
			[FromForm, Required] string networkId,
			[Required] IFormFile demands,
			IFormFile startConfiguration,
			[FromForm] bool allowUnspecifiedConsumerDemands)
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			if (!CanCreateSession(server, networkId, id, out var userQuotas, out errorResult))
				return errorResult;

			try
			{
				// When accepting two IFormFiles, they have to be read in the same sequence.
				// Ensure this by reading them into memory streams.
				using Stream problemMemoryStream = ReadToMemoryStream(demands);
				using Stream currentConfigurationStream = startConfiguration != null ? ReadToMemoryStream(startConfiguration) : null;

				var session = server.CreateJsonSession(
					id,
					networkId,
					problemMemoryStream,
					currentConfigurationStream,
					allowUnspecifiedConsumerDemands);

				session.SetTimesOutAfter(userQuotas.SessionTimeout);

				string userId = UserId;
				session.BestSolutionFound += (sender, e) => Session_BestSolutionFound(userId, session, sender, e);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred while parsing the session data: {ex.Message}");
			}

			return Created($"/api/sessions/{id}", new { id });
		}

		/// <summary>
		/// Returns the currently best known solution to the session's problem in PGO's JSON format.
		/// 
		/// If the solution is radial and has an associated computable flow, the estimates on the electrical quantities of voltage, power and current are included in the result. 
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist, or if no solution has been found yet (have you started the optimization?)
		/// </remarks>
		[HttpGet("{id}/bestSolution")]
		[ProducesResponseType(typeof(Solution), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		//[Produces("application/json")]
		public ActionResult<string> GetBestSolutionAsJSON(string id)
		{
			if (!FindSession(id, out var session, out var errorResult))
				return errorResult;

			IPgoSolution sol = session.GetBestSolutionClone();
			if (sol == null)
				return BadRequest("No solution found yet (have you started the optimization?)");

			return sol.ToJson((sol.Encoding as PgoProblem)?.FlowProvider, true);
		}

		/// <summary>
		/// Returns a solution that has earlier been added to the session's set of solutions, in PGO's JSON format.
		/// 
		/// If the solution is radial and has an associated computable flow, the estimates on the electrical quantities of voltage, power and current are included in the result. 
		/// </summary>
		/// <param name="sessionId">The ID of the session</param>
		/// <param name="solutionId">The ID of the solution. The special ID 'best' refers to the session's best known solution</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist, or if the session does not have a solution with the given ID
		/// </remarks>
		[HttpGet("{sessionId}/solutions/{solutionId}")]
		[ProducesResponseType(typeof(Solution), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public ActionResult<string> GetSolutionAsJSON(string sessionId, string solutionId)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
				return errorResult;

			IPgoSolution solution = session.GetSolution(solutionId);
			if (solution == null)
				return NotFound();

			return solution.ToJson((solution.Encoding as PgoProblem)?.FlowProvider, true);
		}

		/// <summary>
		/// Adds a solution to the session's set of solutions.
		/// </summary>
		/// <param name="sessionId">The ID of the session.</param>
		/// <param name="solutionId">The ID of the solution.</param>
		/// <param name="solution">The solution description. Only the switch settings are used; flows etc.
		///   are recomputed internally.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist
		///   * `BadRequest`, if a solution with the same ID already exists, or if the solution is invalid
		/// </remarks>
		[HttpPost("{sessionId}/solutions/{solutionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public IActionResult AddSolution(string sessionId, string solutionId, [FromBody] Solution solution)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
				return errorResult;

			try
			{
				PgoSolution pgoSolution = PgoJsonParser.ParseSolution(session.Problem, solution);
				session.ComputeFlow(pgoSolution);
				session.AddSolution(pgoSolution, solutionId);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred adding the solution: {ex.Message}");
			}

			return Ok();
		}

		/// <summary>
		/// Updates an existing solution
		/// </summary>
		/// <param name="sessionId">The ID of the session.</param>
		/// <param name="solutionId">The ID of the solution.</param>
		/// <param name="solution">The solution data. Only the switch settings are used; flows etc.
		///   are recomputed internally.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist
		///   * `BadRequest`, if the solution does not exist, or if the solution is invalid
		/// </remarks>
		[HttpPut("{sessionId}/solutions/{solutionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public IActionResult UpdateSolution([FromRoute] string sessionId, [FromRoute] string solutionId, [FromBody] Solution solution)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
			{
				return errorResult;
			}

			try
			{
				PgoSolution pgoSolution = PgoJsonParser.ParseSolution(session.Problem, solution);
				session.ComputeFlow(pgoSolution);
				session.UpdateSolution(pgoSolution, solutionId);
				return Ok();
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred while evaluating the solution: {ex.Message}");
			}
		}

		/// <summary>
		/// Returns the demands that were used to create the session.
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist.
		/// </remarks>
		[HttpGet("{id}/demands")]
		[ProducesResponseType(typeof(Demand), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		//[Produces("application/json")]
		public ActionResult<string> GetDemands(string id)
		{
			if (!FindSession(id, out var session, out var errorResult))
				return errorResult;

			return Ok(session.GetDemands());
		}
	}
}

namespace Sintef.Pgo.REST.Controllers.CIM
{
	using Sintef.Pgo.Server;

	/// <summary>
	/// The controller for CIM-specific session endpoints
	/// </summary>
	[Route("api/cim/sessions")]
	[ApiController]
	public class SessionsController : SessionsControllerBase
	{
		/// <summary>
		/// Initializes a <see cref="SessionsController"/> instance.
		/// </summary>
		/// <param name="serverCollection">The server collection to use</param>
		/// <param name="solutionStatusHubContext">The context for the solution status hub.</param>
		public SessionsController(IMultiUserServer serverCollection, IHubContext<SolutionStatusHub> solutionStatusHubContext)
			: base(serverCollection, solutionStatusHubContext)
		{
		}

		/// <summary>
		/// Creates a session for solving a network configuration problem. 
		/// </summary>
		/// <param name="id">The ID of the new session.</param>
		/// <param name="parameters">Parameters for creating the session</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Created`, if the action was successful
		///   * `BadRequest`, if no network with the given ID has been loaded 
		///     -or- if a session with the same ID already exists
		///     -or- if there was a problem parsing the demand or configuration data
		/// </remarks>
		[HttpPost("{id}")]
		[DisableRequestSizeLimit]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public ActionResult<string> CreateCimSession(
			[Required] string id,
			[Required, FromBody] CimJsonLdSessionParameters parameters)
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			if (!CanCreateSession(server, parameters.NetworkId, id, out var userQuotas, out errorResult))
				return errorResult;

			try
			{
				var session = server.CreateCimSession(id, parameters);

				session.SetTimesOutAfter(userQuotas.SessionTimeout);

				string userId = UserId;
				session.BestSolutionFound += (sender, e) => Session_BestSolutionFound(userId, session, sender, e);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred while parsing the session data: {ex.Message}");
			}

			return Created($"/api/cim/sessions/{id}", new { id });
		}

		/// <summary>
		/// Returns the currently best known solution to the session's problem.
		/// </summary>
		/// <param name="id">The ID of the session.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist, or if no solution has been found yet (have you started the optimization?)
		/// </remarks>
		[HttpGet("{id}/bestSolution")]
		[ProducesResponseType(typeof(CimJsonLdSolution), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		//[Produces("application/json")]
		public ActionResult<CimJsonLdSolution> GetBestSolutionAsCim(string id)
		{
			if (!FindSession(id, out var session, out var errorResult))
				return errorResult;

			IPgoSolution bestSolution = session.GetBestSolutionClone();
			if (bestSolution == null)
				return BadRequest("No solution found yet (have you started the optimization?)");

			return ConvertSolution(bestSolution, session);
		}

		/// <summary>
		/// Adds a solution to the session's set of solutions.
		/// </summary>
		/// <param name="sessionId">The ID of the session.</param>
		/// <param name="solutionId">The ID of the solution.</param>
		/// <param name="solution">The solution to add.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist
		///   * `BadRequest`, if a solution with the same ID already exists, or if the solution is invalid
		/// </remarks>
		[HttpPost("{sessionId}/solutions/{solutionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public IActionResult AddSolution(string sessionId, string solutionId, [FromBody] CimJsonLdSolution solution)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
				return errorResult;

			try
			{
				PgoSolution pgoSolution = ConvertSolution(solution, session);

				session.ComputeFlow(pgoSolution);
				session.AddSolution(pgoSolution, solutionId);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred adding the solution: {ex.Message}");
			}

			return Ok();
		}

		/// <summary>
		/// Updates an existing solution
		/// </summary>
		/// <param name="sessionId">The ID of the session.</param>
		/// <param name="solutionId">The ID of the solution.</param>
		/// <param name="solution">The updated solution.</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist
		///   * `BadRequest`, if a solution with the same ID already exists, or if the solution is invalid
		/// </remarks>
		[HttpPut("{sessionId}/solutions/{solutionId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public IActionResult UpdateSolution(string sessionId, string solutionId, [FromBody] CimJsonLdSolution solution)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
				return errorResult;

			try
			{
				PgoSolution pgoSolution = ConvertSolution(solution, session);

				session.ComputeFlow(pgoSolution);
				session.UpdateSolution(pgoSolution, solutionId);
			}
			catch (Exception ex)
			{
				return BadRequest($"An error occurred updating the solution: {ex.Message}");
			}

			return Ok();
		}

		/// <summary>
		/// Returns a solution that has earlier been added to the session's set of solutions.
		/// </summary>
		/// <param name="sessionId">The ID of the session</param>
		/// <param name="solutionId">The ID of the solution. The special ID 'best' refers to the session's best known solution</param>
		/// <remarks>
		/// Returns HTTP status
		///   * `Ok`, if the action was successful
		///   * `NotFound`, if the specified session does not exist, or if the session does not have a solution with the given ID
		/// </remarks>
		[HttpGet("{sessionId}/solutions/{solutionId}")]
		[ProducesResponseType(typeof(CimJsonLdSolution), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public ActionResult<CimJsonLdSolution> GetSolutionAsCim(string sessionId, string solutionId)
		{
			if (!FindSession(sessionId, out var session, out var errorResult))
				return errorResult;

			IPgoSolution solution = session.GetSolution(solutionId);
			if (solution == null)
				return NotFound();

			return ConvertSolution(solution, session);
		}

		/// <summary>
		/// Converts an external CIM solution to an internal solution for the given session
		/// </summary>
		private PgoSolution ConvertSolution(CimJsonLdSolution solution, ISession session)
		{
			FindServerForUser(out var server, out _);

			var cimNetworkConverter = server.GetCimNetworkConverter(session.NetworkId);
			var converter = new CimSolutionConverter(cimNetworkConverter);

			return converter.ConvertToPgo(solution, session.Problem);
		}

		/// <summary>
		/// Converts the given internal solution in the given session to an external CIM solution
		/// </summary>
		private CimJsonLdSolution ConvertSolution(IPgoSolution sol, ISession session)
		{
			FindServerForUser(out var server, out _);

			var cimNetworkConverter = server.GetCimNetworkConverter(session.NetworkId);
			var converter = new CimSolutionConverter(cimNetworkConverter);
			var cimSolution = converter.ConvertToCim(sol);

			var metadata = sol.Problem().Periods.Select(p => new CimJsonExporter.SolutionMetadata());

			return converter.ConvertToCimJsonLd(cimSolution, metadata);
		}
	}
}