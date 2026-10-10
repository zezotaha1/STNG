using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STNG.Authentication;

namespace STNG.Controllers;

[ApiController, Authorize(Policy = "ParentBearer")]
[Route("api/auth/me")]
public class ParentCurrentUserController : STNGController
{
    private readonly IParentCurrentUserAppService _currentUser;
    public ParentCurrentUserController(IParentCurrentUserAppService currentUser) => _currentUser = currentUser;

    [HttpGet]
    public Task<ParentCurrentUserDto> GetAsync() => _currentUser.GetAsync();
}
