using Aspire.Hosting.Yarp.Transforms;

const string RESOURCE_PREFIX = "mi-";
var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIRECOMPUTE003
#pragma warning disable ASPIREPIPELINES003

// Replace registry URL with your own container registry
var registryUrl = "aspireapp.azurecr.io";
var registry = builder
    .AddContainerRegistry(RESOURCE_PREFIX + "container-registry", registryUrl /*"mandalay-integration.azurecr.io"*/);

var environment = builder.AddKubernetesEnvironment(RESOURCE_PREFIX + "k8s")
       .WithContainerRegistry(registry)
       .WithProperties(k8s =>
       {
           k8s.HelmChartName = "aspire-app";
           k8s.DefaultStorageType = "pvc";
           k8s.DefaultStorageClassName = "managed-csi";
           //k8s.DefaultServiceType = "LoadBalancer";
       });

var dockerEnv = builder
    .AddDockerComposeEnvironment(RESOURCE_PREFIX + "docker-engine")
    .WithContainerRegistry(registry);

var pgUser = builder.AddParameter("postgres-user", "postgres");
var pgPassword = builder.AddParameter("postgres-password", "postgres", secret: true);

var pg = builder
    .AddPostgres("postgres", pgUser, pgPassword)
    .WithComputeEnvironment(environment)
    .WithVolume("kafka-pv", "data", false)
    .PublishAsKubernetesService(configure => 
    {
        configure.Service!.Spec.Type = "LoadBalancer";
    })
    .AddDatabase("audit-log");

    var kafka = //builder.ExecutionContext.IsRunMode ?
        builder.AddKafka("kafka", 9092)
            .WithEnvironment("KAFKA_SASL_USERNAME", "root")
            .WithEnvironment("KAFKA_SASL_PASSWORD", "root")
            .WithComputeEnvironment(environment)
            .PublishAsKubernetesService(configure => 
            {
                configure.Service!.Spec.Type = "LoadBalancer";
            })
            ;


var api = builder
    .AddProject<Projects.aspire_app_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithComputeEnvironment(environment)
    .WithContainerRegistry(registry)
    .WithReference(pg)
    .WithReference(kafka)
    .WaitFor(pg)
    .WaitFor(kafka)
    .WithImagePushOptions(ctx =>
    {
        ctx.Options.RemoteImageTag = "latest";
    })
    .PublishAsKubernetesService(options =>
    {
        options.Service!.Spec.Type = "LoadBalancer";
        options.Service!.Metadata.Name = "apiservice";
    });

var webFrontEnd = builder.AddProject<Projects.aspire_app_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(api)
    .WaitFor(api)
    .WithComputeEnvironment(environment)
    .WithContainerRegistry(registry)
    .WithImagePushOptions(ctx =>
    {
        ctx.Options.RemoteImageTag = "latest";
    })
    .PublishAsKubernetesService(options =>
    {
        options.Service!.Spec.Type = "LoadBalancer";
        options.Service!.Metadata.Name = "webfrontend";
    });

var gateway = builder.AddYarp("gateway")
                     .WithComputeEnvironment(environment)
                     .WithConfiguration(yarp =>
                     {
                         yarp.AddRoute("/api/{**catch-all}", api.GetEndpoint("http"))
                             .WithTransformPathRemovePrefix("/api");

                         yarp.AddRoute(webFrontEnd.GetEndpoint("http"))
                             .WithTransformPathRemovePrefix("/web");

                     })
                    .PublishAsKubernetesService(configure =>
                    {
                        configure.Service!.Spec.Type = "LoadBalancer";
                    })
                    .WithReference(api)
                    .WithReference(webFrontEnd)
                    .WithExternalHttpEndpoints();

api.WithReference(gateway);
webFrontEnd.WithReference(gateway);

var app = builder.Build();
await app.RunAsync();
