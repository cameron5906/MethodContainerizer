using MethodContainerizer.Sample.Models;

namespace MethodContainerizer.Sample.Repositories
{
    public class UserRepository
    {
        public UserModel CreateUserAsync(UserModel userModel)
        {
            userModel.Id = 1337;
            return userModel;
        }
    }
}
