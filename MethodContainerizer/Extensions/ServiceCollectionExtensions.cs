using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq.Expressions;

namespace MethodContainerizer.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Begins exporting and building remote method services
        /// </summary>
        public static IServiceCollection BuildContainers(this IServiceCollection services)
        {
            InjectionManager.BuildContainers();

            return services;
        }

        /// <summary>
        /// Marks as method to be exported
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="services"></param>
        /// <param name="expression">A reference to the method to add. Use "default" for parameters</param>
        /// <param name="minAvailable">The minimum amount of APIs to have at one time</param>
        public static IServiceCollection ContainerizeMethod<T>(this IServiceCollection services, Expression<Func<T, object>> expression, int minAvailable) where T : class
        {
            if(expression.Body is MethodCallExpression methodCallExpression)
            {
                InjectionManager.AddMethodToInject(methodCallExpression.Method);
            }

            return services;
        }

        /// <summary>
        /// Registers an application lifecycle hook to kill all method APIs when the main application begins shutting down
        /// </summary>
        public static IApplicationBuilder TerminateMethodContainersOnExit(this IApplicationBuilder builder)
        {
            var appLifetime = builder.ApplicationServices.GetRequiredService<IApplicationLifetime>();

            appLifetime.ApplicationStopping.Register(() =>
            {
                MethodProxyManager.ShutdownAPIs();
            });

            return builder;
        }
    }
}
