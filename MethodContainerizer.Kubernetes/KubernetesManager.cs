using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using k8s;
using k8s.Models;
using MethodContainerizer.Interfaces;
using MethodContainerizer.Kubernetes.Models;
using Newtonsoft.Json.Linq;

namespace MethodContainerizer.Kubernetes
{
    internal class KubernetesManager : IOrchestrator
    {
        private readonly IDictionary<string, int> _containerNames;
        private readonly IList<string> _alreadyBuiltImages;
        private readonly IList<string> _k8sServices;
        private readonly k8s.Kubernetes _kubernetesClient;
        private V1Pod _managementPod;
        private KubernetesOrchestrationOptions _config;

        public KubernetesManager(KubernetesOrchestrationOptions config)
        {
            _config = config;
            _containerNames = new Dictionary<string, int>();
            _alreadyBuiltImages = new List<string>();
            _k8sServices = new List<string>();
            
            var k8sConfig = k8s.KubernetesClientConfiguration
                .BuildConfigFromConfigFile(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config"),
                    config.ContextName);

            _kubernetesClient = new k8s.Kubernetes(k8sConfig);
        }

        public async Task PrepareDockerDaemon()
        {
            _managementPod = await _kubernetesClient.CreateNamespacedPodAsync(new V1Pod
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
                                Privileged = true,
                                RunAsUser = 0,
                                AllowPrivilegeEscalation = true
                            },
                        }
                    },
                },
                Metadata = new V1ObjectMeta
                {
                    Name = $"docker-daemon{DateTime.Now.Ticks}",
                    Labels = new Dictionary<string, string> {{"app", "methodcontainerizer"}}
                }
            }, "default");

            var eventWaiter1 = new SemaphoreSlim(0, 1);
            var watcher1 = await _kubernetesClient.WatchNamespacedPodAsync(_managementPod.Name(), _managementPod.Namespace());
            watcher1.OnEvent += (a, b) =>
            {
                if (b.Status.Phase == "Running")
                {
                    eventWaiter1.Release();
                    watcher1.Dispose();
                }
            };

            await eventWaiter1.WaitAsync();
        }
        
        public async Task<(string ContainerId, string Hostname, int Port)> Start(string imageName, string dockerPath, int assemblyByteLength)
        {
            using var httpClient = new HttpClient();
            using var tarStream = File.OpenRead(dockerPath);
            var needToBuild = false;
            var tarHash = assemblyByteLength.ToString();
            var serviceClusterIp = "";

            if(!_k8sServices.Contains(imageName))
            {
                Console.WriteLine($"Deploying Kubernetes service for {imageName}");
                var service = await _kubernetesClient.CreateNamespacedServiceAsync(new V1Service
                {
                    ApiVersion = "v1",
                    Kind = "Service",
                    Spec = new V1ServiceSpec
                    {
                        Type = "ClusterIP",
                        Selector = new Dictionary<string, string>
                        {
                            { "app", GetAssemblyFriendlyName() }
                        },
                        Ports = new List<V1ServicePort>
                        {
                            new V1ServicePort(5959, targetPort: new IntstrIntOrString("5959"))
                        },
                    },
                    Metadata = new V1ObjectMeta
                    {
                        Name = $"{imageName}-loadbalancer"
                    }
                }, "default");

                var serviceWaiter = new SemaphoreSlim(0, 1);
                var serviceWatcher = await _kubernetesClient.WatchNamespacedServiceAsync(service.Name(), service.Namespace());
                serviceWatcher.OnEvent += (a, b) =>
                {
                    if(!string.IsNullOrEmpty(b.Spec.ClusterIP))
                    {
                        serviceClusterIp = b.Spec.ClusterIP;
                        serviceWaiter.Release();
                    }
                };

                await serviceWaiter.WaitAsync();

                _k8sServices.Add(imageName);
            }

            try
            {
                HttpResponseMessage response = null;

                if(_config.ContainerRegistryOptions.IsDockerHub)
                {
                    response = await httpClient.GetAsync($"https://registry.hub.docker.com/v2/repositories/{_config.ContainerRegistryOptions.Username}/{imageName}/tags");
                }
                else
                {
                    response = await httpClient.GetAsync($"{_config.ContainerRegistryOptions.Host}/v2/{imageName}/tags");
                }

                var body = await response.Content.ReadAsStringAsync();
                var tags = JsonSerializer.Deserialize<RegistryTagsResponse>(body);

                if (!tags.results.Any(t => t.name == tarHash))
                    needToBuild = true;
            } catch(Exception ex)
            {
                needToBuild = true;
            }

            if (needToBuild && _managementPod != null)
            {
                Console.WriteLine($"Building method image: {imageName}");

                if (!_alreadyBuiltImages.Contains(imageName))
                {
                    Console.WriteLine("Sending assembly information to build daemon...");
                    var tarId = await SendDockerContextToBuilder(dockerPath);

                    Console.WriteLine("Building image...");
                    await BuildRemoteContainer(tarId, imageName, tarHash);

                    Console.WriteLine("Pushing image to registry...");
                    await PushContainerToRegistry(imageName, tarHash);
                    _alreadyBuiltImages.Add(imageName);
                }
            } else if(_managementPod is not null && !needToBuild)
            {
                Console.WriteLine($"Method image {imageName} is already latest, destroying build daemon");
                await TearDownBuildAgent();
            }

            Console.WriteLine($"Deploying container for {imageName}");
            var containerId = await DeployContainer(imageName);
            
            return (containerId, serviceClusterIp, 5959);
        }

        public async Task<bool> Shutdown(string name)
        {
            await _kubernetesClient.DeleteNamespacedPodAsync(name, "default");

            return true;
        }

        public async Task CleanUp()
        {
            if (_managementPod is not null)
            {
                await TearDownBuildAgent();
            }
        }

        private async Task TearDownBuildAgent()
        {
            await _kubernetesClient.DeleteNamespacedPodAsync(_managementPod.Name(), _managementPod.Namespace());
            _managementPod = null;
        }

        private async Task<string> SendDockerContextToBuilder(string tarFilePath)
        {
            var tarId = Guid.NewGuid().ToString().Replace("-", "");
            var tarBytes = await File.ReadAllBytesAsync(tarFilePath);

            // Transfer TAR file
            await ExecuteBuildAgentCommand(
                new[]{ 
                    "/bin/sh", 
                    "-c", 
                    $"echo {Convert.ToBase64String(tarBytes)} | base64 -d > /home/{tarId}.tar"
                });
            
            // Create directory
            await ExecuteBuildAgentCommand(
                new[]{
                    "mkdir", 
                    $"/home/{tarId}"
                });
            
            // Extract into directory
            await ExecuteBuildAgentCommand(
                new[] {
                    "tar", 
                    "-xvf", 
                    $"/home/{tarId}.tar", 
                    "-C", 
                    $"/home/{tarId}"
                });
            
            // Delete TAR
            await ExecuteBuildAgentCommand(
                new[] {
                    "rm", 
                    "-rf", 
                    $"/home/{tarId}.tar"
                });
            
            return tarId;
        }

        private async Task BuildRemoteContainer(string tarId, string imageName, string hash)
        {
            var remoteImageName = $"{(_config.ContainerRegistryOptions.IsDockerHub ? _config.ContainerRegistryOptions.Username : _config.ContainerRegistryOptions.Host)}/{imageName}";
            await ExecuteBuildAgentCommand(
                new[] {
                    "docker", 
                    "build", 
                    "-t", 
                    remoteImageName, 
                    "-t", 
                    $"{remoteImageName}:{hash}", 
                    $"/home/{tarId}" 
                });
        }

        private async Task PushContainerToRegistry(string imageName, string hash)
        {
            var remoteImageName = $"{(_config.ContainerRegistryOptions.IsDockerHub ? _config.ContainerRegistryOptions.Username : _config.ContainerRegistryOptions.Host)}/{imageName}";

            if(_config.ContainerRegistryOptions.RequireAuthentication)
            {
                await ExecuteBuildAgentCommand(
                    new[] { 
                        "docker", 
                        "login", 
                        "-u", 
                        _config.ContainerRegistryOptions.Username, 
                        "-p", 
                        _config.ContainerRegistryOptions.Password 
                    });
            }

            await ExecuteBuildAgentCommand(
                new[] { 
                    "docker", 
                    "image", 
                    "push", 
                    remoteImageName, 
                    "--all-tags" 
                });
        }

        private async Task<string> DeployContainer(string imageName)
        {
            var remoteImageName = $"{(_config.ContainerRegistryOptions.IsDockerHub ? _config.ContainerRegistryOptions.Username : _config.ContainerRegistryOptions.Host)}/{imageName}";

            if (!_containerNames.ContainsKey(imageName))
                _containerNames.Add(imageName, 1);
            else
                _containerNames[imageName]++;

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
                            Name = $"{imageName}-{_containerNames[imageName]}",
                            Image = $"{remoteImageName}:latest",
                            Ports = new List<V1ContainerPort>
                            {
                                new V1ContainerPort(5959)
                            }
                        }
                    },
                },
                Metadata = new V1ObjectMeta
                {
                    Name = $"{imageName}-pod-{DateTime.Now.Ticks}",
                    Labels = new Dictionary<string, string> { 
                        { "app", GetAssemblyFriendlyName() }
                    }
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

            return pod.Metadata.Name;
        }

        private async Task ExecuteBuildAgentCommand(string[] commandArguments)
        {
            var shell = await _kubernetesClient.WebSocketNamespacedPodExecAsync(
                _managementPod.Name(), 
                _managementPod.Namespace(), 
                commandArguments,
                stderr: true, 
                stdout: true,
                tty: true,
                container: _managementPod.Spec.Containers.First().Name
            );

            var demuxer = new StreamDemuxer(shell);
            var std = demuxer.GetStream(1, 0);
            demuxer.Start();

            var eventWaiter = new SemaphoreSlim(0, 1);

            // TODO: Fix this trash. Need to launch a sub-process of checking when stdout is finished with a long command, releases a lock once detected
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async() =>
            {
                var blanks = new byte[512];
                while (true)
                {
                    try
                    {
                        await Task.Delay(500);
                        var read = await std.ReadAsync(blanks);
                        if (read == 0)
                        {
                            eventWaiter.Release();
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        eventWaiter.Release();
                        return;
                    }
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await eventWaiter.WaitAsync();

            if(!shell.CloseStatus.HasValue)
            {
                await shell.CloseAsync(WebSocketCloseStatus.NormalClosure, "Command complete", default(CancellationToken));
            }
        }

        private string GetAssemblyFriendlyName()
        {
            return Assembly.GetEntryAssembly().GetName().Name.Replace(".", "-").ToLower();
        }
    }

    internal class ProgressReporter : IProgress<JSONMessage>
    {
        public void Report(JSONMessage value)
        {

        }
    }
}
