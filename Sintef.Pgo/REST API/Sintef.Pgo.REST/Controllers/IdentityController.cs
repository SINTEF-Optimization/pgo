using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.REST.Extensions;
using System.Linq;

namespace Sintef.Pgo.REST.Controllers
{
	/// <summary>
	/// Handles requests under the /identity path
	/// </summary>
	[Route("api/identity")]
	[Route("api/cim/identity")]
	public class IdentityController : Controller
	{
		/// <summary>
		/// Returns the quotas available to the logged in user
		/// </summary>
		[HttpGet("quotas")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public ActionResult<UserQuotas> GetQuotas()
		{
			var result = User.GetQuotas();

			return Ok(result);
		}
	}
}
