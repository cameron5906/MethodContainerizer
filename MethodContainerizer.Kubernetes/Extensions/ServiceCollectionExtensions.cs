using Microsoft.Extensions.DependencyInjection;
using System;

namespace MethodContainerizer.Kubernetes.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection UseKubernetesOrchestration(this IServiceCollection services, Action<KubernetesOptionsBuilder> buildConfig)
        {
            var configBuilder = new KubernetesOptionsBuilder();
            buildConfig(configBuilder);
            var config = configBuilder.Build();

            var orchestrationManager = new KubernetesManager(config);

            orchestrationManager.PrepareDockerDaemon().GetAwaiter().GetResult();
            
            MethodProxyManager.SetOrchestrator(orchestrationManager);
            InjectionManager.SetOrchestrationManager(orchestrationManager);

            return services;
        }
    }
}
