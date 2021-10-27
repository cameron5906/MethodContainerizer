using System.Threading.Tasks;

namespace MethodContainerizer.Interfaces
{
    public interface IOrchestrator
    {
        Task<(string ContainerId, int Port)> Start(string name, string dockerPath);
        Task<bool> Shutdown(string name);
    }
}
