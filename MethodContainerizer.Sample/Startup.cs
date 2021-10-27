using MethodContainerizer.Docker.Extensions;
using MethodContainerizer.Extensions;
using MethodContainerizer.Sample.Repositories;
using MethodContainerizer.Sample.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

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
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "KubeRPC.Sample", Version = "v1" });
            });

            services
                .AddSingleton<UserService>()
                .AddSingleton<PostService>()
                .AddTransient<UserRepository>()
                .AddTransient<PostRepository>()
                .ContainerizeMethod<UserService>(x => x.CreateUser(default), 1)
                .UseDockerOrchestration()
                .BuildContainers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "KubeRPC.Sample v1"));
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
