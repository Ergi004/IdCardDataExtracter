using GenerativeAI;
using ImageReader.Data;
using ImageReader.Models;
using ImageReader.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// 1) Register OpenAPI (Swagger) if in Development
builder.Services.AddOpenApi();

// 2) Register DbContext (unchanged)
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure()
    )
);

// 3) Bind GenerativeAI settings from configuration
builder.Services.Configure<GenerativeAiOptions>(
    builder.Configuration.GetSection("GenerativeAI")
);

// 4) IMPORTANT: Register IHttpClientFactory so ChatService can use it
builder.Services.AddHttpClient();

// 5) Register GoogleAi singleton using the configured API key
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<GenerativeAiOptions>>().Value;
    return new GoogleAi(opts.ApiKey);
});

// 6) Register ChatService and IdCardUploadService
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IIdCardUploadService, IdCardUploadService>();

var app = builder.Build();

// 7) Map OpenAPI endpoint when in Development
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 8) Expose a GET endpoint to trigger image processing
app.MapGet("/process-uploads", async (IIdCardUploadService svc) =>
{
    // Assumes there is an "Uploads" folder in the application root
    var results = await svc.ProcessUploadsAsync("Uploads");
    return Results.Ok(results);
});

app.UseHttpsRedirection();
app.Run();
