using ImageReader.Data;
using ImageReader.Models;
using ImageReader.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GenerativeAiOptions>(opts =>
{
    opts.ApiKeys = new List<string>
    {
        builder.Configuration["GeminiApiKey1"]!,
        builder.Configuration["GeminiApiKey2"]!
    };
    opts.UploadFolders = new List<string>
    {
        builder.Configuration["UploadsFolder1"] ?? "Uploads1",
        builder.Configuration["UploadsFolder2"] ?? "Uploads2"
    };
    opts.SystemPrompt = builder.Configuration["SystemPrompt"] ?? "";
});

builder.Services.AddHttpClient();

builder.Services.AddSingleton<IChatService>(sp =>
{
    var cfg  = sp.GetRequiredService<IOptions<GenerativeAiOptions>>().Value;
    var key1 = cfg.ApiKeys[0];
    return new ChatService(
        sp.GetRequiredService<IHttpClientFactory>(),
        key1,
        cfg.SystemPrompt);
});
builder.Services.AddSingleton<IChatService>(sp =>
{
    var cfg  = sp.GetRequiredService<IOptions<GenerativeAiOptions>>().Value;
    var key2 = cfg.ApiKeys[1];
    return new ChatService(
        sp.GetRequiredService<IHttpClientFactory>(),
        key2,
        cfg.SystemPrompt);
});

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddScoped<IIdCardUploadService, IdCardUploadService>();



builder.Services.AddScoped<IJsonToCsvService, JsonToCsvService>();
var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/process-uploads", async (
    IIdCardUploadService svc,
    IOptions<GenerativeAiOptions> cfg
) =>
{
    var folders = cfg.Value.UploadFolders;
    var result = await svc.ProcessUploadsAsync(folders);
    return Results.Ok(result);
});

app.MapGet("/generate-csv", async (IJsonToCsvService csvService) =>
{
    try
    {
        var filePath = await csvService.CreateCsvFromJsonsAsync("id_cards_summary.csv");
        return Results.Ok(new { message = "CSV generated successfully.", path = filePath });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError
        );
    }
});


app.UseHttpsRedirection();
app.Run();
