using MethodContainerizer.Sample.Models;
using MethodContainerizer.Sample.Repositories;

namespace MethodContainerizer.Sample.Services
{
    public class PostService
    {
        private readonly PostRepository _postRepository;

        public PostService()
        {

        }

        public PostService(PostRepository postRepository)
        {
            _postRepository = postRepository;
        }

        public PostModel CreatePost(PostModel postModel)
        {
            return _postRepository.CreatePostAsync(postModel);
        }
    }
}
