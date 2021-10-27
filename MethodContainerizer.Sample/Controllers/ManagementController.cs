using System.Linq;
using MethodContainerizer.Sample.Models;
using MethodContainerizer.Sample.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace MethodContainerizer.Sample.Controllers
{
    [ApiController]
    [Route("/")]
    public class ManagementController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly PostService _postService;

        public ManagementController(UserService userService, PostService postService)
        {
            _userService = userService;
            _postService = postService;
        }

        [HttpGet]
        public async Task<UserModel> GetAString_Controller()
        {
            await Task.CompletedTask;

            return _userService.CreateUser(new UserModel() { Username = "Cameron" });
        }

        [HttpPost]
        public async Task<PostModel> CreatePostAsync()
        {
            await Task.CompletedTask;

            return _postService.CreatePost(new Models.PostModel());
        }
    }
}
