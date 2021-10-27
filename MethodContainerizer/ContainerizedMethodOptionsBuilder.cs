using MethodContainerizer.Models;

namespace MethodContainerizer
{
    public sealed class ContainerizedMethodOptionsBuilder
    {
        private readonly ContainerizedMethodOptions _containerizedMethodOptions;

        public ContainerizedMethodOptionsBuilder()
        {
            _containerizedMethodOptions = new ContainerizedMethodOptions();
        }
        
        public ContainerizedMethodOptionsBuilder SetMinimumAvailable(int amount)
        {
            _containerizedMethodOptions.MinimumAvailable = amount;
            return this;
        }

        public ContainerizedMethodOptionsBuilder AsNeeded()
        {
            _containerizedMethodOptions.MinimumAvailable = 0;
            _containerizedMethodOptions.CreateAsNeeded = true;
            return this;
        }

        public ContainerizedMethodOptionsBuilder DoNotRequireAuthorization()
        {
            _containerizedMethodOptions.IsOpen = true;
            return this;
        }

        public ContainerizedMethodOptionsBuilder UseCustomBearerToken(string bearer)
        {
            _containerizedMethodOptions.CustomBearer = bearer;
            return this;
        }

        internal ContainerizedMethodOptions Build()
        {
            return _containerizedMethodOptions;
        }
    }
}