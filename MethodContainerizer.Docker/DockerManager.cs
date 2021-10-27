using Docker.DotNet;
using Docker.DotNet.Models;
using MethodContainerizer.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MethodContainerizer.Docker
{
    internal class DockerManager : IOrchestrator
    {
        private readonly IDictionary<string, int> _containerNames;
        private readonly DockerClient _dockerClient;

        public DockerManager()
        {
            _containerNames = new Dictionary<string, int>();
            _dockerClient = new DockerClientConfiguration()
                .CreateClient();
        }

        public async Task<(string ContainerId, int Port)> Start(string imageName, string dockerPath)
        {
            var tarReader = File.OpenRead(dockerPath);

            await _dockerClient.Images.BuildImageFromDockerfileAsync(new ImageBuildParameters
            {
                Tags = new[] { imageName, "kube-rpc", DateTime.Now.Ticks.ToString() },
            }, tarReader, new AuthConfig[0], new Dictionary<string, string>(), new ProgressReporter(), CancellationToken.None);
            tarReader.Close();

            var hostPort = (int)Math.Floor(6000.0 + (new Random().Next(0, 3000)));

            var instanceNumber = _containerNames.ContainsKey(imageName)
                ? _containerNames[imageName] + 1
                : 1;

            if (!_containerNames.ContainsKey(imageName))
                _containerNames.Add(imageName, instanceNumber);
            else
                _containerNames[imageName] = instanceNumber;
            
            var container = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = imageName,
                Name = $"{imageName}-inst-{instanceNumber}",
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    { "5959", new EmptyStruct() }
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        { "5959", new List<PortBinding> { new() { HostPort = hostPort.ToString() } } }
                    }
                }
            });

            await _dockerClient.Containers.StartContainerAsync(container.ID, new ContainerStartParameters());

            return (container.ID, hostPort);
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
