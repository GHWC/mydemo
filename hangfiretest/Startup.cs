using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Hangfire;
using Hangfire.Autofac;
using Hangfire.Dashboard;
using Hangfire.Dashboard.BasicAuthorization;
using Hangfire.Redis;
using Hangfire.Server;
using hangfiretest.RecurringJobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace hangfiretest
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterType(typeof(test)).As(typeof(Itest)).InstancePerDependency().PropertiesAutowired();
            var controllerBaseType = typeof(ControllerBase);
            builder.RegisterAssemblyTypes(typeof(Program).Assembly)
                .Where(t => controllerBaseType.IsAssignableFrom(t) && t != controllerBaseType)
                .PropertiesAutowired();

        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews().AddControllersAsServices();
            GlobalStateHandlers.Handlers.Add(new SucceededStateExpireHandler(int.Parse(Configuration["Hangfire:JobExpirationTimeout"])));
            services.AddHostedService<RecurringJobsService>();
            services.AddHangfire(x =>
            {
                var connectionString = Configuration["Hangfire:Redis:ConnectionString"];
                x.UseRedisStorage(connectionString, new RedisStorageOptions()
                {
                    //活动服务器超时时间 
                    InvisibilityTimeout = TimeSpan.FromMinutes(60),
                    Db = int.Parse(Configuration["Hangfire:Redis:Db"])
                });
                x.UseDashboardMetric(DashboardMetrics.ServerCount)
                    .UseDashboardMetric(DashboardMetrics.RecurringJobCount)
                    .UseDashboardMetric(DashboardMetrics.RetriesCount)
                    .UseDashboardMetric(DashboardMetrics.AwaitingCount)
                    .UseDashboardMetric(DashboardMetrics.EnqueuedAndQueueCount)
                    .UseDashboardMetric(DashboardMetrics.ScheduledCount)
                    .UseDashboardMetric(DashboardMetrics.ProcessingCount)
                    .UseDashboardMetric(DashboardMetrics.SucceededCount)
                    .UseDashboardMetric(DashboardMetrics.FailedCount)
                    .UseDashboardMetric(DashboardMetrics.DeletedCount);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthorization();
            var filter = new BasicAuthAuthorizationFilter(
        new BasicAuthAuthorizationFilterOptions
        {
            SslRedirect = false,
            RequireSsl = false,
            LoginCaseSensitive = false,
            Users = new[]
            {
                        new BasicAuthAuthorizationUser
                        {
                            Login = Configuration["Hangfire:Login"] ,
                            PasswordClear= Configuration["Hangfire:PasswordClear"]
                        }
            }
        });
            app.UseHangfireDashboard("", new DashboardOptions
            {
                Authorization = new[]
                {
                   filter
                },
            });
            var jobOptions = new BackgroundJobServerOptions
            {
                Queues = new[] { "critical", "test", "default" },
                WorkerCount = Environment.ProcessorCount * int.Parse(Configuration["Hangfire:ProcessorCount"]),
                ServerName = Configuration["Hangfire:ServerName"],
            };
            app.UseHangfireServer(jobOptions);
        }
    }
}