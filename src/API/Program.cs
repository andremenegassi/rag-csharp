using IA.API;
using IA.API.Application.Abstractions;
using IA.API.Application.Documents;
using IA.API.Application.Questions;
using IA.API.Infrastructure.Elasticsearch;
using IA.API.Infrastructure.OpenAI;
using IA.API.Infrastructure.Options;
using IA.API.Infrastructure.Parsers;
using IA.API.Infrastructure.Rag;
using IA.API.Middleware;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

AppSettings _appSettings = new AppSettings();

#region Serilog
string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
Directory.CreateDirectory(logFolder);
Log.Logger = new LoggerConfiguration()
   .Enrich.FromLogContext()
   .WriteTo.Debug(outputTemplate: DateTime.Now.ToString())
   .WriteTo.File(new CompactJsonFormatter(), (Path.Combine(logFolder, @".json")),
        retainedFileCountLimit: 1,
        rollingInterval: RollingInterval.Day)
   .WriteTo.File((Path.Combine(logFolder, @".log")),
        retainedFileCountLimit: 1,
        rollingInterval: RollingInterval.Day)
   .CreateLogger();
Log.Information("Logger funcionado...");
#endregion



try
{
    Log.Information("Starting...");

    var builder = WebApplication.CreateBuilder(args);
    Log.Information("Serviço interno: " + builder.Configuration.GetSection("KestrelUrl").Value);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls(builder.Configuration.GetSection("KestrelUrl").Value);
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 524288000; //500MB
    });

    #region Serviço de configuração da aplicação.
    var cfcoAppSettings = new ConfigureFromConfigurationOptions<IAppSettings>(builder.Configuration.GetSection("AppSettings"));
    cfcoAppSettings.Configure(_appSettings);
    builder.Services.AddSingleton<IAppSettings>(_appSettings);
    #endregion

    builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
    builder.Services.Configure<ElasticsearchOptions>(builder.Configuration.GetSection(ElasticsearchOptions.SectionName));
    builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
    builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection(UploadOptions.SectionName));
    builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName));

    builder.Services.AddHttpClient(nameof(OpenAiSemanticClient));

    builder.Services.AddHttpClient(nameof(RagElasticsearchRepository), (sp, client) =>
    {
        var elasticsearchOptions = sp.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;

        if (!string.IsNullOrWhiteSpace(elasticsearchOptions.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("Authorization");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", elasticsearchOptions.ApiKey);
        }
    });

    builder.Services.AddMemoryCache(); 
    builder.Services.AddSingleton<IA.API.Application.Questions.IQuestionSessionMemoryStore, IA.API.Application.Questions.QuestionSessionMemoryStore>();
    builder.Services.AddSingleton<IA.API.Application.Documents.IDocumentUploadQueue, IA.API.Application.Documents.MemoryCacheDocumentUploadQueue>();

    builder.Services.AddHostedService<DocumentUploadQueueHostedService>();

    builder.Services.AddScoped<IEmbeddingService, OpenAiSemanticClient>();
    builder.Services.AddScoped<IDocumentParser, MarkdownDocumentParser>();
    builder.Services.AddScoped<IDocumentParser, PdfDocumentParser>();
    builder.Services.AddSingleton<RagTokenizer>();
    builder.Services.AddScoped<IChunkingService, ChunkingService>();
    builder.Services.AddScoped<IRagRepository, RagElasticsearchRepository>();
    builder.Services.AddScoped<FileHashService>();
    builder.Services.AddScoped<IA.API.Application.Documents.UploadDocumentUseCase>();
    builder.Services.AddScoped<IA.API.Application.Questions.RagContextBuilder>();
    builder.Services.AddScoped<IA.API.Application.Questions.AskQuestionUseCase>();
    builder.Services.AddScoped<SearchQuestionChunksUseCase>();
 
    builder.Services.AddHealthChecks();


    ConfigureServiceDocAPI(builder);

    builder.Services.AddControllers();


    builder.Services.AddCors();

    var app = builder.Build();
    _appSettings.ContentRootPath = app.Environment.ContentRootPath;

    string routePrefix = "";

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseMiddleware<ProblemDetailsMiddleware>();
    app.UseRouting();

    app.UseCors(x => x
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());

    app.UseAuthorization();
    app.UseStaticFiles();
    app.MapOpenApi();
    app.MapGet("/", (HttpContext context) => Results.Redirect($"{context.Request.PathBase}/doc"));
    app.MapScalarApiReference("/doc");
    app.MapHealthChecks("/health");
    app.MapControllers();

    app.Run();
}

catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

void ConfigureServiceDocAPI(WebApplicationBuilder builder)
{
    // gera o documento OpenAPI
    builder.Services.AddOpenApi(options =>
    {
        // habilita o uso de token
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "Provedor de serviço de IA";
            document.Info.Version = "v1";
            document.Info.Description =
                        $@"<h3>API para interagir com o provedor de IA OpenAI. Uso restrito.</h3>
                           <p>
                                O acesso aos Endpoints da API precisam ser autorizado por uma chave (APIKEY) e deve ser enviada no <b>cabeçalho da requisição HTTP</b>.
                            </p>
                            <p>
                                Exemplo: <b>Authorization &lt;Bearer APIKEY&gt; </b>
                            </p>
                            <p>
                                Versão da API {_appSettings.Version}
                            </p>";

            document.Components ??= new();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "Informe o token JWT: Bearer {token}"
            };

            if (!string.IsNullOrWhiteSpace(_appSettings.BaseURL))
            {
                document.Servers =
                [
                    new OpenApiServer
                    {
                        Url = _appSettings.BaseURL.TrimEnd('/')
                    }
                ];
            }

            return Task.CompletedTask;
        });
    });

}