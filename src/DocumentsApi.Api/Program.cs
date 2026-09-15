using System.Text.Json.Serialization;
using DocumentsApi.Core.Auth;
using DocumentsApi.Core.Data;
using DocumentsApi.Core.Options;
using DocumentsApi.Core.Pdf;
using DocumentsApi.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using PdfSharpCore.Fonts;

GlobalFontSettings.FontResolver = new EmbeddedFontResolver();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<PdfSignatureOptions>(builder.Configuration.GetSection(PdfSignatureOptions.SectionName));
builder.Services.Configure<DocumentUploadOptions>(builder.Configuration.GetSection(DocumentUploadOptions.SectionName));
builder.Services.Configure<SignaturePermissionOptions>(builder.Configuration.GetSection(SignaturePermissionOptions.SectionName));

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

// This API trusts an external identity provider (Authority/Audience below)
// rather than managing users itself. In Development, if no Authority is
// configured, fall back to a header-based dev auth scheme so the API can be
// exercised locally without a real OIDC provider - see DevHeaderAuthenticationHandler.
var useDevHeaderAuth = builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(authOptions.Authority);

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = useDevHeaderAuth ? DevHeaderAuthenticationHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = options.DefaultAuthenticateScheme;
});

if (useDevHeaderAuth)
{
    authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(
        DevHeaderAuthenticationHandler.SchemeName, _ => { });
}
else
{
    authenticationBuilder.AddJwtBearer(options =>
    {
        options.Authority = authOptions.Authority;
        options.Audience = authOptions.Audience;
        options.RequireHttpsMetadata = authOptions.RequireHttpsMetadata;
        options.TokenValidationParameters.RoleClaimType = authOptions.RoleClaimType;
        options.TokenValidationParameters.NameClaimType = authOptions.NameClaimType;
    });
}

builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("DocumentsDb")
    ?? throw new InvalidOperationException("Connection string 'DocumentsDb' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 34))));

builder.Services.AddScoped<IDocumentSignatureRepository, DocumentSignatureRepository>();
builder.Services.AddScoped<ISigningFailureLogger, SigningFailureLogger>();
builder.Services.AddScoped<IPdfSigningService, PdfSigningService>();
builder.Services.AddScoped<ISigningWorkflowService, SigningWorkflowService>();
builder.Services.AddScoped<IDocumentSigningService, DocumentSigningService>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
