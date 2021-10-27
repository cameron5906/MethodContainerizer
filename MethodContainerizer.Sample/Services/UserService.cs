using MethodContainerizer.Sample.Models;
using MethodContainerizer.Sample.Repositories;
using System;

namespace MethodContainerizer.Sample.Services
{
    public class UserService
    {
        private readonly UserRepository _userRepository;

        public UserService() { }

        public UserService(UserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public UserModel CreateUser(UserModel userModel)
        {
            Console.WriteLine($"Creating {userModel.Username}");
            return _userRepository.CreateUserAsync(userModel);
        }
    }
}
