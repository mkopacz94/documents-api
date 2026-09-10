using DocumentsApi.Api.Data;
using DocumentsApi.Api.Options;
using DocumentsApi.Api.Pdf;
using DocumentsApi.Api.Services;
using Microsoft.EntityFrameworkCore;
using PdfSharpCore.Fonts;

GlobalFontSettings.FontResolver = new EmbeddedFontResolver();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<PdfSignatureOptions>(builder.Configuration.GetSection(PdfSignatureOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("DocumentsDb")
    ?? throw new InvalidOperationException("Connection string 'DocumentsDb' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 34))));

builder.Services.AddScoped<IDocumentSignatureRepository, DocumentSignatureRepository>();
builder.Services.AddScoped<IPdfSigningService, PdfSigningService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
