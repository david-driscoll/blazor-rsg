using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Blazor.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rocket.Surgery.Conventions;
using Rocket.Surgery.Conventions.Reflection;
using Rocket.Surgery.Conventions.Scanners;
using Rocket.Surgery.Extensions.AutoMapper;
using Rocket.Surgery.Extensions.Configuration;
using Rocket.Surgery.Extensions.DependencyInjection;
using Rocket.Surgery.Extensions.FluentValidation;
using Rocket.Surgery.Extensions.FluentValidation.MediatR;
using Rocket.Surgery.Extensions.MediatR;
using ConfigurationBuilder = Rocket.Surgery.Extensions.Configuration.ConfigurationBuilder;
using IMsftConfigurationBuilder = Microsoft.Extensions.Configuration.IConfigurationBuilder;
using MsftConfigurationBuilder = Microsoft.Extensions.Configuration.ConfigurationBuilder;

namespace BlazorApp1.Client
{
    public static class Program
    {
        public static void Main(string[] args)
        {

            //AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
            //Console.WriteLine($"[load]: {args.LoadedAssembly.GetName()?.FullName}");
            var host = CreateHostBuilder(args).Build();


            host.Services.GetRequiredService<IServiceProviderDictionary>();
            var builder = host.Services.GetRequiredService<IRocketWasmHostBuilder>();
            foreach (var convention in builder.Scanner.BuildProvider().GetAll())
            {
                if (convention.Convention != null)
                {
                    Console.WriteLine(convention.Convention.GetType().FullName);
                }
            }
            host.Run();
        }

        public static IWebAssemblyHostBuilder CreateHostBuilder(string[] args) =>
            BlazorWebAssemblyHost.CreateDefaultBuilder()
                .ConfigureRocketSurgery(typeof(Program).Assembly, x =>
                {
                    x.PrependConvention(new AutoMapperConvention());
                    x.PrependConvention(new MediatRConvention());
                    x.PrependConvention(new FluentValidationConvention());
                    x.PrependConvention(new FluentValidationMediatRConvention());
                })
                .UseBlazorStartup<Startup>();

        public static IWebAssemblyHostBuilder ConfigureRocketSurgery(
            [NotNull] this IWebAssemblyHostBuilder builder,
            Assembly assembly,
            [NotNull] Action<IRocketWasmHostBuilder> action
        )
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            action(GetOrCreateBuilder(builder, assembly));
            return builder;
        }

        private static readonly ConditionalWeakTable<IWebAssemblyHostBuilder, RocketWasmHostBuilder> Builders =
            new ConditionalWeakTable<IWebAssemblyHostBuilder, RocketWasmHostBuilder>();
        internal static RocketWasmHostBuilder GetOrCreateBuilder(IWebAssemblyHostBuilder builder, Assembly assembly = null)
        {
            if (!Builders.TryGetValue(builder, out var conventionalBuilder))
            {
                var diagnosticSource = new DiagnosticListener("Rocket.Surgery.Blazor");
                var logger = new DiagnosticLogger(diagnosticSource);
                var serviceProviderDictionary = new ServiceProviderDictionary(builder.Properties);

                serviceProviderDictionary.Set<ILogger>(logger);
                serviceProviderDictionary.Set(HostType.Live);
                var assemblyCandidateFinder = new AppDomainAssemblyCandidateFinder(AppDomain.CurrentDomain, logger);
                var assemblyProvider = new DefaultAssemblyProvider(AppDomain.CurrentDomain.GetAssemblies().Concat(GetAllAssemblies(assembly!)).ToArray());

                static IEnumerable<Assembly> GetAllAssemblies(Assembly assembly, HashSet<Assembly> existingAssemblies = null)
                {
                    existingAssemblies ??= new HashSet<Assembly>();

                    yield return assembly;
                    existingAssemblies.Add(assembly);
                    foreach (var dependency in assembly.GetReferencedAssemblies())
                    {
                        if (dependency.Name?.StartsWith("System.", StringComparison.OrdinalIgnoreCase) == true ||
                            dependency.Name?.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) == true ||
                            dependency.Name?.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) == true ||
                            dependency.Name?.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            continue;
                        }

                        Assembly dependentAssembly;
                        try
                        {
                            dependentAssembly = Assembly.Load(dependency);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        if (existingAssemblies.Contains(dependentAssembly)) continue;

                        foreach (var item in GetAllAssemblies(dependentAssembly, existingAssemblies))
                        {
                            yield return item;
                        }
                    }

                }

                //foreach (var item in assembly!.GetReferencedAssemblies())
                //{
                //    Console.WriteLine($"[reference]: {item?.FullName}");
                //}

                foreach (var item in assemblyProvider.GetAssemblies())
                {
                    Console.WriteLine($"[assembly]: {item.GetName()?.FullName}");
                }

                var scanner = new SimpleConventionScanner(assemblyCandidateFinder, serviceProviderDictionary, logger);
                conventionalBuilder = new RocketWasmHostBuilder(
                    builder,
                    scanner,
                    assemblyCandidateFinder,
                    assemblyProvider,
                    diagnosticSource,
                    serviceProviderDictionary
                );

                IRocketEnvironment environment = new RocketEnvironment("Development", assembly.GetName().Name, null, new EmbeddedFileProvider(assembly));
                conventionalBuilder.Set(environment);

                conventionalBuilder.Set(
                    new ConfigurationOptions
                    {
                        ApplicationConfiguration =
                        {
                            b => b.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true),
                        },
                        EnvironmentConfiguration =
                        {
                            (b, environmentName) => b.AddJsonFile(
                                $"appsettings.{environmentName}.json",
                                optional: true,
                                reloadOnChange: true
                            ),
                        }
                    }
                );

                var host = new RocketContext(builder);
                builder
                    .ConfigureServices(host.ConfigureAppConfiguration)
                    .ConfigureServices(host.ConfigureServices)
                   .ConfigureServices(host.DefaultServices)
                    .ConfigureServices(x =>
                    {
                        x.AddSingleton<IRocketWasmHostBuilder>(conventionalBuilder);
                        x.AddSingleton<IServiceProviderDictionary>(serviceProviderDictionary);
                    });
                Builders.Add(builder, conventionalBuilder);
            }
            if (assembly != null)
            {
                conventionalBuilder.Set(assembly);
            }

            return conventionalBuilder;
        }
    }

    public interface IRocketWasmHostBuilder : IConventionHostBuilder
    {
        IWebAssemblyHostBuilder Builder { get; }
    }

    internal class RocketWasmHostBuilder : ConventionHostBuilder<IRocketWasmHostBuilder>, IRocketWasmHostBuilder
    {
        public RocketWasmHostBuilder(
            IWebAssemblyHostBuilder builder,
            IConventionScanner scanner,
            IAssemblyCandidateFinder assemblyCandidateFinder,
            IAssemblyProvider assemblyProvider,
            DiagnosticSource diagnosticSource,
            IServiceProviderDictionary serviceProperties
        ) : base(scanner, assemblyCandidateFinder, assemblyProvider, diagnosticSource, serviceProperties)
        {
            Builder = builder;
            Logger = new DiagnosticLogger(diagnosticSource);
        }

        public ILogger Logger { get; }

        internal RocketWasmHostBuilder With(IConventionScanner scanner) => new RocketWasmHostBuilder(
            Builder,
            scanner,
            AssemblyCandidateFinder,
            AssemblyProvider,
            DiagnosticSource,
            ServiceProperties
        );

        internal RocketWasmHostBuilder With(IAssemblyCandidateFinder assemblyCandidateFinder) => new RocketWasmHostBuilder(
            Builder,
            Scanner,
            assemblyCandidateFinder,
            AssemblyProvider,
            DiagnosticSource,
            ServiceProperties
        );

        internal RocketWasmHostBuilder With(IAssemblyProvider assemblyProvider) => new RocketWasmHostBuilder(
            Builder,
            Scanner,
            AssemblyCandidateFinder,
            assemblyProvider,
            DiagnosticSource,
            ServiceProperties
        );

        internal RocketWasmHostBuilder With(DiagnosticSource diagnosticSource) => new RocketWasmHostBuilder(
            Builder,
            Scanner,
            AssemblyCandidateFinder,
            AssemblyProvider,
            diagnosticSource,
            ServiceProperties
        );

        public IWebAssemblyHostBuilder Builder { get; }
    }
    /// <summary>
    /// Class RocketContext.
    /// </summary>
    internal class RocketContext
    {
        private readonly IWebAssemblyHostBuilder _hostBuilder;

        public RocketContext(IWebAssemblyHostBuilder hostBuilder) => _hostBuilder = hostBuilder;

        public void ConfigureAppConfiguration(WebAssemblyHostBuilderContext context, IServiceCollection services)
        {
            var rocketHostBuilder = Program.GetOrCreateBuilder(_hostBuilder);
            var _environment = rocketHostBuilder.Get<IRocketEnvironment>();

            var configurationOptions = rocketHostBuilder.GetOrAdd(() => new ConfigurationOptions());
            var configurationBuilder = new MsftConfigurationBuilder()
               .SetFileProvider(_environment.ContentRootFileProvider)
               .Apply(configurationOptions.ApplicationConfiguration)
               .Apply(configurationOptions.EnvironmentConfiguration, _environment.EnvironmentName)
               .Apply(configurationOptions.EnvironmentConfiguration, "local");

            var cb = new ConfigurationBuilder(
                rocketHostBuilder.Scanner,
                _environment,
                configurationBuilder.Build(),
                configurationBuilder,
                rocketHostBuilder.Logger,
                rocketHostBuilder.Properties
            );

            configurationBuilder.AddConfiguration(cb.Build());

            var newConfig = configurationBuilder.Build();
            rocketHostBuilder.Set<IConfiguration>(newConfig);
            services.AddSingleton<IConfiguration>(newConfig);
        }

        public void ConfigureServices(WebAssemblyHostBuilderContext context, IServiceCollection services)
        {
            var rocketHostBuilder = Program.GetOrCreateBuilder(_hostBuilder);
            services.AddSingleton(rocketHostBuilder.AssemblyCandidateFinder);
            services.AddSingleton(rocketHostBuilder.AssemblyProvider);
            services.AddSingleton(rocketHostBuilder.Scanner);
        }
        public void DefaultServices(WebAssemblyHostBuilderContext context, IServiceCollection services)
        {
            var conventionalBuilder = Program.GetOrCreateBuilder(_hostBuilder);
            _hostBuilder.UseServiceProviderFactory(
                new ServicesBuilderServiceProviderFactory(
                    collection =>
                        new ServicesBuilder(
                            conventionalBuilder.Scanner,
                            conventionalBuilder.AssemblyProvider,
                            conventionalBuilder.AssemblyCandidateFinder,
                            collection,
                            conventionalBuilder.Get<IConfiguration>(),
                            conventionalBuilder.Get<IRocketEnvironment>(),
                            conventionalBuilder.Logger,
                            conventionalBuilder.Properties
                        )
                )
            );
        }
    }

    internal static class ProxyConfigurationBuilderExtensions
    {
        public static T Apply<T>(
            this T builder,
            IEnumerable<Func<IMsftConfigurationBuilder, IMsftConfigurationBuilder>> builders
        )
            where T : IMsftConfigurationBuilder
        {
            foreach (var b in builders)
            {
                b(builder);
            }

            return builder;
        }

        public static T Apply<T>(
            this T builder,
            IEnumerable<Func<IMsftConfigurationBuilder, string, IMsftConfigurationBuilder>> builders,
            string environmentName
        )
            where T : IMsftConfigurationBuilder
        {
            foreach (var b in builders)
            {
                b(builder, environmentName);
            }

            return builder;
        }
    }
    public class ServicesBuilderServiceProviderFactory : IServiceProviderFactory<IServicesBuilder>
    {
        private readonly Func<IServiceCollection, IServicesBuilder> _func;

        public ServicesBuilderServiceProviderFactory(Func<IServiceCollection, IServicesBuilder> func) => _func = func;

        /// <summary>
        /// Creates a container builder from an <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The collection of services</param>
        /// <returns>A container builder that can be used to create an <see cref="IServiceProvider" />.</returns>
        public IServicesBuilder CreateBuilder(IServiceCollection services) => _func(services);

        /// <summary>
        /// Creates the service provider.
        /// </summary>
        /// <param name="containerBuilder">The container builder.</param>
        /// <returns>IServiceProvider.</returns>
        public IServiceProvider CreateServiceProvider([NotNull] IServicesBuilder containerBuilder)
        {
            if (containerBuilder == null)
            {
                throw new ArgumentNullException(nameof(containerBuilder));
            }

            return containerBuilder.Build();
        }
    }


    public class WasmAssemblyProvider : IAssemblyProvider
    {
        private readonly AppDomain _appDomain;

        public WasmAssemblyProvider(AppDomain? appDomain = null)
        {
            _appDomain = appDomain ?? AppDomain.CurrentDomain;
        }

        public IEnumerable<Assembly> GetAssemblies() => _appDomain.GetAssemblies();
    }
}
