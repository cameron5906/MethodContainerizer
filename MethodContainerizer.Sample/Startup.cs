using MethodContainerizer.Docker.Extensions;
using MethodContainerizer.Extensions;
using MethodContainerizer.Kubernetes.Extensions;
using MethodContainerizer.Sample.Repositories;
using MethodContainerizer.Sample.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MethodContainerizer.Sample
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddControllers();

            services
                .AddSingleton<UserService>()
                .AddSingleton<PostService>()
                .AddTransient<UserRepository>()
                .AddTransient<PostRepository>()
                .ContainerizeMethod<UserService>(x => x.CreateUser(default), opts => 
                    opts
                        .SetMinimumAvailable(3)
                        .UseCustomBearerToken("mytesttoken")
                )
                .UseKubernetesOrchestration("", "")
                .BuildContainers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.TerminateMethodContainersOnExit();
        }
    }
}
