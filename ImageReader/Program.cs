using ImageReader.Data;
using ImageReader.Models;
using ImageReader.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GenerativeAiOptions>(options =>
{
    options.ApiKeys = new List<string>
    {
        builder.Configuration["GeminiApiKey1"] ?? "",
        builder.Configuration["GeminiApiKey2"] ?? ""
    };
    options.UploadFolders = new List<string>
    {
        builder.Configuration["Uploads1"] ?? "Uploads1",
        builder.Configuration["Uploads2"] ?? "Uploads2"
    };
    options.SystemPrompt = builder.Configuration["SystemPrompt"] ?? "";
});

builder.Services.AddHttpClient();

builder.Services.AddSingleton<Func<string, IChatService>>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<GenerativeAiOptions>>().Value;
    return (apiKey) => new ChatService(
        sp.GetRequiredService<IHttpClientFactory>(),
        apiKey,
        opts.SystemPrompt);
});

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddScoped<IIdCardUploadService, IdCardUploadService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/process-uploads", async (
    IIdCardUploadService svc
) =>
{
    var result = await svc.ProcessUploadsAsync();
    return Results.Ok(result);
});

app.UseHttpsRedirection();
app.Run();