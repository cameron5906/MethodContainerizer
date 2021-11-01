using MethodContainerizer.Kubernetes.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MethodContainerizer.Kubernetes
{
    public class KubernetesOptionsBuilder
    {
        private readonly KubernetesOrchestrationOptions _kubernetesOptions;

        public KubernetesOptionsBuilder()
        {
            _kubernetesOptions = new KubernetesOrchestrationOptions();
        }

        public KubernetesOptionsBuilder SetDockerHubRegistry(string username = "", string password = "")
        {
            _kubernetesOptions.ContainerRegistryOptions.RequireAuthentication = true;
            _kubernetesOptions.ContainerRegistryOptions.IsDockerHub = true;
            _kubernetesOptions.ContainerRegistryOptions.Username = username;
            _kubernetesOptions.ContainerRegistryOptions.Password = password;
            return this;
        }

        public KubernetesOptionsBuilder SetPrivateContainerRegistry(string host, string username = "", string password = "")
        {
            _kubernetesOptions.ContainerRegistryOptions.RequireAuthentication = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
            _kubernetesOptions.ContainerRegistryOptions.Host = host;
            _kubernetesOptions.ContainerRegistryOptions.IsDockerHub = false;
            _kubernetesOptions.ContainerRegistryOptions.Username = username;
            _kubernetesOptions.ContainerRegistryOptions.Password = password;
            return this;
        }

        public KubernetesOptionsBuilder SetContext(string contextName)
        {
            _kubernetesOptions.ContextName = contextName;
            return this;
        }

        public KubernetesOrchestrationOptions Build()
        {
            return _kubernetesOptions;
        }
    }
}
