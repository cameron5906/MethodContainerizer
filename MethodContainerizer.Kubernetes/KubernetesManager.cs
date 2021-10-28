using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using k8s;
using k8s.KubeConfigModels;
using k8s.Models;
using MethodContainerizer.Interfaces;
using Microsoft.Rest;

namespace MethodContainerizer.Kubernetes
{
    internal class KubernetesManager : IOrchestrator
    {
        private readonly IDictionary<string, int> _containerNames;
        private readonly DockerClient _dockerClient;
        private readonly k8s.Kubernetes _kubernetesClient;

        public KubernetesManager(string k8sConnectionString, string containerRegistry)
        {
            _containerNames = new Dictionary<string, int>();
            var config = k8s.KubernetesClientConfiguration
                .BuildConfigFromConfigFile(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".kube", "config"),
                    "docker-desktop");

            _kubernetesClient = new k8s.Kubernetes(config);
        }

        public async Task PrepareDockerDaemon()
        {
            var pod = await _kubernetesClient.CreateNamespacedPodAsync(new V1Pod
            {
                ApiVersion = "v1",
                Kind = "Pod",
                Spec = new V1PodSpec
                {
                    Containers = new List<V1Container>
                    {
                        new V1Container
                        {
                            Name = "docker-build-daemon",
                            Image = "docker:dind",
                            SecurityContext = new V1SecurityContext
                            {
                                Privileged = true
                            },
                        }
                    },
                },
                Metadata = new V1ObjectMeta
                {
                    Name = $"docker-daemon{DateTime.Now.Ticks.ToString()}",
                    Labels = new Dictionary<string, string> {{"app", "methodcontainerizer"}}
                }
            }, "default");

            var eventWaiter = new SemaphoreSlim(0, 1);
            var watcher = await _kubernetesClient.WatchNamespacedPodAsync(pod.Name(), pod.Namespace());
            watcher.OnEvent += (a, b) =>
            {
                if (b.Status.Phase == "Running")
                {
                    eventWaiter.Release();
                    watcher.Dispose();
                }
            };

            await eventWaiter.WaitAsync();

            var shell = await _kubernetesClient.WebSocketNamespacedPodExecAsync(pod.Name(), pod.Namespace(), "/bin/sh",
                pod.Spec.Containers.First().Name, true, true, true, true);

            var cmd = Encoding.UTF8.GetBytes("echo \"hey it works\" > /home/yo.txt");
            await shell.SendAsync(cmd, WebSocketMessageType.Binary, true, default);
            //await shell.CloseAsync(WebSocketCloseStatus.NormalClosure, "Transferred", default(CancellationToken))
        }
        
        public async Task<(string ContainerId, int Port)> Start(string imageName, string dockerPath)
        {

            return ("", 0);
        }

        public async Task<bool> Shutdown(string name)
        {
            await _dockerClient.Containers.StopContainerAsync(name, new ContainerStopParameters
            {
                WaitBeforeKillSeconds = 3
            });

            await _dockerClient.Containers.RemoveContainerAsync(name, new ContainerRemoveParameters());

            return true;
        }
    }

    internal class ProgressReporter : IProgress<JSONMessage>
    {
        public void Report(JSONMessage value)
        {

        }
    }
}
