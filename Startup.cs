using System;
using System.Linq;
using System.Text;
using App.Services;
using App.Options;
using App.Middlewares;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Serialization;

namespace App
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add options
            services.AddOptions();

            // Add memory cache support
            services.AddMemoryCache();

            // Add framework services.
            services.AddMvc(config =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                config.Filters.Add(new AuthorizeFilter(policy));
            }).AddJsonOptions(option =>
            {
                option.SerializerSettings.ContractResolver = new AppContractResolver {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                };
            });

            // Configure Database
            var databaseConfiguration = Configuration.GetSection("Database").GetSection("Default");
            services.AddSingleton(typeof(Database),
                new Database(
                    databaseConfiguration["Engine"],
                    databaseConfiguration["ConnectionString"]
                )
            );

            // Configure JwtIssuerOptions
            services.Configure<JwtConfig>(options =>
            {
                var jwtAppSettingOptions = Configuration.GetSection(nameof(JwtConfig));

                options.SigningKey = jwtAppSettingOptions[nameof(JwtConfig.SigningKey)];
                options.Issuer = jwtAppSettingOptions[nameof(JwtConfig.Issuer)];
                options.Audience = jwtAppSettingOptions[nameof(JwtConfig.Audience)];
                options.SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.ASCII.GetBytes(options.SigningKey)),
                    SecurityAlgorithms.HmacSha256
                );
            });

            // Use policy auth.
            services.AddAuthorization(options =>
            {
                options.AddPolicy("Admin",
                    policy => policy.RequireClaim(AppConfig.AuthRoleIdentifierName, "Admin"));
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            // Add logging for development
            if (env.IsDevelopment())
            {
                loggerFactory.AddConsole(Configuration.GetSection("Logging"));
                loggerFactory.AddDebug();
            }

            // JSON error response
            app.UseMiddleware(typeof(ErrorHandlerMiddleware));

            // JWT
            var jwtAppSettingOptions = Configuration.GetSection(nameof(JwtConfig));
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtAppSettingOptions[nameof(JwtConfig.Issuer)],
                ValidateAudience = true,
                ValidAudience = jwtAppSettingOptions[nameof(JwtConfig.Audience)],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.ASCII.GetBytes(
                        jwtAppSettingOptions[nameof(JwtConfig.SigningKey)]
                    )
                ),
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
            var jwtBearerOptions = new JwtBearerOptions
            {
                AutomaticAuthenticate = true,
                AutomaticChallenge = true,
                TokenValidationParameters = tokenValidationParameters
            };
            var tokenValidator = new TokenValidator
            {
                Cache = (IMemoryCache) app.ApplicationServices.GetService(typeof(IMemoryCache))
            };

            jwtBearerOptions.SecurityTokenValidators.Clear();
            jwtBearerOptions.SecurityTokenValidators.Add(tokenValidator);

            app.UseJwtBearerAuthentication(jwtBearerOptions);
            // End of JWT

            app.UseMvc();
        }
    }
}
