﻿using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Service.Configuration;
using Service.Messaging;

namespace Service
{
    [ExcludeFromCodeCoverage]
    public class Startup
    {
        ArchiveProcessorConfig Config { get; }

        public Startup()
        {
            Config = new ArchiveProcessorConfig();

            var builder = new ConfigurationBuilder()
                .AddJsonFile("config/appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            builder.Bind(Config);

            if (String.IsNullOrEmpty(Config.MessageBrokerUser))
            {
                Config.MessageBrokerUser = "guest";
            }

            if (String.IsNullOrEmpty(Config.MessageBrokerPassword))
            {
                Config.MessageBrokerPassword = "guest";
            }
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IAdaptationOutcomeSender, AdaptationOutcomeSender>();
            services.AddTransient<IArchiveProcessor, ArchiveProcessor>();
            services.AddSingleton<IArchiveProcessorConfig>(Config);
        }
    }
}