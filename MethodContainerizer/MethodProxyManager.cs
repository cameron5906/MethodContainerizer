using MethodContainerizer.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;

namespace MethodContainerizer
{
    public class MethodProxyManager
    {
        private static IOrchestrator _orchestrator;
        private static Dictionary<string, IList<string>> _remoteMethodIdMap = new Dictionary<string, IList<string>>();
        private static Dictionary<string, IList<int>> _remoteMethodPortMap = new Dictionary<string, IList<int>>();

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
            if (!_remoteMethodIdMap.ContainsKey(methodName))
                _remoteMethodIdMap.Add(methodName, new[] { id });
            else
                _remoteMethodIdMap[methodName].Add(id);

            if (!_remoteMethodPortMap.ContainsKey(methodName))
                _remoteMethodPortMap.Add(methodName, new[] { port });
            else
                _remoteMethodPortMap[methodName].Add(port);
        }

        /// <summary>
        /// Orders all running APIs to shut down
        /// </summary>
        public static void ShutdownAPIs()
        {
            foreach(var openMethod in _remoteMethodIdMap)
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

            if (_remoteMethodPortMap.ContainsKey(methodName))
            {
                var port = _remoteMethodPortMap[methodName][0];
                using var httpClient = new HttpClient();
                
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                httpClient.DefaultRequestHeaders.Add("User-Agent", "kuberpc-client");

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

            var instance = FormatterServices.GetUninitializedObject(method.DeclaringType);
            return method.Invoke(instance, args.Skip(1).ToArray()); // TODO: I think this is an infinite loop. Need to store the original method...
        }
    }
}
