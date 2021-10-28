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
        private V1Pod _buildPod;

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
            _buildPod = await _kubernetesClient.CreateNamespacedPodAsync(new V1Pod
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
            var watcher = await _kubernetesClient.WatchNamespacedPodAsync(_buildPod.Name(), _buildPod.Namespace());
            watcher.OnEvent += (a, b) =>
            {
                if (b.Status.Phase == "Running")
                {
                    eventWaiter.Release();
                    watcher.Dispose();
                }
            };

            await eventWaiter.WaitAsync();
        }
        
        public async Task<(string ContainerId, int Port)> Start(string imageName, string dockerPath)
        {
            var tarId = await SendDockerContextToBuilder(dockerPath);
            await BuildRemoteContainer(tarId, imageName);
            
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

        private async Task<string> SendDockerContextToBuilder(string tarFilePath)
        {
            var tarId = Guid.NewGuid().ToString().Replace("-", "");
            var tarBytes = await File.ReadAllBytesAsync(tarFilePath);

            // Transfer TAR file
            await ExecuteBuildAgentCommand(new[]
                {"/bin/sh", "-c", $"echo {Convert.ToBase64String(tarBytes)} | base64 -d > /home/{tarId}.tar"});
            
            // Create directory
            await ExecuteBuildAgentCommand(new[]{"mkdir", $"/home/{tarId}"});
            
            // Extract into directory
            await ExecuteBuildAgentCommand(new[] {"tar", "-xvf", $"/home/{tarId}.tar", "-C", $"/home/{tarId}"});
            
            // Delete TAR
            await ExecuteBuildAgentCommand(new[] {"rm", "-rf", $"/home/{tarId}.tar"});
            
            return tarId;
        }

        private async Task BuildRemoteContainer(string tarId, string imageName)
        {
            await ExecuteBuildAgentCommand(new[] {"docker", "build", $"/home/{tarId}", "-t", imageName});
        }

        private async Task ExecuteBuildAgentCommand(string[] commandArguments)
        {
            var shell = await _kubernetesClient.WebSocketNamespacedPodExecAsync(
                _buildPod.Name(), 
                _buildPod.Namespace(), 
                commandArguments,
                stderr: true, 
                stdout: true,
                tty: true,
                container: _buildPod.Spec.Containers.First().Name
            );
            
            await shell.CloseAsync(WebSocketCloseStatus.NormalClosure, "Command complete", default(CancellationToken));
        }
    }

    internal class ProgressReporter : IProgress<JSONMessage>
    {
        public void Report(JSONMessage value)
        {

        }
    }
}
