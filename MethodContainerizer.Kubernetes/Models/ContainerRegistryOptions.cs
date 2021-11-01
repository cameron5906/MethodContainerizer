using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MethodContainerizer.Kubernetes.Models
{
    public class ContainerRegistryOptions
    {
        public string Host { get; set; }
        public bool IsDockerHub { get; set; }
        public bool RequireAuthentication { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
