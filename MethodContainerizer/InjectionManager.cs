using MethodContainerizer.Extensions;
using MethodContainerizer.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MethodContainerizer.Models;

namespace MethodContainerizer
{
    public static class InjectionManager
    {
        private static IList<MethodInfo> _injectionMethods = new List<MethodInfo>();
        private static IDictionary<string, ContainerizedMethodOptions> _injectedMethodOptions =
            new Dictionary<string, ContainerizedMethodOptions>();
        private static IOrchestrator _orchestrator;

        public static void SetOrchestrationManager(IOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        /// <summary>
        /// Adds a method to the list of methods to export
        /// </summary>
        internal static void AddMethodToInject(MethodInfo meth)
        {
            _injectionMethods.Add(meth);
        }

        internal static void AddMethodInjectionOptions(MethodInfo meth, ContainerizedMethodOptions opts)
        {
            _injectedMethodOptions.Add(meth.Name, opts);
        }

        internal static ContainerizedMethodOptions GetMethodOptions(MethodInfo meth)
        {
            return _injectedMethodOptions.ContainsKey(meth.Name) ? _injectedMethodOptions[meth.Name] : null;
        }

        /// <summary>
        /// Begins exporting and injecting replacements for the indicated methods
        /// </summary>
        internal static void BuildContainers()
        {
            if (_injectionMethods == null)
                throw new Exception("No containerized methods have been defined");

            foreach (var method in _injectionMethods)
            {
                var containerizationOptions = _injectedMethodOptions[method.Name];
                InjectMethodProxy(method);

                if (!containerizationOptions.CreateAsNeeded)
                {
                    for (var i = 0; i < containerizationOptions.MinimumAvailable; i++)
                    {
                        BuildContainer(method, !containerizationOptions.IsOpen, containerizationOptions.CustomBearer);
                    }
                }
            }
        }

        internal static (string ContainerId, int Port) BuildContainer(MethodInfo method, bool requireAuthorization,
            string bearerToken)
        {
            return BuildAndStartMethodContainer(method, requireAuthorization, bearerToken);
        }

        /// <summary>
        /// Builds a replacement method to take the place of the original, instead redirecting arguments to MethodProxy.Intercept
        /// </summary>
        /// <param name="method">The method to make remote</param>
        private static void InjectMethodProxy(MethodInfo method)
        {
            var assemblyName = $"{method.DeclaringType.Name}_Proxy";
            var name = new AssemblyName(assemblyName);
            var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);

            var generator = new Lokad.ILPack.AssemblyGenerator();

            var module = assembly.DefineDynamicModule(assemblyName);
            var typeBuilder = module.DefineType($"{method.DeclaringType.Name}ProxyContainer", TypeAttributes.Class | TypeAttributes.Public, method.DeclaringType);

            typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

            var methodParameters = method.GetParameters();

            var methodAccessor = typeBuilder.DefineMethod(
                $"{method.Name}Proxy",
                method.Attributes,
                method.CallingConvention,
                method.ReturnType,
                methodParameters.Select(x => x.ParameterType).ToArray()
            );
            methodAccessor.InitLocals = true;

            for (var i = 0; i < methodParameters.Length; i++)
            {
                var p = methodParameters[i];
                methodAccessor.DefineParameter(p.Position, p.Attributes, p.Name);
            }

            var il = methodAccessor.GetILGenerator();

            // Create an argument array for the MethodProxy
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, methodParameters.Length + 1);
            il.Emit(OpCodes.Newarr, typeof(object));

            // Append the method name as the first element
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, 0);
            il.Emit(OpCodes.Ldstr, $"{method.DeclaringType.FullName}|{method.Name}");
            il.Emit(OpCodes.Box, typeof(string));
            il.Emit(OpCodes.Stelem_Ref);

            // Build the rest of the argument array with values in the stack
            for (var i = 0; i < methodParameters.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i + 1);
                il.Emit(OpCodes.Ldarg, i + 1);
                il.Emit(OpCodes.Box, methodParameters[i].ParameterType);
                il.Emit(OpCodes.Stelem_Ref);
            }

            // Call the Intercept method on MethodProxy
            var getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle");
            il.Emit(OpCodes.Call, typeof(MethodProxyManager).GetMethod("Intercept"));

            // Build return
            il.Emit(OpCodes.Ret);

            // Build the Proxy declaring class type
            var t = typeBuilder.CreateType();

            // Get the proxied method
            var injectionMethod = t.GetMethod($"{method.Name}Proxy");

            // Replace the original method with its proxy
            method.Becomes(injectionMethod);
        }

        /// <summary>
        /// Builds a new assembly containing only method and its operational dependencies, builds a docker image, runs it, and tracks its API information
        /// </summary>
        /// <param name="method">The method to build the container for</param>
        private static (string ContainerId, int Port) BuildAndStartMethodContainer(MethodInfo method,
            bool requireAuthorization, string bearerToken)
        {
            var generatedAssembly = ShallowAssemblyBuilder.GenerateShallowMethodAssembly(method);
            var tarPath =
                DockerfileBuilder.BuildDockerContext(generatedAssembly, method, requireAuthorization, bearerToken);
            var imageName = $"{method.DeclaringType.Name}-{method.Name}".ToLower();

            var result = _orchestrator.Start(imageName, tarPath).GetAwaiter().GetResult();

            MethodProxyManager.AddRemoteMethod(result.ContainerId, method.Name, result.Port);

            return result;
        }
    }
}