# Method Containerizer

A library for automatically scaling a codebase by dynamically replacing class methods with containerized APIs during runtime.



## Prerequisites

Currently requires Docker to be running on the machine running your application. In the future, I plan to support remote docker instances and Kubernetes clusters.



## Usage

Add **MethodContainerizer** and **MethodContainerizer.Docker** to your project.

##### Example Startup.cs:

```csharp
services
    .AddSingleton<SomeComplexJob>()
    .ContainerizeMethod<SomeComplexJob>(x => x.Start(default))
    .UseDockerOrchestration()
    .BuildContainers();
```



##### If you would like to make sure all containers are terminated when the host closes, add the following:

```csharp
app.TerminateMethodContainersOnExit();
```



##### By default, method containers use authentication with a randomly generated Bearer token. You may disable authentication altogether:

```csharp
services
  .ContainerizeMethod<SomeComplexJob>(x => x.Start(default), opts => {
    opts.DoNotRequireAuthorization()
  })
```



##### Or, to change the bearer token:

```csharp
services
  .ContainerizeMethod<SomeComplexJob>(x => x.Start(default), opts =>
    opts.UseCustomBearerToken("mybearer")
  )
```



##### To set up a minimum amount of containers per each method for load balancing purposes:

```csharp
services
  .ContainerizeMethod<SomeComplexJob>(x => x.Start(default), opts =>
    opts.SetMinimumAvailable(3)
  )
```



##### Or, if you want each invocation of the method to create a new container that is destroyed once the method returns:

```csharp
services
  .ContainerizeMethod<SomeComplexJob>(x => x.Start(default), opts => 
    opts.AsNeeded()
  )
```



## Orchestration

##### To use with a local Docker instance:

```csharp
services
    .UseDockerOrchestration();
```



##### To use with Kubernetes with Docker Hub as a container registry:

```csharp
services
    .UseKubernetesOrchestration(opts =>
        opts.SetDockerHubRegistry("username", "password")
            .SetContext("docker-desktop")
    )
```



##### To use with Kubernetes with a custom container registry:

```csharp
services
    .UseKubernetesOrchestration(opts =>
    	opts.SetPrivateContainerRegistry("myregistry.io:5000", "username", "password")
            .SetContext("docker-desktop")
    )
```





## How it works

When you mark a method to be containerized, a lot happens behind the scenes. 

1. A new "shallow assembly" is dynamically generated containing the marked method *and only the direct dependencies needed for that method to function properly*. This means, while this library does work by creating assemblies based off of the master application, these assemblies are small, build quickly, and run fast. There is no bloat from other actions happening within the application.
2. A dynamic ASP.NET Core project is generated containing Program.cs with a main method, and a single HTTP Post controller endpoint
3. A docker image is built with the aforementioned API project and shallow assembly inside
4. A container is built and started containing the isolated method environment
5. The original method is replaced with a proxy method which calls the respective API



## Future Plans

- Add support for building of Dockerfiles remotely
- Docker Swarm
- Kubernetes
- Load balancing
- Analytics / Monitoring