using System.Threading.Tasks;

namespace MethodContainerizer.Interfaces
{
    public interface IOrchestrator
    {
        Task<(string ContainerId, string Hostname, int Port)> Start(string name, string dockerPath, int assemblyByteLength);
        Task<bool> Shutdown(string name);
        Task CleanUp();
    }
}
