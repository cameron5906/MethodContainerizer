using MethodContainerizer.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MethodContainerizer
{
    public sealed class MethodProxyManager
    {
        private static IOrchestrator _orchestrator;
        private static readonly Dictionary<string, List<string>> RemoteMethodIdMap = new();
        private static readonly Dictionary<string, List<int>> RemoteMethodPortMap = new();

        public static void SetOrchestrator(IOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        /// <summary>
        /// Tracks the connection information to an exported method API
        /// </summary>
        /// <param name="id">The container ID of the method</param>
        /// <param name="methodName">The name of the method</param>
        /// <param name="port">Which port the isolated method API is running on</param>
        public static void AddRemoteMethod(string id, string methodName, int port)
        {
            if (!RemoteMethodIdMap.ContainsKey(methodName))
                RemoteMethodIdMap.Add(methodName, new List<string> { id });
            else
                RemoteMethodIdMap[methodName].Add(id);

            if (!RemoteMethodPortMap.ContainsKey(methodName))
                RemoteMethodPortMap.Add(methodName, new List<int> { port });
            else
                RemoteMethodPortMap[methodName].Add(port);
        }

        /// <summary>
        /// Orders all running APIs to shut down
        /// </summary>
        internal static void ShutdownAllApis()
        {
            foreach(var openMethod in RemoteMethodIdMap)
            {
                foreach(var containerId in openMethod.Value)
                {
                    _orchestrator.Shutdown(containerId);
                }
            }
        }

        /// <summary>
        /// Called by re-written root level methods, it takes in the arguments to the original method call and POSTs them to the remote API. 
        /// The response is then deserialized to the method's return type
        /// </summary>
        public object Intercept(object[] args)
        {
            // Method metadata is passed as a string ParentType|MethodName
            var typePath = args[0] as string;
            var declaringTypeName = typePath.Split('|')[0];
            var methodName = typePath.Split('|')[1];

            // Find the method that is being proxied
            var method = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypes())
                .FirstOrDefault(x => x.FullName == declaringTypeName)?
                .GetMethod(methodName);
            
            // Get the method containerization options
            var containerizationOptions = InjectionManager.GetMethodOptions(method);
            
            // Check to see if we need to create a new container for each method call
            if (containerizationOptions.CreateAsNeeded)
            {
                var (containerId, port) = InjectionManager.BuildContainer(method, !containerizationOptions.IsOpen,
                    containerizationOptions.CustomBearer);
                Thread.Sleep(2000); //TODO: Find a way to see when the API comes up (notification?)
                
                // If it's a void method, run it in a new Task and don't worry about returning a value
                if (method.ReturnType == typeof(void))
                {
                    Task.Run(() =>
                    {
                        CallRemoteMethod(method, port, args);
                        
                        // Destroy the container once a response is received
                        _orchestrator.Shutdown(containerId).GetAwaiter().GetResult();
                    });
                    
                    return null;
                }

                var result = CallRemoteMethod(method, port, args);
                    
                // Destroy the container once a response is received
                _orchestrator.Shutdown(containerId).GetAwaiter().GetResult();

                return result;
            }
            
            // Otherwise, get one of the running instances and call it
            if (RemoteMethodPortMap.ContainsKey(methodName))
            {
                var port = RemoteMethodPortMap[methodName][0];

                // If its a void method, run the request in a new Task and don't worry about returning a value
                if (method?.ReturnType == typeof(void))
                {
                    Task.Run(() => CallRemoteMethod(method, port, args));
                    return null;
                }

                return CallRemoteMethod(method, port, args);
            }   

            var instance = FormatterServices.GetUninitializedObject(method.DeclaringType);
            return method.Invoke(instance, args.Skip(1).ToArray()); // TODO: I think this is an infinite loop. Need to store the original method...
        }

        private static object CallRemoteMethod(MethodInfo method, int port, object[] args)
        {
            var containerizationOptions = InjectionManager.GetMethodOptions(method);
            
            using var httpClient = new HttpClient();
                
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "methodcontainerizer");

            if (!containerizationOptions.IsOpen)
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {containerizationOptions.CustomBearer}");
            }

            var result = httpClient.PostAsync(
                $"http://127.0.0.1:{port}",
                new StringContent(
                    JsonConvert.SerializeObject(
                        args.Skip(1).Select(x => 
                            x
                        )
                    )
                )
            ).GetAwaiter().GetResult();

            var respStr = result.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonConvert.DeserializeObject(respStr, method.ReturnType);
        }
    }
}
