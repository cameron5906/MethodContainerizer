using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq.Expressions;
using MethodContainerizer.Models;

namespace MethodContainerizer.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Begins exporting and building remote method services
        /// </summary>
        public static IServiceCollection BuildContainers(this IServiceCollection services)
        {
            services.Configure<HostOptions>(opts =>
            {
                opts.ShutdownTimeout = TimeSpan.FromSeconds(30);
            });
            
            InjectionManager.BuildContainers();

            return services;
        }

        /// <summary>
        /// Marks as method to be exported
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="services"></param>
        /// <param name="expression">A reference to the method to add. Use "default" for parameters</param>
        /// <param name="optionsBuilder">An action used to build specific properties of the containerized method</param>
        public static IServiceCollection ContainerizeMethod<T>(this IServiceCollection services,
            Expression<Func<T, object>> expression,
            Action<ContainerizedMethodOptionsBuilder> optionsBuilder = null) where T : class
        {
            // Do nothing if a valid method was not found in the expression (TODO: Throw?)
            if (expression.Body is not MethodCallExpression methodCallExpression) return services;
            
            // Add a record to inject the method
            InjectionManager.AddMethodToInject(methodCallExpression.Method);

            // If no additional options, return now
            if (optionsBuilder is null)
            {
                InjectionManager.AddMethodInjectionOptions(methodCallExpression.Method, new ContainerizedMethodOptions
                {
                    MinimumAvailable = 1,
                    CreateAsNeeded = false,
                    IsOpen = false,
                    CustomBearer = Guid.NewGuid().ToString()
                });
            }
                
            // Otherwise, build the options and record them
            var optionsBuilderInst = new ContainerizedMethodOptionsBuilder();
            optionsBuilder(optionsBuilderInst);
            var options = optionsBuilderInst.Build();   
            InjectionManager.AddMethodInjectionOptions(methodCallExpression.Method, options);

            return services;
        }

        /// <summary>
        /// Registers an application lifecycle hook to kill all method APIs when the main application begins shutting down
        /// </summary>
        public static IApplicationBuilder TerminateMethodContainersOnExit(this IApplicationBuilder builder)
        {   
            var appLifetime = builder.ApplicationServices.GetRequiredService<IApplicationLifetime>();

            appLifetime.ApplicationStopping.Register(MethodProxyManager.ShutdownAllApis);

            return builder;
        }
    }
}
