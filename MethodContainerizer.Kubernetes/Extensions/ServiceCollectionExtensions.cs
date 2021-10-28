using Microsoft.Extensions.DependencyInjection;

namespace MethodContainerizer.Kubernetes.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection UseKubernetesOrchestration(this IServiceCollection services, string k8s, string containerRegistry)
        {
            var orchestrationManager = new KubernetesManager(k8s, containerRegistry);

            orchestrationManager.PrepareDockerDaemon().GetAwaiter().GetResult();
            
            MethodProxyManager.SetOrchestrator(orchestrationManager);
            InjectionManager.SetOrchestrationManager(orchestrationManager);

            return services;
        }
    }
}
