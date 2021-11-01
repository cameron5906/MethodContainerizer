using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MethodContainerizer.Kubernetes.Models
{
    public class KubernetesOrchestrationOptions
    {
        public string ContextName { get; set; }
        public ContainerRegistryOptions ContainerRegistryOptions { get; set; }
    
        public KubernetesOrchestrationOptions()
        {
            ContainerRegistryOptions = new ContainerRegistryOptions();
        }
    }
}
