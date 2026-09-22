using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STNG.Authentication;
using Volo.Abp.Auditing;

namespace STNG.Controllers;

[AllowAnonymous, DisableAuditing]
[Route("api/auth/parent")]
public class ParentRegistrationController : STNGController
{
    private readonly IParentRegistrationAppService _registration;
    public ParentRegistrationController(IParentRegistrationAppService registration) => _registration = registration;

    [HttpPost("register")]
    public Task RegisterAsync(RegisterParentDto input) => _registration.RegisterAsync(input);
}
