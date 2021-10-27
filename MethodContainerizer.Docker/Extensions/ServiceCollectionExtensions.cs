using Microsoft.Extensions.DependencyInjection;

namespace MethodContainerizer.Docker.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection UseDockerOrchestration(this IServiceCollection services)
        {
            var orchestrationManager = new DockerManager();

            MethodProxyManager.SetOrchestrator(orchestrationManager);
            InjectionManager.SetOrchestrationManager(orchestrationManager);

            return services;
        }
    }
}
