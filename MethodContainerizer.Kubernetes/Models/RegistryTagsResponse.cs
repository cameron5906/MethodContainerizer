using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MethodContainerizer.Kubernetes.Models
{
    internal class RegistryTagsResponse
    {
        public int count { get; set; }
        public RegistryTag[] results { get; set; }
    }

    internal class RegistryTag
    {
        public string name { get; set; }
    }
}
