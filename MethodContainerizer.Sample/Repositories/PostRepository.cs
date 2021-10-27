using MethodContainerizer.Sample.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MethodContainerizer.Sample.Repositories
{
    public class PostRepository
    {
        public PostModel CreatePostAsync(PostModel postModel)
        {
            return postModel;
        }
    }
}
